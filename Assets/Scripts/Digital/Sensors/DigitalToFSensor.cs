using UnityEngine;

/// <summary>
/// Simulated VL53L0X-style time-of-flight rangefinder. One physics raycast
/// per sample from this transform along its -up axis (i.e. mount it above
/// the belt looking down, ahead of the contact patch). Publishes a scalar
/// distance just like the real sensor; successive readings as the belt
/// rotates form the bump profile.
///
/// Mounting: place this GameObject so its transform.position is the emitter
/// origin and transform.up points away from the road. The sensor's forward
/// stand-off from the wheel contact is just the position you give it in the
/// scene; the sensor itself only knows "shoot down".
/// </summary>
public class DigitalToFSensor : DigitalSensorBase
{
    [Header("ToF (digital)")]
    [Tooltip("Max measurable distance (m). VL53L0X ~2 m, L1X ~4 m.")]
    [SerializeField] private float range = 2.0f;
    [Tooltip("Physics layers the beam can hit (the belt / road surface).")]
    [SerializeField] private LayerMask hitMask = ~0;
    [Tooltip("Optional: draw a debug line in the Scene/Game view.")]
    [SerializeField] private bool drawDebug = false;

    [Header("Output")]
    [SerializeField] private ToFSensorOutput tofOutput;

    public override void Initialize()
    {
        base.Initialize();
        if (output == null) output = tofOutput;
    }

    protected override void Sample()
    {
        if (tofOutput == null) return;

        Vector3 origin = transform.position;
        Vector3 dir = -transform.up;       // aimed at the road

        if (Physics.Raycast(origin, dir, out RaycastHit hit, range, hitMask,
                            QueryTriggerInteraction.Ignore))
        {
            // Clamp into [0, range].
            float d = hit.distance;
            if (d < 0f) d = 0f;
            if (d > range) d = range;
            tofOutput.Publish(d);

            if (drawDebug) Debug.DrawLine(origin, hit.point, Color.green, 0f, false);
        }
        else
        {
            tofOutput.PublishNoTarget();
            if (drawDebug) Debug.DrawRay(origin, dir * range, Color.red, 0f, false);
        }
    }
}
