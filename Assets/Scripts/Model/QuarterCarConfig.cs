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

    [Header("Spring / damper (k = N/m, c = N·s/m)")]
    [Tooltip("Spring constant k (N/m) → yDrive.positionSpring. Distinct from the damping " +
             "coefficient c below. Measured ≈ 27 for this suspension.")]
    [Min(1f)]     public float springConstant = 27f;
    [Tooltip("Damping coefficient c (N·s/m) → yDrive.positionDamper. Initial value; overwritten " +
             "at runtime by the damping actuator once the first bump is solved.")]
    [Min(0f)]     public float initialDamping  = 600f;
    [Tooltip("Tyre vertical stiffness (between belt and unsprung mass). Pushed to the " +
             "UnsprungMass tyre joint so this is the single source of truth.")]
    [Min(0f)]     public float tyreStiffness   = 120000f;
    [Tooltip("Tyre vertical damping (N·s/m). Pushed to the UnsprungMass tyre joint.")]
    [Min(0f)]     public float tyreDamping     = 600f;

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
            sprung.mass = sprungMassKg;
            var rb = sprung.GetComponent<Rigidbody>();
            if (rb != null) rb.mass = sprungMassKg;
        }
        if (unsprung != null)
        {
            unsprung.mass = unsprungMassKg;
            var rb = unsprung.GetComponent<Rigidbody>();
            if (rb != null) rb.mass = unsprungMassKg;

            unsprung.SetTyreDrive(tyreStiffness, tyreDamping);
        }

        // 2) Spring / damper — k → positionSpring, c → positionDamper, maxForce → maximumForce
        if (springJoint != null)
        {
            var d = springJoint.yDrive;
            d.positionSpring = springConstant;   // k
            d.positionDamper = initialDamping;   // c (then driven by the damping actuator)
            if (d.maximumForce <= 0f || float.IsInfinity(d.maximumForce))
                d.maximumForce = 1e6f;           // max actuator force
            springJoint.yDrive = d;
        }

        // 3) Solver parameters must match the physical rig
        if (pipeline != null)
        {
            pipeline.mass      = sprungMassKg;
            pipeline.stiffness = springConstant;
            pipeline.cMin      = cMin;
            pipeline.cMax      = cMax;
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
        staticDeflectionMm   = (sprungMassKg * 9.81f / springConstant) * 1000f;
        naturalFreqHz        = (1f / (2f * Mathf.PI)) * Mathf.Sqrt(springConstant / sprungMassKg);
        criticalDamping      = 2f * Mathf.Sqrt(springConstant * sprungMassKg);
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

}
