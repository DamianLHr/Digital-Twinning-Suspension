using UnityEngine;

/// <summary>
/// Per-sample distance from a time-of-flight rangefinder (metres).
/// Like a real VL53L0X, this output carries one scalar per reading;
/// downstream consumers (e.g. the predictive solver) buffer successive
/// readings into a bump profile, pairing each with belt position / time.
/// </summary>
public class ToFSensorOutput : SensorOutputBase
{
    [SerializeField] private float latestDistance;
    [SerializeField] private bool latestHit;        // false => no target / out of range

    [Tooltip("Fired whenever a new range reading is published. -1 means no target.")]
    public FloatEvent OnDistance = new FloatEvent();

    public float LatestDistance => latestDistance;
    public bool LatestHit => latestHit;

    /// <summary>Publish a valid range reading.</summary>
    public void Publish(float distance)
    {
        latestDistance = distance;
        latestHit = true;
        Stamp();
        OnDistance.Invoke(distance);
    }

    /// <summary>Publish a no-target reading (out of range / nothing in the beam).</summary>
    public void PublishNoTarget()
    {
        //latestDistance = -1f;
        latestHit = false;
        Stamp();
        OnDistance.Invoke(-1f);
    }
}
