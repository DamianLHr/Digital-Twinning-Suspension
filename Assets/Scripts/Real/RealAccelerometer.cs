using UnityEngine;

/// <summary>
/// MPU-6050 accelerometer. Payload layout: 3 floats accel (m/s^2),
/// little-endian. If your firmware sends raw int16 counts instead, switch to
/// ReadShort() and scale by gRange.
/// </summary>
public class RealAccelerometer : RealSensorBase
{
    [Header("Accelerometer (real) — MPU-6050")]
    [SerializeField] private float gRange = 16.0f;   // +/- g  (for raw-count decode)
    [SerializeField] private AccelerometerOutput accelOutput;

    protected override void Decode(SensorPacket packet)
    {
        if (accelOutput == null || packet.Payload.Length < 12) return;

        Vector3 a = new Vector3(packet.ReadFloat(0), packet.ReadFloat(4), packet.ReadFloat(8));
        accelOutput.Publish(a);
    }
}
