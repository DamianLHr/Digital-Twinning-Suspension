using UnityEngine;

/// <summary>
/// Real damping actuator. Consumes a DampingCommand, maps the coefficient c to a
/// stepper position on the PC (the device's CommandData carries target_steps, not
/// a float c), and transmits it. Live only in Twinning mode.
/// Set channelId = PicoChannels.Damping in the Inspector.
/// </summary>
public class RealDampingActuator : RealActuatorBase
{
    [Header("Damping (real)")]
    [SerializeField] private DampingCommand dampingCommand;

    [Header("c → stepper position (linear)")]
    [SerializeField] private float cMin = 50f;
    [SerializeField] private float cMax = 3000f;
    [SerializeField] private int stepsAtCMin = 0;
    [SerializeField] private int stepsAtCMax = 2000;

    [Header("Diagnostics (read-only)")]
    [SerializeField] private float currentC;
    [SerializeField] private int currentSteps;
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

    private int CToSteps(float c)
    {
        float t = Mathf.Clamp01(Mathf.InverseLerp(cMin, cMax, c));
        return Mathf.RoundToInt(Mathf.Lerp(stepsAtCMin, stepsAtCMax, t));
    }

    private void OnDamping(float c)
    {
        currentC = c;
        currentSteps = CToSteps(c);
        SendInt(currentSteps);
    }
}
