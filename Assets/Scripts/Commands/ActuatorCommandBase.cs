using UnityEngine;

/// <summary>
/// Shared command channel — the actuator-side mirror of
/// <see cref="SensorOutputBase"/>. A producer (e.g. the damping scheduler)
/// Publishes a command value; the enabled Digital* OR Real* actuator consumes it
/// via the typed UnityEvent on the concrete subclass. Swapping which actuator is
/// enabled (done by ModeManager) is the simulate/twin swap point on this side.
/// </summary>
public abstract class ActuatorCommandBase : MonoBehaviour
{
    [SerializeField] protected bool isValid;
    protected float timestamp;

    public float GetTimestamp() => timestamp;
    public bool  IsValid       => isValid;

    /// <summary>Stamp the channel as freshly updated. Call from Publish().</summary>
    protected void Stamp()
    {
        timestamp = Time.time;
        isValid   = true;
    }
}
