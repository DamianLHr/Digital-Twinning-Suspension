using UnityEngine;

/// <summary>Scrolling plot of an AccelerometerOutput's acceleration magnitude.</summary>
public class AccelerometerOutputVisualizer : SensorOutputVisualizerBase
{
    [Header("Source")]
    [SerializeField] private AccelerometerOutput output;
    [Tooltip("Rest reading subtracted so the plot shows DYNAMIC acceleration around 0 instead of " +
             "the constant ~1 g gravity offset. In g, so this is 1.")]
    [SerializeField] private float gravityBaseline = 1.0f;

    private void Reset()
    {
        title = "Accel (vert)";
        units = "g";
        traceColor = new Color(1f, 0.55f, 0.40f, 1f);
    }

    protected override void Subscribe()
    {
        units = "g";   // this sensor is fixed in g — override any stale serialized unit
        if (output == null) output = GetComponent<AccelerometerOutput>();
        if (output != null) output.OnAcceleration.AddListener(OnAcceleration);
    }

    protected override void Unsubscribe()
    {
        if (output != null) output.OnAcceleration.RemoveListener(OnAcceleration);
    }

    private void OnAcceleration(Vector3 a)
    {
        // Vertical proper acceleration with gravity removed → fluctuates around 0 (in g),
        // not pinned at ~1 g. (Vertical is what the bumps drive.)
        Push(a.y - gravityBaseline);
    }
}
