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

    protected override void Decode(SensorPacket packet)
    {
        if (accelOutput == null || packet.Payload.Length < 12) return;

        // Published in g — the MPU's native unit. No conversion to m/s² (the whole
        // accel pipeline works in g, with a baseline of 1 g at rest).
        accelOutput.Publish(PicoChannelCodec.DecodeAccelG(packet));
    }
}
