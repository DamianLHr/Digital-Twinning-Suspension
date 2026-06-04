using UnityEngine;

/// <summary>
/// Simulated damping actuator. Consumes a DampingCommand and writes the
/// coefficient into the suspension spring joint's Y drive damper on the model
/// (the same joint QuarterCarConfig drives). Live only in Simulating mode.
/// </summary>
public class DigitalDampingActuator : DigitalActuatorBase
{
    [Header("Damping (digital)")]
    [SerializeField] private DampingCommand dampingCommand;
    [Tooltip("The suspension spring/damper joint whose Y drive damper is set.")]
    [SerializeField] private ConfigurableJoint springJoint;

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
        if (springJoint == null) return;
        var d = springJoint.yDrive;
        d.positionDamper = c;
        springJoint.yDrive = d;
    }
}
