using UnityEngine;

/// <summary>
/// Single source of the Terrain Wheel's motion. Subscribes to the speed command,
/// ramps the actual speed toward it (accel limit), and drives
/// <see cref="TerrainWheel.SetLinearSpeed"/> every FixedUpdate.
///
/// Runs in BOTH modes (it is NOT an IDigitalDevice/IRealDevice):
///   • Simulating — this IS the drum's drive.
///   • Twinning   — the real terrain wheel is driven by the firmware (via RealTerrainWheelActuator);
///                  this becomes an open-loop ESTIMATE of the drum's speed from the
///                  same command, so the bump-pipeline odometer (TraveledDistance)
///                  stays valid even though the real wheel speed isn't sensed.
///
/// Replaces the old split of WheelDriveMotor + DigitalBeltActuator (sim) and
/// BeltSpeedEstimator (twin), which duplicated this ramp logic.
/// </summary>
[DisallowMultipleComponent]
public class TerrainWheelSpeedDriver : MonoBehaviour
{
    [Header("Wiring")]
    [SerializeField] private TerrainWheelSpeedCommand speedCommand;
    [SerializeField] private TerrainWheel terrainWheel;

    [Header("Drive")]
    [Tooltip("Ramp toward the commanded speed (m/s²) — match the real drive's accel limit.")]
    [SerializeField] private float accelLimit = 5f;

    [Header("Diagnostics (read-only)")]
    [SerializeField] private float currentSpeed;

    private float _target;

    public float CurrentSpeed => currentSpeed;

    private void OnEnable()
    {
        if (speedCommand != null) speedCommand.OnSpeed.AddListener(OnSpeed);
    }

    private void OnDisable()
    {
        if (speedCommand != null) speedCommand.OnSpeed.RemoveListener(OnSpeed);
    }

    private void OnSpeed(float metersPerSecond) => _target = metersPerSecond;

    private void FixedUpdate()
    {
        currentSpeed = Mathf.MoveTowards(currentSpeed, _target, accelLimit * Time.fixedDeltaTime);
        if (terrainWheel != null) terrainWheel.SetLinearSpeed(currentSpeed);
    }
}
