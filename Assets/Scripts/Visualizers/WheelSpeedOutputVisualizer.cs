using UnityEngine;

/// <summary>Scrolling plot of a WheelSpeedOutput's speed readings.</summary>
public class WheelSpeedOutputVisualizer : SensorOutputVisualizerBase
{
    [Header("Source")]
    [SerializeField] private WheelSpeedOutput output;

    private void Reset()
    {
        title = "Wheel speed";
        units = "m/s";
        traceColor = new Color(1f, 0.85f, 0.40f, 1f);
    }

    protected override void Subscribe()
    {
        if (output == null) output = GetComponent<WheelSpeedOutput>();
        if (output != null) output.OnWheelSpeed.AddListener(OnWheelSpeed);
    }

    protected override void Unsubscribe()
    {
        if (output != null) output.OnWheelSpeed.RemoveListener(OnWheelSpeed);
    }

    private void OnWheelSpeed(float v)
    {
        Push(v);
    }
}
