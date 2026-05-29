using UnityEngine;

namespace Suspension.Sensors
{
    /// <summary>Surface speed of the drive wheel / road belt (m/s).</summary>
    public class WheelSpeedOutput : SensorOutputBase
    {
        [SerializeField] private float latestSpeed;

        [Tooltip("Fired on every new wheel-speed reading.")]
        public FloatEvent OnWheelSpeed = new FloatEvent();

        public void Publish(float v)
        {
            latestSpeed = v;
            Stamp();
            OnWheelSpeed.Invoke(v);
        }

        public float GetLatest() => latestSpeed;
    }
}
