using UnityEngine;

/// <summary>
/// MPU-6050 accelerometer over the Pico link. The transport packs the three
/// acceleration components as little-endian floats in g; this converts to m/s²
/// so the digital and real signals share the same units on the output.
/// Channel: PicoChannels.Accel.
/// </summary>
public class RealAccelerometer : RealSensorBase
{
    private const float GToMs2 = 9.81f;   // 1 g -> m/s²

    [Header("Accelerometer (real) — MPU-6050")]
    [SerializeField] private AccelerometerOutput accelOutput;

    protected override void Decode(SensorPacket packet)
    {
        if (accelOutput == null || packet.Payload.Length < 12) return;

        Vector3 g = PicoChannelCodec.DecodeAccelG(packet);
        accelOutput.Publish(g * GToMs2);
    }
}
