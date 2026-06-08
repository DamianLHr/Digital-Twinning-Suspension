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
    [SerializeField] private ToFSensorOutput tofOutput;

    protected override void Decode(SensorPacket packet)
    {
        if (tofOutput == null || packet.Payload.Length < 4) return;

        int mm = PicoChannelCodec.DecodeToFMm(packet);
        if (mm < minRangeMm || mm > maxRangeMm)
            tofOutput.PublishNoTarget();
        else
            tofOutput.Publish(mm / 1000f);   // mm -> m
    }
}
