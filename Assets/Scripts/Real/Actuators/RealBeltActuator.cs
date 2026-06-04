using UnityEngine;

/// <summary>
/// Real belt drive. Consumes a SpeedCommand and transmits it to the
/// microcontroller as a single little-endian float (m/s). Live only in Twinning.
/// </summary>
public class RealBeltActuator : RealActuatorBase
{
    [Header("Belt (real)")]
    [SerializeField] private SpeedCommand speedCommand;

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

    private void OnSpeed(float v) => Send(v);
}
