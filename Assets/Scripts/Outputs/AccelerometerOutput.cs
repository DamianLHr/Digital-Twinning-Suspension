using UnityEngine;

/// <summary>Proper acceleration of the mass, in g (~1 g up at rest).</summary>
public class AccelerometerOutput : SensorOutputBase
{
    [SerializeField] private Vector3 latestAccel;

    [Tooltip("Fired on every new acceleration reading.")]
    public Vector3Event OnAcceleration = new Vector3Event();

    public void Publish(Vector3 a)
    {
        latestAccel = a;
        Stamp();
        OnAcceleration.Invoke(a);
    }

    public Vector3 GetLatest() => latestAccel;
}
