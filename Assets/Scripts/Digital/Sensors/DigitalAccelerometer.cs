using UnityEngine;

/// <summary>
/// Simulated accelerometer, generic over any Rigidbody (the sprung mass OR the
/// unsprung mass — both have Rigidbodies). Computes PROPER acceleration directly
/// from the assigned Rigidbody's velocity (specific force = kinematic
/// acceleration − gravity) and publishes it in g (~+1 g up at rest), so the
/// digital signal is interchangeable with the real MPU-6050. Mount one per mass
/// and point each at its own Rigidbody.
/// </summary>
public class DigitalAccelerometer : DigitalSensorBase
{
    [Header("Accelerometer (digital)")]
    [Tooltip("Rigidbody this accelerometer is mounted on (sprung or unsprung mass). " +
             "Auto-found on this object / parent if left empty.")]
    [SerializeField] private Rigidbody target;
    [SerializeField] private AccelerometerOutput accelOutput;

    private Vector3 _lastVelocity;
    private Vector3 _properAccel;   // specific force (m/s^2), matches a real accelerometer
    private bool _primed;

    public override void Initialize()
    {
        base.Initialize();
        if (output == null) output = accelOutput;
        if (target == null) target = GetComponent<Rigidbody>();
        if (target == null) target = GetComponentInParent<Rigidbody>();
        if (target != null) _lastVelocity = target.linearVelocity;
    }

    protected override void FixedUpdate()
    {
        if (target != null)
        {
            Vector3 v = target.linearVelocity;
            if (_primed)
                _properAccel = (v - _lastVelocity) / Time.fixedDeltaTime - Physics.gravity;
            _lastVelocity = v;
            _primed = true;
        }

        base.FixedUpdate();   // accumulator → Sample() at the configured rate
    }

    protected override void Sample()
    {
        if (target == null || accelOutput == null) return;
        // Publish in g (specific force ÷ gravity) to match the MPU-6050: ~1 g up at rest.
        accelOutput.Publish(_properAccel / Mathf.Max(0.001f, Physics.gravity.magnitude));
    }
}
