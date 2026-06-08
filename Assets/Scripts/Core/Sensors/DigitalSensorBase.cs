using UnityEngine;

/// <summary>
/// Base for sensors that synthesise their signal from the Unity simulation.
/// Drives <see cref="Sample"/> at the configured rate from FixedUpdate.
/// </summary>
public abstract class DigitalSensorBase : SensorBase, IDigitalDevice
{
    [Header("Digital")]
    [Tooltip("Digital sampling rate in Hz. The project physics tick runs at 200 Hz, " +
             "so that is the usable maximum; the effective rate is capped by the physics step.")]
    [Range(0,200)]
    [SerializeField] protected float samplingRate = 100f;
    private float updatePeriod = 0f;

    private float _accumulator;

    public override void Initialize()
    {
        if (updatePeriod <= 0f && samplingRate > 0f)
            updatePeriod = 1f / samplingRate;
    }

    protected virtual void FixedUpdate()
    {
        _accumulator += Time.fixedDeltaTime;
        if (_accumulator < updatePeriod) return;
        _accumulator = 0f;
        Sample();
    }

    /// <summary>Read the simulation and publish to the output.</summary>
    protected abstract void Sample();
}