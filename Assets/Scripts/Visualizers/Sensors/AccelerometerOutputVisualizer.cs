using UnityEngine;

/// <summary>Scrolling plot of an AccelerometerOutput's acceleration magnitude.</summary>
public class AccelerometerOutputVisualizer : SensorOutputVisualizerBase
{
    [Header("Source")]
    [SerializeField] private AccelerometerOutput output;

    private void Reset()
    {
        title = "Accel |a|";
        units = "m/s\u00b2";
        traceColor = new Color(1f, 0.55f, 0.40f, 1f);
    }

    protected override void Subscribe()
    {
        if (output == null) output = GetComponent<AccelerometerOutput>();
        if (output != null) output.OnAcceleration.AddListener(OnAcceleration);
    }

    protected override void Unsubscribe()
    {
        if (output != null) output.OnAcceleration.RemoveListener(OnAcceleration);
    }

    private void OnAcceleration(Vector3 a)
    {
        Push(a.magnitude);
    }
}
