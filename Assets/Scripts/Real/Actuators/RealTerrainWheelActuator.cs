using UnityEngine;

/// <summary>
/// Real terrain wheel drive. Commands the device's belt at a fixed wheel speed in
/// RPM (sent as belt_command). Live only in Twinning mode.
/// Set channelId = PicoChannels.Belt in the Inspector.
/// </summary>
public class RealTerrainWheelActuator : RealActuatorBase
{
    [Header("Terrain wheel (real)")]
    [Tooltip("Wheel speed command sent to the device, in RPM.")]
    [SerializeField] private int rpm = 100;

    protected override void OnEnable()
    {
        base.OnEnable();
        SendInt(rpm);
    }

    protected override void Subscribe() { }

    protected override void Unsubscribe() { }
}
