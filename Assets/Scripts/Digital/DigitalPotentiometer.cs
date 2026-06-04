using UnityEngine;

/// <summary>
/// Simulated linear potentiometer measuring suspension travel of the sprung
/// mass relative to its rest height.
/// </summary>
public class DigitalPotentiometer : DigitalSensorBase
{
    [Header("Potentiometer (digital)")]
    [SerializeField] private SprungMass target;          // your model class
    [SerializeField] private string side = "L";
    [SerializeField] private float restHeight = 0f;      // equilibrium Y (m)
    [SerializeField] private PositionOutput positionOutput;

    public override void Initialize()
    {
        base.Initialize();
        if (output == null) output = positionOutput;
    }

    protected override void Sample()
    {
        if (target == null || positionOutput == null) return;
        float travel = target.GetPosition().y - restHeight;
        positionOutput.Publish(travel);
    }
}
