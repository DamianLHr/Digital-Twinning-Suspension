using UnityEngine;

/// <summary>
/// Base for sensors that synthesise their signal from the Unity simulation.
/// Drives <see cref="Sample"/> at the configured rate from FixedUpdate.
/// </summary>
public abstract class DigitalSensorBase : SensorBase
{
    [Header("Digital")]
    [Tooltip("Seconds between samples. <= 0 derives it from samplingRate.")]
    [SerializeField] protected float updatePeriod = 0f;

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
