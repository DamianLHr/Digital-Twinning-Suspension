using UnityEngine;

/// <summary>Readout of the latest damping command, with an optional travel indicator.</summary>
public class DampingActuatorVisualizer : ActuatorVisualizerBase
{
    [Header("Source")]
    [SerializeField] private DampingCommand command;

    [Header("Optional indicator (assigned scene object)")]
    [Tooltip("Transform slid to visualize the setpoint within [cMin, cMax]. Leave empty for none.")]
    [SerializeField] private Transform slideIndicator;
    [SerializeField] private Vector3 slideAxis = Vector3.up;
    [SerializeField] private Vector3 slideLowLocalPos;
    [SerializeField] private float slideLengthMeters = 0.05f;
    [SerializeField] private float cMin = 50f;
    [SerializeField] private float cMax = 3000f;

    private void Reset()
    {
        title = "Damping cmd";
        units = "N\u00b7s/m";
    }

    protected override void Subscribe()
    {
        if (command == null) command = GetComponent<DampingCommand>();
        if (command != null) command.OnDamping.AddListener(Push);
    }

    protected override void Unsubscribe()
    {
        if (command != null) command.OnDamping.RemoveListener(Push);
    }

    protected override void OnValue(float value)
    {
        if (slideIndicator == null) return;
        float span = Mathf.Abs(cMax - cMin) < 1e-6f ? 1f : (cMax - cMin);
        float t = Mathf.Clamp01((value - cMin) / span);
        Vector3 axis = slideAxis.sqrMagnitude > 1e-6f ? slideAxis.normalized : Vector3.up;
        slideIndicator.localPosition = slideLowLocalPos + axis * (t * slideLengthMeters);
    }
}
