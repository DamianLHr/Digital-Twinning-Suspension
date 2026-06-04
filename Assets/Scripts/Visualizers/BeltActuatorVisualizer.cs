using UnityEngine;

/// <summary>Readout of the latest belt speed command, with an optional spinning indicator.</summary>
public class BeltActuatorVisualizer : ActuatorVisualizerBase
{
    [Header("Source")]
    [SerializeField] private SpeedCommand command;

    [Header("Optional indicator (assigned scene object)")]
    [Tooltip("Transform spun to visualize speed. Leave empty for none.")]
    [SerializeField] private Transform spinIndicator;
    [SerializeField] private Vector3 spinAxis = Vector3.right;
    [SerializeField] private float degPerMeterPerSecond = 360f;

    private void Reset()
    {
        title = "Belt speed cmd";
        units = "m/s";
    }

    protected override void Subscribe()
    {
        if (command == null) command = GetComponent<SpeedCommand>();
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
