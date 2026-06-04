using UnityEngine;

/// <summary>
/// Root of the sensor family. A sensor reads a quantity (from Unity physics
/// or from hardware) and pushes it to its <see cref="SensorOutputBase"/>,
/// which is the shared swap point between digital and real implementations.
/// </summary>
public abstract class SensorBase : MonoBehaviour
{
    [Header("Sensor")]
    [SerializeField] protected string sensorId = "sensor";
    [Range(1f, 200f)]
    [Tooltip("Digital sampling rate in Hz. The project physics tick runs at 200 Hz, " +
             "so that is the usable maximum; the effective rate is capped by the physics step.")]
    [SerializeField] protected float samplingRate = 100f;
    [Tooltip("Shared output this sensor publishes to. A Digital* and a Real* " +
             "sensor may reference the same output; only one should be enabled.")]
    [SerializeField] protected SensorOutputBase output;

    public string SensorId => sensorId;
    public SensorOutputBase Output => output;

    protected virtual void Awake() => Initialize();

    /// <summary>Resolve references and prepare for sampling. Called from Awake.</summary>
    public abstract void Initialize();
}
