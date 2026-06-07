using UnityEngine;

/// <summary>
/// Simulated accelerometer, generic over any Rigidbody (the sprung mass OR the
/// unsprung mass — both have Rigidbodies). Computes PROPER acceleration directly
/// from the assigned Rigidbody's velocity (specific force = kinematic
/// acceleration − gravity, so it reads ~+9.81 m/s² up at rest), so the digital
/// signal is interchangeable with the real MPU-6050 and matches the scheduler's
/// gravity baseline. Mount one per mass and point each at its own Rigidbody.
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
        // Differentiate velocity every physics tick for an accurate reading, then
        // subtract gravity so at rest it reads +9.81 up (proper acceleration).
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
        accelOutput.Publish(_properAccel);
    }
}
