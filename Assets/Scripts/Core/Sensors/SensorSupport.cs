using System;
using UnityEngine;
using UnityEngine.Events;

/// <summary>Which implementation feeds a given output (used by the binding/UI layer).</summary>
public enum SensorSource { Digital, Real }

// ---- Concrete UnityEvent subclasses ------------------------------------
// Generic UnityEvent<T> only serializes (and shows in the Inspector) when
// wrapped in a concrete, [Serializable] subclass. These are the typed
// channels every *Output broadcasts on.
[Serializable] public class FloatEvent      : UnityEvent<float>   {}
[Serializable] public class FloatArrayEvent : UnityEvent<float[]> {}
[Serializable] public class Vector3Event    : UnityEvent<Vector3> {}

/// <summary>
/// One framed message from the microcontroller, already de-framed by the
/// serial layer. Payload is little-endian; use the Read* helpers.
/// </summary>
public readonly struct SensorPacket
{
    public readonly byte    ChannelId;
    public readonly float   Timestamp;   // device timestamp (s)
    public readonly byte[]  Payload;

    public SensorPacket(byte channelId, float timestamp, byte[] payload)
    {
        ChannelId = channelId;
        Timestamp = timestamp;
        Payload   = payload ?? Array.Empty<byte>();
    }

    public float ReadFloat(int offset) => BitConverter.ToSingle(Payload, offset);
    public int   ReadInt(int offset)   => BitConverter.ToInt32(Payload, offset);
    public short ReadShort(int offset) => BitConverter.ToInt16(Payload, offset);
    public int   FloatCount            => Payload.Length / 4;
}

/// <summary>
/// Implemented by the serial layer (e.g. SerialPortManager). Real* sensors
/// subscribe to it by channelId in OnEnable. This is the only coupling
/// point between the sensor family and the comms subsystem — wire your
/// existing decoder to this interface.
/// </summary>
public interface ISensorPacketSource
{
    void Subscribe(byte channelId, Action<SensorPacket> handler);
    void Unsubscribe(byte channelId, Action<SensorPacket> handler);
}

/// <summary>
/// First-order exponential low-pass (smoothing). Time-constant based, so a given tau gives the
/// same real-time response regardless of the caller's sample rate — the digital ToF (200 Hz) and
/// the real ToF (USB rate) therefore smooth identically for the same tau. Used to model the real
/// VL53L0X's internal averaging without flattening bumps (keep tau much smaller than a bump's
/// duration). A value type; hold one as a field and call Step() each sample.
/// </summary>
public struct Ema
{
    private float _v;
    private bool _init;
    public float Value => _v;
    public void Reset() => _init = false;      // call on a no-target gap so we don't blend across it
    public float Step(float sample, float dt, float tau)
    {
        if (!_init || tau <= 0f || dt <= 0f) { _v = sample; _init = true; }
        else _v += (1f - Mathf.Exp(-dt / tau)) * (sample - _v);
        return _v;
    }
}
