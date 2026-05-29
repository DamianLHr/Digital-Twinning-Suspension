using UnityEngine;

namespace Suspension.Sensors
{
    /// <summary>
    /// VL53L0X time-of-flight sensor. Payload is one little-endian float per
    /// packet: distance in metres. Negative values are reported as no-target.
    /// </summary>
    public class RealToFSensor : RealSensorBase
    {
        [Header("ToF (real) — VL53L0X")]
        [SerializeField] private float maxRange = 2.0f;
        [SerializeField] private ToFSensorOutput tofOutput;

        protected override void Decode(SensorPacket packet)
        {
            if (tofOutput == null || packet.Payload.Length < 4) return;

            float d = packet.ReadFloat(0);

            if (d < 0f || d > maxRange)
                tofOutput.PublishNoTarget();
            else
                tofOutput.Publish(d);
        }
    }
}
