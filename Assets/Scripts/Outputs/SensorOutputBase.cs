using UnityEngine;

namespace Suspension.Sensors
{
    /// <summary>
    /// Shared broadcast point. Both the Digital* and Real* implementation of a
    /// sensor publish to the SAME output instance — that is the swap point that
    /// lets you mix real and simulated sensors. Consumers (e.g. the damping
    /// search) subscribe to the typed UnityEvent on the concrete subclass and
    /// never poll.
    /// </summary>
    public abstract class SensorOutputBase : MonoBehaviour
    {
        [SerializeField] protected bool isValid;
        protected float timestamp;

        public float GetTimestamp() => timestamp;
        public bool   IsValid       => isValid;

        /// <summary>Stamp the output as freshly updated. Call from Publish().</summary>
        protected void Stamp()
        {
            timestamp = Time.time;
            isValid   = true;
        }
    }
}
