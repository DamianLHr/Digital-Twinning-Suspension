using UnityEngine;

/// <summary>Suspension travel / linear position from a potentiometer (metres).</summary>
public class PositionOutput : SensorOutputBase
{
    [SerializeField] private float  latestPosition;

    [Tooltip("Fired on every new position reading.")]
    public FloatEvent OnPosition = new FloatEvent();

    public void Publish(float x)
    {
        latestPosition = x;
        Stamp();
        OnPosition.Invoke(x);
    }

    public float GetLatest() => latestPosition;
}
