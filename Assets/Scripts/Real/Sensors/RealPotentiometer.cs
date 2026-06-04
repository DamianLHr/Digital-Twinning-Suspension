using UnityEngine;

/// <summary>
/// 10k slide potentiometer (real-only — no digital sibling by design).
/// Payload is one normalized ADC float in [0,1]; scaled to physical stroke.
/// Instantiate twice (L and R) on different channels.
/// </summary>
public class RealPotentiometer : RealSensorBase
{
    [Header("Potentiometer (real) — 10k slide")]
    [SerializeField] private float  strokeLength = 0.075f;  // full travel (m)
    [SerializeField] private string mountedOn    = "L";
    [SerializeField] private PositionOutput positionOutput;

    protected override void Decode(SensorPacket packet)
    {
        if (positionOutput == null || packet.Payload.Length < 4) return;
        float normalized = Mathf.Clamp01(packet.ReadFloat(0));
        positionOutput.Publish(normalized * strokeLength);
    }
}
