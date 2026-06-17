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

    private Ema _ema;
    private float _lastTime;

    protected override void Decode(SensorPacket packet)
    {
        if (tofOutput == null || packet.Payload.Length < 4) return;

        int mm = PicoChannelCodec.DecodeToFMm(packet);
        if (mm < minRangeMm || mm > maxRangeMm)
        {
            _ema.Reset(); _lastTime = Time.time;   // gap: don't blend across a no-target
            tofOutput.PublishNoTarget();
        }
        else
        {
            float m = _ema.Step(mm / 1000f, Time.time - _lastTime, smoothingTau); _lastTime = Time.time;
            tofOutput.Publish(m);   // mm -> m, low-pass (matches DigitalToFSensor)
        }
    }
}
