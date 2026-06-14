using UnityEngine;

/// <summary>
/// Publishes a fixed damping coefficient to the shared <see cref="DampingCommand"/>
/// channel — the "no prediction" baseline for DIAG experiment A. Enabled
/// exclusively by <see cref="DampingPolicySelector"/> (the predictive scheduler is
/// disabled while this is active), so it reuses the existing command swap-point and
/// the actuator is untouched.
/// </summary>
[DisallowMultipleComponent]
public class ConstantDampingPolicy : MonoBehaviour
{
    [SerializeField] private DampingCommand dampingCommand;
    [Tooltip("Fixed damping coefficient (N·s/m) held for the whole run.")]
    [SerializeField] private float constantC = 3.0f;

    public float ConstantC
    {
        get => constantC;
        set { constantC = value; Publish(); }
    }

    private void OnEnable() => Publish();

    private void Publish()
    {
        if (isActiveAndEnabled && dampingCommand != null) dampingCommand.Publish(constantC);
    }
}
