using UnityEngine;

/// <summary>
/// MPU-6050 accelerometer over the Pico link. The transport packs the three
/// acceleration components as little-endian floats in g, and they are published
/// in g (no conversion) — the digital accelerometer matches. Channel: PicoChannels.Accel.
/// </summary>
public class RealAccelerometer : RealSensorBase
{
    [Header("Accelerometer (real) — MPU-6050")]
    [SerializeField] private AccelerometerOutput accelOutput;

    private RollingAverage _ax, _ay, _az;   // per-axis rolling average

    protected override void Decode(SensorPacket packet)
    {
        if (accelOutput == null || packet.Payload.Length < 12) return;
        _ax.Configure(rollingAverageWindow);
        _ay.Configure(rollingAverageWindow);
        _az.Configure(rollingAverageWindow);

        Vector3 g = PicoChannelCodec.DecodeAccelG(packet);
        accelOutput.Publish(new Vector3(_ax.Add(g.x), _ay.Add(g.y), _az.Add(g.z)));
    }
}
