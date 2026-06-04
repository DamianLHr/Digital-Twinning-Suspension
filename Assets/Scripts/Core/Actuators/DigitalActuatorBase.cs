using UnityEngine;

/// <summary>
/// Base for actuators that act on the Unity simulation. Live only in Simulating
/// mode (ModeManager toggles `enabled`, which drives the OnEnable/OnDisable
/// subscribe/unsubscribe below). Concrete subclasses hook the command channel's
/// typed event in Subscribe and apply the value to the model.
/// </summary>
public abstract class DigitalActuatorBase : ActuatorBase, IDigitalDevice
{
    public override void Initialize() { /* references resolved in OnEnable */ }

    protected virtual void OnEnable()  => Subscribe();
    protected virtual void OnDisable() => Unsubscribe();

    /// <summary>Add the listener to the command channel's typed event.</summary>
    protected abstract void Subscribe();

    /// <summary>Remove the listener.</summary>
    protected abstract void Unsubscribe();
}
