using UnityEngine;

namespace Suspension.Sensors
{
    /// <summary>Suspension travel / linear position from a potentiometer (metres).</summary>
    public class PositionOutput : SensorOutputBase
    {
        [SerializeField] private float  latestPosition;
        [Tooltip("Which corner this output represents, e.g. \"L\" or \"R\".")]
        [SerializeField] private string side = "L";

        [Tooltip("Fired on every new position reading.")]
        public FloatEvent OnPosition = new FloatEvent();

        public string Side => side;

        public void Publish(float x)
        {
            latestPosition = x;
            Stamp();
            OnPosition.Invoke(x);
        }

        public float GetLatest() => latestPosition;
    }
}
