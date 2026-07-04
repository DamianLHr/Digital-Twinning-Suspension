using UnityEngine;

/// <summary>
/// The wheel/axle (unsprung mass) of the quarter-car rig. The tyre is modelled
/// as a ConfigurableJoint spring between the wheel and a small NON-SPINNING
/// "contact follower" Rigidbody that this component creates and parks at the
/// road surface under the contact patch each step (found by raycast).
///
/// Why not joint straight to the TerrainWheel: the belt is a kinematic drum that
/// rotates, and a joint anchored to it would sweep the wheel around. Its centre
/// hub is no good either � the bumps are on the rim, so the centre never moves.
/// The follower tracks the road HEIGHT under the wheel without spinning, so the
/// joint becomes a clean vertical tyre spring.
///
/// PhysX solves the joint drive implicitly, so it stays stable with a stiff tyre
/// (unlike a hand-applied force), and the two-sided drive keeps the wheel planted
/// on the road. Disable physical collision between this wheel and the belt � the
/// raycast + joint are the contact.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class UnsprungMass : MonoBehaviour, IModeReceiver
{
    [Tooltip("Unsprung mass (kg). Set centrally by QuarterCarConfig.")]
    public float mass = 0.5f;
    [SerializeField] private Rigidbody rb;

    [Header("Contact raycast")]
    [Tooltip("Uncompressed hub height above the road (the tyre's free radius).")]
    [SerializeField] private float wheelRadius = 0.025f;
    [SerializeField] private LayerMask beltMask = ~0;
    [SerializeField] private float rayLength = 0.5f;
    [SerializeField] private Collider selfCollider;     // excluded from the raycast
    [SerializeField] private bool drawDebug = true;

    [Header("Tyre spring (ConfigurableJoint drive)")]
    [Tooltip("Tyre vertical stiffness (N/m). The joint solver handles stiff values.")]
    [SerializeField] private float tyreStiffness = 120000f;
    [Tooltip("Tyre damping (N-s/m). Near/above critical = sticks without bouncing.")]
    [SerializeField] private float tyreDamping = 600f;
    [Tooltip("Drive force ceiling. 0 = effectively unlimited.")]
    [SerializeField] private float maxTyreForce = 0f;

    private Rigidbody _contact;        // non-spinning kinematic anchor at the road
    private ConfigurableJoint _joint;
    private float _lastRoadY;
    private bool _hasRoad;

    private void Awake()
    {
        if (rb == null) rb = GetComponent<Rigidbody>();
        if (selfCollider == null) selfCollider = GetComponent<Collider>();

        rb.mass = mass;
        rb.isKinematic = false;
        rb.useGravity = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.constraints = RigidbodyConstraints.FreezePositionX
                       | RigidbodyConstraints.FreezePositionZ
                       | RigidbodyConstraints.FreezeRotation;

        CreateContactAnchor();
        CreateTyreJoint();
    }

    private void CreateContactAnchor()
    {
        var go = new GameObject(name + " (tyre contact)");
        // Deliberately unparented: it must not inherit the drum's spin. It only
        // tracks the road height under the wheel.
        _contact = go.AddComponent<Rigidbody>();
        _contact.isKinematic = true;
        _contact.useGravity = false;
        _contact.interpolation = RigidbodyInterpolation.None;
        go.transform.position = transform.position - Vector3.up * wheelRadius;
    }

    private void CreateTyreJoint()
    {
        _joint = gameObject.AddComponent<ConfigurableJoint>();
        _joint.connectedBody = _contact;
        _joint.autoConfigureConnectedAnchor = false;
        _joint.anchor = Vector3.zero;
        _joint.connectedAnchor = new Vector3(0f, wheelRadius, 0f);   // wheel rests a tyre-radius above the contact

        // Wheel rotation is frozen, so the joint's local frame == world; the
        // Y drive is therefore a vertical spring.
        _joint.axis = Vector3.right;
        _joint.secondaryAxis = Vector3.up;

        _joint.xMotion = ConfigurableJointMotion.Locked;
        _joint.zMotion = ConfigurableJointMotion.Locked;
        _joint.yMotion = ConfigurableJointMotion.Free;
        _joint.angularXMotion = ConfigurableJointMotion.Locked;
        _joint.angularYMotion = ConfigurableJointMotion.Locked;
        _joint.angularZMotion = ConfigurableJointMotion.Locked;

        _joint.targetPosition = Vector3.zero;   // rest: wheel sits at the anchor
        ApplyDrive();
    }

    private void ApplyDrive()
    {
        _joint.yDrive = new JointDrive
        {
            positionSpring = tyreStiffness,
            positionDamper = tyreDamping,
            maximumForce = maxTyreForce > 0f ? maxTyreForce : float.MaxValue
        };
    }

    /// <summary>
    /// Set the tyre spring stiffness/damping from a single source of truth
    /// (QuarterCarConfig). Stores the values and, if the joint already exists,
    /// applies them live. If called before Awake, the stored values are used
    /// when the joint is created.
    /// </summary>
    public void SetTyreDrive(float stiffness, float damping)
    {
        tyreStiffness = stiffness;
        tyreDamping   = damping;
        if (_joint != null) ApplyDrive();
    }

    private void FixedUpdate()
    {
        Vector3 origin = transform.position + Vector3.up * (rayLength * 0.5f);
        var hits = Physics.RaycastAll(origin, Vector3.down, rayLength, beltMask,
                                      QueryTriggerInteraction.Ignore);

        float nearestDist = float.PositiveInfinity;
        RaycastHit best = default;
        bool found = false;
        for (int i = 0; i < hits.Length; i++)
        {
            if (selfCollider != null && hits[i].collider == selfCollider) continue;
            if (hits[i].distance < nearestDist) { nearestDist = hits[i].distance; best = hits[i]; found = true; }
        }

        if (drawDebug)
        {
            if (found) Debug.DrawLine(origin, best.point, Color.green, 0f, false);
            else Debug.DrawRay(origin, Vector3.down * rayLength, Color.red, 0f, false);
        }

        if (found) { _lastRoadY = best.point.y; _hasRoad = true; }
        else if (!_hasRoad) return;     // never seen the road yet

        Vector3 p = _contact.position;
        p.x = transform.position.x;
        p.z = transform.position.z;
        p.y = _lastRoadY;
        _contact.MovePosition(p);
    }

    private void OnValidate()
    {
        if (Application.isPlaying && _joint != null) ApplyDrive();   // live-tune in play mode
    }

    private void OnDestroy()
    {
        if (_contact != null) Destroy(_contact.gameObject);
    }

    public Vector3 GetPosition() => rb.position;

    // Twinning: the real rig is the source of truth and the model mirrors it from
    // sensor data, so make the body kinematic to stop physics fighting that.
    public void OnModeChanged(TwinMode mode)
    {
        if (rb == null) rb = GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = (mode == TwinMode.Twinning);
    }
}