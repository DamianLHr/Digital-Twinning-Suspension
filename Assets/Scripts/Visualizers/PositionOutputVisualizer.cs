using UnityEngine;

/// <summary>Scrolling plot of a PositionOutput's suspension-travel readings.</summary>
public class PositionOutputVisualizer : SensorOutputVisualizerBase
{
    [Header("Source")]
    [SerializeField] private PositionOutput output;

    private void Reset()
    {
        title = "Suspension travel";
        units = "m";
        traceColor = new Color(0.55f, 1f, 0.55f, 1f);
    }

    protected override void Subscribe()
    {
        if (output == null) output = GetComponent<PositionOutput>();
        if (output != null) output.OnPosition.AddListener(OnPosition);
    }

    protected override void Unsubscribe()
    {
        if (output != null) output.OnPosition.RemoveListener(OnPosition);
    }

    private void OnPosition(float x)
    {
        Push(x);
    }
}
