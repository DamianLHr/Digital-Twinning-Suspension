using UnityEngine;

namespace Suspension.Sensors
{
    /// <summary>
    /// MPU-6050 IMU. Payload layout: 3 floats accel (m/s^2) then 3 floats gyro
    /// (rad/s), little-endian. If your firmware sends raw int16 counts instead,
    /// switch to ReadShort() and scale by gRange / dpsRange.
    /// </summary>
    public class RealIMU : RealSensorBase
    {
        [Header("IMU (real) — MPU-6050")]
        [SerializeField] private float gRange   = 16.0f;   // +/- g  (for raw-count decode)
        [SerializeField] private float dpsRange = 2000f;   // +/- dps(for raw-count decode)
        [SerializeField] private AccelerometerOutput accelOutput;
        [SerializeField] private GyroOutput gyroOutput;

        protected override void Decode(SensorPacket packet)
        {
            if (packet.Payload.Length < 24) return;

            if (accelOutput != null)
            {
                Vector3 a = new Vector3(packet.ReadFloat(0), packet.ReadFloat(4), packet.ReadFloat(8));
                accelOutput.Publish(a);
            }

            if (gyroOutput != null)
            {
                Vector3 w = new Vector3(packet.ReadFloat(12), packet.ReadFloat(16), packet.ReadFloat(20));
                gyroOutput.Publish(w);
            }
        }
    }
}
