using UnityEngine;
using Suspension.Model;

namespace Suspension.Sensors
{
    /// <summary>Simulated wheel-speed sensor reading the drive motor.</summary>
    public class DigitalWheelSpeedSensor : DigitalSensorBase
    {
        [Header("Wheel speed (digital)")]
        [SerializeField] private WheelDriveMotor source;     // your model class
        [SerializeField] private WheelSpeedOutput wheelOutput;

        public override void Initialize()
        {
            base.Initialize();
            if (output == null) output = wheelOutput;
        }

        protected override void Sample()
        {
            if (source == null || wheelOutput == null) return;
            wheelOutput.Publish(noiseModel.Apply(source.GetSpeed()));
        }
    }
}
