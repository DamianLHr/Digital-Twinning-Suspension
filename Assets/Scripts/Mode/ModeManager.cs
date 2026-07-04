using UnityEngine;

/// <summary>
/// Single authority for the rig's Simulating/Twinning mode. It does NOT let
/// devices decide for themselves: it scans the scene for IDigitalDevice and
/// IRealDevice components and enables exactly one family for the active mode,
/// then notifies every IModeReceiver.
///
///   Simulating: IDigitalDevice enabled,  IRealDevice disabled.
///   Twinning:   IRealDevice    enabled,  IDigitalDevice disabled.
///
/// Apply runs in Start (after every device's OnEnable, so the subscribe/
/// unsubscribe in the Real* and Digital* bases settles deterministically) and
/// can be re-run at runtime via SetMode(), or from the context menu.
/// </summary>
[DisallowMultipleComponent]
public class ModeManager : MonoBehaviour
{
    [Header("Mode")]
    [SerializeField] private TwinMode mode = TwinMode.Simulating;

    [Header("Diagnostics (read-only)")]
    [SerializeField] private int digitalDevices;
    [SerializeField] private int realDevices;
    [SerializeField] private int modeReceivers;

    public TwinMode Mode => mode;

    private void Start() => Apply();

    /// <summary>Switch mode at runtime and re-enforce immediately.</summary>
    public void SetMode(TwinMode newMode)
    {
        mode = newMode;
        Apply();
    }

    [ContextMenu("Apply mode")]
    public void Apply()
    {
        bool simulating = mode == TwinMode.Simulating;
        digitalDevices = realDevices = modeReceivers = 0;

        var all = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include);
        for (int i = 0; i < all.Length; i++)
        {
            var mb = all[i];
            if (mb == null || mb is ModeManager) continue;

            //Debug.Log(mb.name);

            if (mb is IDigitalDevice) { mb.enabled = simulating;  digitalDevices++; }
            else if (mb is IRealDevice) { mb.enabled = !simulating; realDevices++; }

            // Separate check: a component may be a mode receiver as well.
            if (mb is IModeReceiver receiver) { receiver.OnModeChanged(mode); modeReceivers++; }
        }
    }
}
