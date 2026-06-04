using UnityEngine;

/// <summary>
/// Simulated belt drive. Consumes a SpeedCommand and applies it to the
/// WheelDriveMotor model, which keeps the ramp dynamics and encoder ticks (the
/// motor stays the model, exactly like a digital sensor reads a model rather
/// than being it). Live only in Simulating mode.
/// </summary>
public class DigitalBeltActuator : DigitalActuatorBase
{
    [Header("Belt (digital)")]
    [SerializeField] private SpeedCommand speedCommand;
    [SerializeField] private WheelDriveMotor motor;

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

    private void OnSpeed(float v)
    {
        if (motor != null) motor.SetTargetSpeed(v);
    }
}
