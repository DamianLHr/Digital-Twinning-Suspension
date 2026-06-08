using UnityEngine;

/// <summary>Readout of the latest terrain wheel speed command, with an optional spinning indicator.</summary>
public class TerrainWheelActuatorVisualizer : ActuatorVisualizerBase
{
    [Header("Source")]
    [SerializeField] private TerrainWheelSpeedCommand command;

    [Header("Optional indicator (assigned scene object)")]
    [Tooltip("Transform spun to visualize speed. Leave empty for none.")]
    [SerializeField] private Transform spinIndicator;
    [SerializeField] private Vector3 spinAxis = Vector3.right;
    [SerializeField] private float degPerMeterPerSecond = 360f;

    private void Reset()
    {
        title = "Terrain wheel speed cmd";
        units = "m/s";
    }

    protected override void Subscribe()
    {
        if (command == null) command = GetComponent<TerrainWheelSpeedCommand>();
        if (command != null) command.OnSpeed.AddListener(Push);
    }

    protected override void Unsubscribe()
    {
        if (command != null) command.OnSpeed.RemoveListener(Push);
    }

    private void Update()
    {
        if (spinIndicator != null && _hasValue && Mathf.Abs(_value) > 1e-4f)
            spinIndicator.Rotate(spinAxis, _value * degPerMeterPerSecond * Time.deltaTime, Space.Self);
    }
}
