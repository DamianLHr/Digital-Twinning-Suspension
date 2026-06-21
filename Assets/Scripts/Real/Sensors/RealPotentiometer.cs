using UnityEngine;

/// <summary>
/// Slide potentiometer over the Pico link. Payload is a raw int32 ADC count
/// (0..adcMax); normalised and scaled to physical stroke. Instantiate twice
/// (L and R) on PicoChannels.Pot1 / PicoChannels.Pot2.
/// </summary>
public class RealPotentiometer : RealSensorBase
{
    [Header("Potentiometer (real) — slide")]
    [Tooltip("Full ADC scale (e.g. 4096 for 12-bit).")]
    [SerializeField] private float adcMax = 4096f;
    [Tooltip("Physical travel at full scale (m). 6 cm slide = 0.06.")]
    [SerializeField] private float strokeLength = 0.06f;
    [SerializeField] private PositionOutput positionOutput;

    private RollingAverage _avg;

    protected override void Decode(SensorPacket packet)
    {
        if (positionOutput == null || packet.Payload.Length < 4) return;
        _avg.Configure(rollingAverageWindow);

        int raw = PicoChannelCodec.DecodePotRaw(packet);
        float normalized = Mathf.Clamp01(raw / Mathf.Max(1f, adcMax));
        positionOutput.Publish(_avg.Add(normalized * strokeLength));
    }
}
