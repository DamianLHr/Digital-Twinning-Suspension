using UnityEngine;

/// <summary>
/// Activates/deactivates GameObjects based on the rig's Simulating/Twinning mode.
/// Attach to any object that should exist in only one mode — e.g. the wheel bumps
/// (Simulating only, so the drum is smooth when Twinning the real bumped wheel).
///
/// Driven entirely by ModeManager via IModeReceiver: ModeManager finds this even
/// on an inactive GameObject (it scans inactive objects too) and calls
/// OnModeChanged, so toggling works in both directions across mode switches.
/// The component never reads the global mode itself.
/// </summary>
[DisallowMultipleComponent]
public class ModeScopedObject : MonoBehaviour, IModeReceiver
{
    public enum Scope { SimulatingOnly, TwinningOnly, Both }

    [Tooltip("Which mode(s) the target(s) should be ACTIVE in.")]
    [SerializeField] private Scope activeIn = Scope.SimulatingOnly;

    [Tooltip("Toggle this GameObject itself.")]
    [SerializeField] private bool includeSelf = true;

    [Tooltip("Extra GameObjects toggled together with this one (optional).")]
    [SerializeField] private GameObject[] additionalTargets;

    public void OnModeChanged(TwinMode mode)
    {
        bool active = activeIn == Scope.Both
                   || (activeIn == Scope.SimulatingOnly && mode == TwinMode.Simulating)
                   || (activeIn == Scope.TwinningOnly   && mode == TwinMode.Twinning);

        if (additionalTargets != null)
            for (int i = 0; i < additionalTargets.Length; i++)
                if (additionalTargets[i] != null) additionalTargets[i].SetActive(active);

        if (includeSelf) gameObject.SetActive(active);
    }
}
