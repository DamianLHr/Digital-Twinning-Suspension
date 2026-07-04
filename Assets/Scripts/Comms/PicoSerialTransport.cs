using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Ports;
using System.Threading;
using UnityEngine;

/// <summary>
/// The serial layer for the Raspberry Pi Pico. Implements BOTH the inbound
/// (ISensorPacketSource) and outbound (IActuatorPacketSink) coupling points, so
/// the Real* sensors/actuators plug straight in.
///
///   • Reads on a BACKGROUND THREAD (blocking serial), de-frames TwinData, and
///     enqueues it; the main thread drains the queue in Update and DEMUXes one
///     TwinData into per-channel SensorPackets dispatched to subscribers.
///   • Send() MUXes per-actuator commands into a single held CommandData and
///     writes the framed struct.
///
/// Requires the project's API Compatibility Level set to ".NET Framework"
/// (System.IO.Ports is not in .NET Standard). No Unity API is touched off-thread.
/// </summary>
[DisallowMultipleComponent]
public class PicoSerialTransport : MonoBehaviour, ISensorPacketSource, IActuatorPacketSink, IRealDevice
{
    [Header("Port")]
    [Tooltip("e.g. /dev/ttyACM0 on Linux, COM3 on Windows.")]
    [SerializeField] private string portName = "/dev/ttyACM0";
    [SerializeField] private int baudRate = 115200;
    [SerializeField] private bool autoConnect = true;
    [SerializeField] private float reconnectInterval = 2f;
    [SerializeField] private int readTimeoutMs = 500;
    [SerializeField] private int writeTimeoutMs = 500;

    [Header("Robustness")]
    [Tooltip("Skip frames whose every sensor field is exactly zero (a corrupt/blank packet). " +
             "Subscribers then keep their last good value instead of glitching to zero for a " +
             "frame.")]
    [SerializeField] private bool rejectBlankFrames = true;
    [Tooltip("Skip frames containing physically-impossible values (negative distance/pots, " +
             "out-of-range pots, NaN/huge accel). These come from a brief framing mis-sync " +
             "(a dropped byte or a false header in the payload) — the parser re-syncs on the " +
             "next header, this just suppresses the corrupt frame so subscribers hold last good.")]
    [SerializeField] private bool rejectImplausibleFrames = true;
    [Tooltip("Upper bound (mm) for a plausible ToF reading. Negative is always rejected; this " +
             "catches misframed garbage. Generous — the sensor does its own useful-range check.")]
    [SerializeField] private int maxDistanceMm = 50000;
    [Tooltip("Full ADC scale; pot raw outside 0..adcMax is non-physical (12-bit = 4096).")]
    [SerializeField] private int adcMax = 4096;
    [Tooltip("Max plausible |acceleration| per axis, in g. Above this (or NaN/Inf) = corrupt.")]
    [SerializeField] private float maxAccelG = 32f;

    [Header("Diagnostics (read-only)")]
    [SerializeField] private bool connected;
    [SerializeField] private int packetsReceived;
    [SerializeField] private int droppedPackets;     // inferred from packet_id gaps
    [SerializeField] private int blankFrames;        // all-zero frames skipped
    [SerializeField] private int badFrames;          // implausible (misframed) frames skipped
    [SerializeField] private uint lastPacketId;

    public bool Connected => _port != null && _port.IsOpen;
    public int PacketsReceived => packetsReceived;
    public int DroppedPackets => droppedPackets;

    // Public config surface for the setup UI. Set these, then call Connect() to apply.
    public string PortName { get => portName; set => portName = value; }
    public int BaudRate    { get => baudRate; set => baudRate = value; }

    private SerialPort _port;
    private Thread _reader;
    private volatile bool _running;
    private volatile bool _needsReconnect;
    private float _reconnectAt;

    private readonly PicoFrameParser _parser = new PicoFrameParser();
    private readonly ConcurrentQueue<TwinData> _inbound = new ConcurrentQueue<TwinData>();

    private readonly Dictionary<byte, Action<SensorPacket>> _subs =
        new Dictionary<byte, Action<SensorPacket>>();

    private readonly object _writeLock = new object();
    private CommandData _outState;
    private bool _havePacketId;
    private bool _loggedBlank;   // one-shot blank-frame diagnostic per connection

    // ---- lifecycle ----

    private void OnEnable() { if (autoConnect) Connect(); }
    private void OnDisable() => Disconnect();

    public bool Connect()
    {
        Disconnect();
        try
        {
            _port = new SerialPort(portName, baudRate)
            {
                DtrEnable = true,
                RtsEnable = true,
                ReadTimeout = readTimeoutMs,
                WriteTimeout = writeTimeoutMs
            };
            _port.Open();
            _running = true;
            _needsReconnect = false;
            _havePacketId = false;
            _loggedBlank = false;
            _parser.Reset();
            _reader = new Thread(ReadLoop) { IsBackground = true, Name = "PicoSerialReader" };
            _reader.Start();
            connected = true;
            Debug.Log($"[Pico] connected to {portName} @ {baudRate}.");
            return true;
        }
        catch (Exception ex)
        {
            connected = false;
            Debug.LogWarning($"[Pico] connect failed: {ex.Message}");
            ScheduleReconnect();
            return false;
        }
    }

    public void Disconnect()
    {
        _running = false;
        if (_reader != null && _reader.IsAlive) { try { _reader.Join(200); } catch { } }
        _reader = null;
        if (_port != null)
        {
            try { if (_port.IsOpen) _port.Close(); } catch { }
            _port.Dispose();
            _port = null;
        }
        connected = false;
    }

    // ---- background read ----

