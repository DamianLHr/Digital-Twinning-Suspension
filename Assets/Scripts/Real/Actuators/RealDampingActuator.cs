using UnityEngine;

/// <summary>
/// Real damping actuator. Consumes a DampingCommand and transmits the
/// coefficient to the microcontroller, which maps c -> stepper position via its
/// calibration curve. Live only in Twinning mode.
/// </summary>
public class RealDampingActuator : RealActuatorBase
{
    [Header("Damping (real)")]
    [SerializeField] private DampingCommand dampingCommand;

    [Header("Diagnostics (read-only)")]
    [SerializeField] private float currentC;
    public float CurrentC => currentC;

    public override void Initialize()
    {
        base.Initialize();
        if (command == null) command = dampingCommand;
    }

    protected override void Subscribe()
    {
        if (dampingCommand == null) dampingCommand = command as DampingCommand;
        if (dampingCommand != null) dampingCommand.OnDamping.AddListener(OnDamping);
    }

    protected override void Unsubscribe()
    {
        if (dampingCommand != null) dampingCommand.OnDamping.RemoveListener(OnDamping);
    }

    private void OnDamping(float c)
    {
        currentC = c;
        Send(c);
    }
}
