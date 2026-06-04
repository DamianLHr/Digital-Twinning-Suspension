using UnityEngine;

/// <summary>
/// Root of the actuator family — the mirror of <see cref="SensorBase"/>. An
/// actuator consumes commands from a shared <see cref="ActuatorCommandBase"/>
/// channel and either drives the Unity model (Digital*) or sends packets to
/// hardware (Real*). A Digital* and a Real* actuator may reference the SAME
/// command channel; ModeManager enables exactly one.
/// </summary>
public abstract class ActuatorBase : MonoBehaviour
{
    [Header("Actuator")]
    [SerializeField] protected string actuatorId = "actuator";
    [Tooltip("Shared command channel this actuator consumes. A Digital* and a " +
             "Real* actuator may reference the same channel; only one is enabled.")]
    [SerializeField] protected ActuatorCommandBase command;

    public string ActuatorId => actuatorId;
    public ActuatorCommandBase Command => command;

    protected virtual void Awake() => Initialize();

    /// <summary>Resolve references and prepare. Called from Awake.</summary>
    public abstract void Initialize();
}
