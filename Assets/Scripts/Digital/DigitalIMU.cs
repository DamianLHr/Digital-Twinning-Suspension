using UnityEngine;
using Suspension.Model;

namespace Suspension.Sensors
{
    /// <summary>
    /// Simulated 6-axis IMU. Reads sprung-mass acceleration and angular velocity,
    /// applies bias + noise, and publishes to the accelerometer and gyro outputs.
    /// </summary>
    public class DigitalIMU : DigitalSensorBase
    {
        [Header("IMU (digital)")]
        [SerializeField] private SprungMass target;          // your model class
        [SerializeField] private Vector3 accelBias;
        [SerializeField] private Vector3 gyroBias;
        [SerializeField] private AccelerometerOutput accelOutput;
        [SerializeField] private GyroOutput gyroOutput;

        public override void Initialize()
        {
            base.Initialize();
            if (output == null) output = accelOutput;
        }

        protected override void Sample()
        {
            if (target == null) return;

            if (accelOutput != null)
            {
                Vector3 a = noiseModel.Apply(target.GetAcceleration() + accelBias);
                accelOutput.Publish(a);
            }

            if (gyroOutput != null)
            {
                // Expects SprungMass to expose angular velocity (rad/s). If your
                // model lacks GetAngularVelocity(), it's a one-liner returning
                // the Rigidbody.angularVelocity.
                Vector3 w = noiseModel.Apply(target.GetAngularVelocity() + gyroBias);
                gyroOutput.Publish(w);
            }
        }
    }
}
