using UnityEngine;

namespace Suspension.Sensors
{
    /// <summary>
    /// Receives a damping coefficient and translates it into hardware action
    /// (stepper step delta via a calibration curve). Stubbed here; replace
    /// with the real implementation that drives StepperMotorController.
    /// </summary>
    public class DampingActuator : MonoBehaviour
    {
        [SerializeField] private float currentC;
        public float CurrentC => currentC;

        public virtual void SetDamping(float c)
        {
            currentC = c;
            // TODO: c -> calibration curve -> step count -> StepperMotorController.MoveTo(...)
        }
    }
}