    private void ReadLoop()
    {
        var chunk = new byte[256];
        while (_running)
        {
            try
            {
                int n = _port.Read(chunk, 0, chunk.Length);   // blocks up to ReadTimeout
                for (int i = 0; i < n; i++)
                    if (_parser.Feed(chunk[i], out TwinData frame)) _inbound.Enqueue(frame);
            }
            catch (TimeoutException) { /* no data this window — keep polling */ }
            catch (Exception)
            {
                _running = false;
                _needsReconnect = true;   // port likely dropped (e.g. USB unplug)
            }
        }
    }

    // ---- main-thread pump ----

    private void Update()
    {
        while (_inbound.TryDequeue(out TwinData d)) Dispatch(d);

        if (_needsReconnect && autoConnect && Time.unscaledTime >= _reconnectAt)
        {
            connected = false;
            Connect();
        }
        connected = Connected;
    }

    private void Dispatch(TwinData d)
    {
        // Skip the intermittent all-zero frame so every subscriber holds its last
        // good value instead of dropping to zero for a frame.
        if (rejectBlankFrames && IsBlank(d))
        {
            blankFrames++;
            if (!_loggedBlank)
            {
                _loggedBlank = true;   // log only the FIRST one this connection
                Debug.LogWarning(
                    $"[Pico] first blank (all-zero) frame — packet_id={d.packet_id}. " +
                    (d.packet_id != 0
                        ? "id is valid → likely a firmware sensor-read miss (zeroed data), not misframing."
                        : "id is also zero → likely a framing/TX-timing issue (entire struct zero)."));
            }
            return;
        }

        if (rejectImplausibleFrames && IsImplausible(d))
        {
            badFrames++;
            return;
        }

        packetsReceived++;
        if (_havePacketId)
        {
            uint gap = d.packet_id - lastPacketId;   // unsigned: wraps correctly
            if (gap > 1) droppedPackets += (int)(gap - 1);
        }
        lastPacketId = d.packet_id;
        _havePacketId = true;

        float ts = Time.time;
        Emit(PicoChannels.Accel, ts, PicoChannelCodec.EncodeAccel(d.accel_x, d.accel_y, d.accel_z));
        Emit(PicoChannels.ToF,   ts, PicoChannelCodec.EncodeToFMm(d.distance_mm));
        Emit(PicoChannels.Pot1,  ts, PicoChannelCodec.EncodePotRaw(d.analog_1_raw));
        Emit(PicoChannels.Pot2,  ts, PicoChannelCodec.EncodePotRaw(d.analog_2_raw));
    }

    // A frame with every sensor field exactly zero is non-physical (a mounted
    // accelerometer always reads ~1 g; ToF distance is 40–600 mm), so it's a
    // corrupt/blank packet rather than real data.
    private static bool IsBlank(in TwinData d) =>
        d.accel_x == 0f && d.accel_y == 0f && d.accel_z == 0f &&
        d.distance_mm == 0 && d.analog_1_raw == 0 && d.analog_2_raw == 0;

    // A frame is implausible (corrupt/misframed) if any field is non-physical:
    // ToF distance and ADC counts can never be negative, pots can't exceed full
    // scale, and accel can't be NaN/Inf or beyond the sensor's range.
    private bool IsImplausible(in TwinData d)
    {
        if (d.distance_mm < 0 || d.distance_mm > maxDistanceMm) return true;
        if (d.analog_1_raw < 0 || d.analog_1_raw > adcMax) return true;
        if (d.analog_2_raw < 0 || d.analog_2_raw > adcMax) return true;
        if (!IsAccelSane(d.accel_x) || !IsAccelSane(d.accel_y) || !IsAccelSane(d.accel_z)) return true;
        return false;
    }

    private bool IsAccelSane(float g) =>
        !float.IsNaN(g) && !float.IsInfinity(g) && Mathf.Abs(g) <= maxAccelG;

    private void Emit(byte channel, float ts, byte[] payload)
    {
        if (_subs.TryGetValue(channel, out var handler))
            handler?.Invoke(new SensorPacket(channel, ts, payload));
    }

    private void ScheduleReconnect()
    {
        _needsReconnect = true;
        _reconnectAt = Time.unscaledTime + Mathf.Max(0.25f, reconnectInterval);
    }

    // ---- ISensorPacketSource (main thread) ----

    public void Subscribe(byte channelId, Action<SensorPacket> handler)
    {
        _subs.TryGetValue(channelId, out var existing);
        _subs[channelId] = existing + handler;
    }

    public void Unsubscribe(byte channelId, Action<SensorPacket> handler)
    {
        if (!_subs.TryGetValue(channelId, out var existing)) return;
        var combined = existing - handler;
        if (combined == null) _subs.Remove(channelId);
        else _subs[channelId] = combined;
    }

    // ---- IActuatorPacketSink ----

    // Each Real* actuator sends an int on its own channel; we merge them into the
    // single CommandData frame the device expects and write it.
    public void Send(ActuatorPacket packet)
    {
        if (packet.Payload == null || packet.Payload.Length < 4) return;
        int value = BitConverter.ToInt32(packet.Payload, 0);

        lock (_writeLock)
        {
            if (packet.ChannelId == PicoChannels.Damping)   _outState.target_steps = value;
            else if (packet.ChannelId == PicoChannels.Belt) _outState.belt_command = value;
            else return;

            WriteCommand(_outState);
        }
    }

    private void WriteCommand(CommandData cmd)
    {
        if (_port == null || !_port.IsOpen) return;
        try
        {
            byte[] frame = PicoProtocol.FrameCommand(cmd);
            _port.Write(frame, 0, frame.Length);
        }
        catch (TimeoutException) { Debug.LogWarning("[Pico] write timed out — is the device listening?"); }
        catch (Exception) { _needsReconnect = true; }
    }
}
