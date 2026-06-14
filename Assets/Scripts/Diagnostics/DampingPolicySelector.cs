using UnityEngine;

/// <summary>
/// Chooses which damping policy drives the <see cref="DampingCommand"/> channel:
/// the predictive <see cref="DampingCommandScheduler"/> or a fixed
/// <see cref="ConstantDampingPolicy"/>. Exactly one is enabled at a time (the other
/// is disabled), so this is the only hook the DIAG suite adds to the control path.
/// </summary>
[DisallowMultipleComponent]
public class DampingPolicySelector : MonoBehaviour
{
    public enum Policy { Predictive, Constant }

    [SerializeField] private Policy policy = Policy.Predictive;
    [SerializeField] private DampingCommandScheduler predictive;
    [SerializeField] private ConstantDampingPolicy constant;

    public Policy Active => policy;

    private void OnEnable() => Apply();

    public void SetPolicy(Policy p) { policy = p; Apply(); }

    [ContextMenu("Apply policy")]
    public void Apply()
    {
        bool pred = policy == Policy.Predictive;
        // Disable the inactive producer first so the channel never has two writers.
        if (pred) { if (constant != null) constant.enabled = false; if (predictive != null) predictive.enabled = true; }
        else      { if (predictive != null) predictive.enabled = false; if (constant != null) constant.enabled = true; }
    }
}
