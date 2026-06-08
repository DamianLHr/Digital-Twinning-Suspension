using UnityEngine;

/// <summary>
/// Twinning-mode belt-speed source. In Twinning the DigitalBeltActuator (and its
/// WheelDriveMotor) are disabled, so nothing drives the sim drum — yet the bump
/// pipeline still needs TerrainWheel.LinearSpeed / TraveledDistance as its
/// odometer. This open-loop estimator mirrors the commanded belt speed onto the
/// drum with the same ramp dynamics, so the spatial coordinate stays valid even
/// though the real wheel speed isn't sensed (see S-14 / D-3).
///
/// Marked IRealDevice so ModeManager enables it only in Twinning. Replace with a
/// real belt-speed sensor feed later if the device gains one (note: open-loop
/// integration of TraveledDistance can drift over long runs).
/// </summary>
[DisallowMultipleComponent]
public class BeltSpeedEstimator : MonoBehaviour, IRealDevice
{
    [SerializeField] private SpeedCommand speedCommand;
    [SerializeField] private TerrainWheel terrain;
    [Tooltip("Ramp toward the commanded speed (m/s²) — match the real drive's accel limit.")]
    [SerializeField] private float accelLimit = 5f;

    [SerializeField] private float currentSpeed;   // diagnostic
    private float _target;

    private void OnEnable()
    {
        if (speedCommand != null) speedCommand.OnSpeed.AddListener(OnSpeed);
    }

    private void OnDisable()
    {
        if (speedCommand != null) speedCommand.OnSpeed.RemoveListener(OnSpeed);
    }

    private void OnSpeed(float v) => _target = v;

    private void FixedUpdate()
    {
        currentSpeed = Mathf.MoveTowards(currentSpeed, _target, accelLimit * Time.fixedDeltaTime);
        if (terrain != null) terrain.SetLinearSpeed(currentSpeed);
    }
}
