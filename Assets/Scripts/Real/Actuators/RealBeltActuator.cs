using UnityEngine;

/// <summary>
/// Real belt drive. Consumes a SpeedCommand (m/s), maps it to the device's
/// belt_command integer, and transmits it. Live only in Twinning mode.
/// Set channelId = PicoChannels.Belt in the Inspector.
/// </summary>
public class RealBeltActuator : RealActuatorBase
{
    [Header("Belt (real)")]
    [SerializeField] private SpeedCommand speedCommand;
    [Tooltip("belt_command units per m/s. Units are device-specific (TBD); tune to the firmware.")]
    [SerializeField] private float commandPerMeterPerSecond = 1000f;

    [Header("Diagnostics (read-only)")]
    [SerializeField] private int currentCommand;

    public override void Initialize()
    {
        base.Initialize();
        if (command == null) command = speedCommand;
    }

    protected override void Subscribe()
    {

        if (speedCommand == null) speedCommand = command as SpeedCommand;
        if (speedCommand != null) speedCommand.OnSpeed.AddListener(OnSpeed);

    }

    protected override void Unsubscribe()
    {
        if (speedCommand != null) speedCommand.OnSpeed.RemoveListener(OnSpeed);
    }

    private void OnSpeed(float metersPerSecond)
    {
        currentCommand = Mathf.RoundToInt(metersPerSecond * commandPerMeterPerSecond);
        SendInt(currentCommand);
    }
}
