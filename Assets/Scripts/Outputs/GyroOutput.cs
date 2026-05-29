using UnityEngine;

namespace Suspension.Sensors
{
    /// <summary>Angular velocity from the IMU (rad/s).</summary>
    public class GyroOutput : SensorOutputBase
    {
        [SerializeField] private Vector3 latestAngularVelocity;

        [Tooltip("Fired on every new angular-velocity reading.")]
        public Vector3Event OnAngularVelocity = new Vector3Event();

        public void Publish(Vector3 w)
        {
            latestAngularVelocity = w;
            Stamp();
            OnAngularVelocity.Invoke(w);
        }

        public Vector3 GetLatest() => latestAngularVelocity;
    }
}
