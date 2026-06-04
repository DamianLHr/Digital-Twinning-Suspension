using UnityEngine;

/// <summary>
/// Single source of truth for the rig's mechanical parameters. Owns the
/// values; pushes them out to SprungMass, UnsprungMass, the spring/damper
/// ConfigurableJoint, and BumpPipeline (so the solver and the rig stay
/// in lockstep — the most common silent-drift bug otherwise).
///
/// Also reports derived quantities (static deflection, natural frequency,
/// critical damping) and warns if the search range doesn't bracket c_crit.
///
/// Call Apply() to push values. Runs automatically in Awake and on any
/// Inspector change in the editor. Available as a context-menu item too.
/// </summary>
[DisallowMultipleComponent]
[ExecuteAlways]
public class QuarterCarConfig : MonoBehaviour
{
    [Header("Masses (kg)")]
    [Tooltip("Sprung mass — the chassis/payload Rigidbody on top.")]
    [Min(0.001f)] public float sprungMassKg = 5.0f;
    [Tooltip("Unsprung mass — the wheel/axle Rigidbody at the bottom.")]
    [Min(0.001f)] public float unsprungMassKg = 0.5f;

    [Header("Spring / damper (N/m, N·s/m)")]
    [Tooltip("Spring constant of the suspension spring.")]
    [Min(1f)]     public float springStiffness = 20000f;
    [Tooltip("Initial damping coefficient. Overwritten at runtime by the actuator " +
             "as soon as the first bump is solved.")]
    [Min(0f)]     public float initialDamping  = 600f;
    [Tooltip("Tyre stiffness (between belt and unsprung mass). Used if you have a " +
             "second joint modeling the tyre; ignored otherwise.")]
    [Min(0f)]     public float tyreStiffness   = 200000f;

    [Header("Damping search range (N·s/m)")]
    [Tooltip("Lower bound for the parallel damping sweep in BumpPipeline.")]
    [Min(0f)] public float cMin = 50f;
    [Tooltip("Upper bound for the parallel damping sweep in BumpPipeline.")]
    [Min(0f)] public float cMax = 3000f;

    [Header("Targets (assign in Inspector; auto-found if left empty)")]
    public SprungMass       sprung;
    public UnsprungMass     unsprung;       
    public ConfigurableJoint springJoint;
    public BumpPipeline     pipeline;

    [Header("Derived (read-only)")]
    [SerializeField] private float staticDeflectionMm;
    [SerializeField] private float naturalFreqHz;
    [SerializeField] private float criticalDamping;
    [SerializeField] private float dampingRatioInitial;

    // ----------------------------------------------------------------

    private void Awake()          => Apply();
    private void OnValidate()     => Apply();   // applies whenever a value changes in the Inspector

    [ContextMenu("Apply configuration")]
    public void Apply()
    {
        AutoFindTargets();
        ComputeDerived();

        // 1) Masses
        if (sprung != null)
        {
            SetPrivateField(sprung, "mass", sprungMassKg);
            var rb = sprung.GetComponent<Rigidbody>();
            if (rb != null) rb.mass = sprungMassKg;
        }
        if (unsprung != null)
        {
            SetPrivateField(unsprung, "mass", unsprungMassKg);
            var rb = unsprung.GetComponent<Rigidbody>();
            if (rb != null) rb.mass = unsprungMassKg;
        }

        // 2) Spring / damper
        if (springJoint != null)
        {
            var d = springJoint.yDrive;
            d.positionSpring = springStiffness;
            d.positionDamper = initialDamping;
            if (d.maximumForce <= 0f || float.IsInfinity(d.maximumForce))
                d.maximumForce = 1e6f;
            springJoint.yDrive = d;
        }

        // 3) Solver parameters must match the physical rig
        if (pipeline != null)
        {
            SetPrivateField(pipeline, "mass",      sprungMassKg);
            SetPrivateField(pipeline, "stiffness", springStiffness);
            SetPrivateField(pipeline, "cMin",      cMin);
            SetPrivateField(pipeline, "cMax",      cMax);
        }

        // 4) Sanity warnings
        if (cMin >= cMax)
            Debug.LogWarning("[QuarterCarConfig] cMin >= cMax — search range is empty.");
        if (criticalDamping < cMin || criticalDamping > cMax)
            Debug.LogWarning(
                $"[QuarterCarConfig] critical damping ({criticalDamping:F1}) is outside " +
                $"cMin..cMax ({cMin:F1}..{cMax:F1}); the optimum may sit at a boundary.");
        if (sprungMassKg < unsprungMassKg)
            Debug.LogWarning(
                "[QuarterCarConfig] sprung mass < unsprung mass; unusual but not invalid.");
    }

    private void ComputeDerived()
    {
        staticDeflectionMm   = (sprungMassKg * 9.81f / springStiffness) * 1000f;
        naturalFreqHz        = (1f / (2f * Mathf.PI)) * Mathf.Sqrt(springStiffness / sprungMassKg);
        criticalDamping      = 2f * Mathf.Sqrt(springStiffness * sprungMassKg);
        dampingRatioInitial  = initialDamping / Mathf.Max(0.001f, criticalDamping);
    }

    private void AutoFindTargets()
    {
        if (sprung   == null) sprung   = GetComponentInChildren<SprungMass>();
        if (unsprung == null) unsprung = GetComponentInChildren<UnsprungMass>();
        if (pipeline == null) pipeline = GetComponentInChildren<BumpPipeline>();
        if (springJoint == null && sprung != null)
            springJoint = sprung.GetComponent<ConfigurableJoint>();
    }

    // The component fields we're writing to are [SerializeField] private, which
    // is intentional — we want them visible in the Inspector but not part of the
    // public API. Reflection is the cleanest way to drive them from one place
    // without making every model class expose a setter just for this script.
    private static void SetPrivateField(object target, string name, float value)
    {
        var f = target.GetType().GetField(name,
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance);
        if (f != null) f.SetValue(target, value);
    }
}
