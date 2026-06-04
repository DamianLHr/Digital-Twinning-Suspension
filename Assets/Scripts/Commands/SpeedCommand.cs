using UnityEngine;

/// <summary>Commanded belt / drive surface speed (m/s).</summary>
public class SpeedCommand : ActuatorCommandBase
{
    [SerializeField] private float latestSpeed;

    [Tooltip("Fired whenever a new speed command is published.")]
    public FloatEvent OnSpeed = new FloatEvent();

    public void Publish(float v)
    {
        latestSpeed = v;
        Stamp();
        OnSpeed.Invoke(v);
    }

    public float GetLatest() => latestSpeed;
}
