using UnityEngine;

/// <summary>Commanded suspension damping coefficient c (N·s/m).</summary>
public class DampingCommand : ActuatorCommandBase
{
    [SerializeField] private float latestC;

    [Tooltip("Fired whenever a new damping command is published.")]
    public FloatEvent OnDamping = new FloatEvent();

    public void Publish(float c)
    {
        latestC = c;
        Stamp();
        OnDamping.Invoke(c);
    }

    public float GetLatest() => latestC;
}
