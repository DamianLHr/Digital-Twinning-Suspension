using UnityEngine;

/// <summary>
/// Simulated accelerometer. Reads sprung-mass acceleration and publishes it
/// to the accelerometer output.
/// </summary>
public class DigitalAccelerometer : DigitalSensorBase
{
    [Header("Accelerometer (digital)")]
    [SerializeField] private SprungMass target;          // your model class
    [SerializeField] private AccelerometerOutput accelOutput;

    public override void Initialize()
    {
        base.Initialize();
        if (output == null) output = accelOutput;
    }

    protected override void Sample()
    {
        if (target == null || accelOutput == null) return;
        accelOutput.Publish(target.GetAcceleration());
    }
}
