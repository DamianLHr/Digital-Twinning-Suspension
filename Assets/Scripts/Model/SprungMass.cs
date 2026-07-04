using UnityEngine;

/// <summary>
/// The car body mass in the quarter-car rig. A physics body: this wraps a
/// Rigidbody (composition — Rigidbody is a built-in component and cannot be
/// subclassed) and exposes the quantities the accelerometer / potentiometer read.
///
/// The spring / damper connection to the unsprung mass is the rig's job
/// (ConfigurableJoint on the Spring component); this class only reports state.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class SprungMass : MonoBehaviour, IModeReceiver
{
    [Tooltip("Sprung mass (kg). Set centrally by QuarterCarConfig.")]
    public float mass = 5.0f;
    [SerializeField] private Rigidbody rb;

    private Vector3 _lastVelocity;
    private Vector3 _acceleration;   // proper acceleration (m/s^2)

    private void Reset() => rb = GetComponent<Rigidbody>();

    private void Awake()
    {
        if (rb == null) rb = GetComponent<Rigidbody>();
        rb.mass = mass;
        _lastVelocity = rb.linearVelocity;   // Unity 6: rb.linearVelocity
    }

    private void FixedUpdate()
    {
        Vector3 v = rb.linearVelocity;       // Unity 6: rb.linearVelocity
        Vector3 kinematic = (v - _lastVelocity) / Time.fixedDeltaTime;

        _acceleration = kinematic - Physics.gravity;
        _lastVelocity = v;
    }

    public Vector3 GetPosition()        => rb.position;
    public Vector3 GetVelocity()        => rb.linearVelocity;          // Unity 6: linearVelocity
    public Vector3 GetAcceleration()    => _acceleration;        // proper accel, matches real accelerometer

    // Twinning: the real rig is the source of truth and the model mirrors it from
    // sensor data, so make the body kinematic to stop physics fighting that.
    public void OnModeChanged(TwinMode mode)
    {
        if (rb == null) rb = GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = (mode == TwinMode.Twinning);
    }
}
