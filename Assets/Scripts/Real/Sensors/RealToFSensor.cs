using System;
using System.Globalization;
using System.IO;
using UnityEngine;

/// <summary>
/// VL53L0X time-of-flight sensor over the Pico link. Payload is an int32 distance
/// in MILLIMETRES; converted to metres and range-checked (40–600 mm). Out-of-range
/// is reported as no-target. Channel: PicoChannels.ToF.
/// </summary>
public class RealToFSensor : RealSensorBase
{
    [Header("ToF (real) — VL53L0X")]
    [Tooltip("Valid range in millimetres; readings outside report no-target.")]
    [SerializeField] private float minRangeMm = 40f;
    [SerializeField] private float maxRangeMm = 600f;
    [Tooltip("Low-pass smoothing time-constant (s) on the distance, matching DigitalToFSensor so " +
             "sim and twin behave alike. Keep small (<< a bump's duration). 0 = off.")]
    [SerializeField] private float smoothingTau = 0.01f;
    [SerializeField] private ToFSensorOutput tofOutput;

    [Header("Raw logging")]
    [Tooltip("When enabled, append every raw packet (time + raw mm) to a timestamped CSV in the " +
             "user's Documents folder. Logs the unfiltered reading — before range-check, EMA, and " +
             "rolling average.")]
    [SerializeField] private bool logRawToCsv = false;

    private Ema _ema;
    private RollingAverage _avg;
    private float _lastTime;

    private StreamWriter _csv;
    private string _csvPath;

    protected override void Decode(SensorPacket packet)
    {
        if (tofOutput == null || packet.Payload.Length < 4) return;
        _avg.Configure(rollingAverageWindow);

        int mm = PicoChannelCodec.DecodeToFMm(packet);
        LogRaw(packet.Timestamp, mm);

        if (mm < minRangeMm || mm > maxRangeMm)
        {
            _ema.Reset(); _avg.Reset(); _lastTime = Time.time;   // gap: don't blend across a no-target
            tofOutput.PublishNoTarget();
        }
        else
        {
            float m = _ema.Step(mm / 1000f, Time.time - _lastTime, smoothingTau); _lastTime = Time.time;
            tofOutput.Publish(_avg.Add(m));   // mm -> m, low-pass (matches DigitalToFSensor), then rolling avg
        }
    }

    // Append one raw sample (time + raw distance mm) to the CSV, opening it lazily on first use.
    private void LogRaw(float timestamp, float rawMm)
    {
        if (!logRawToCsv) return;
        if (_csv == null)
        {
            try
            {
                string dir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                _csvPath = Path.Combine(dir, $"real_tof_raw_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                _csv = new StreamWriter(_csvPath, append: false) { AutoFlush = true };
                _csv.WriteLine("time_s,raw_mm");
               // Debug.Log($"[RealToF] logging raw ToF data to {_csvPath}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RealToF] could not open raw CSV: {ex.Message}");
                logRawToCsv = false;   // stop retrying every packet
                return;
            }
        }
        _csv.WriteLine($"{timestamp.ToString("R", CultureInfo.InvariantCulture)}," +
                       $"{rawMm.ToString("R", CultureInfo.InvariantCulture)}");
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        if (_csv != null)
        {
            try { _csv.Flush(); _csv.Dispose(); } catch { }
            _csv = null;
            Debug.Log($"[RealToF] closed raw ToF log {_csvPath}");
        }
    }
}
