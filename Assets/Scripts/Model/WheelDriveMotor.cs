using UnityEngine;

/// <summary>
/// Drives the terrain belt. NOT a rigid body — it is a controller/actuator:
/// it ramps the belt toward a commanded speed and accumulates encoder ticks.
/// The wheel-speed sensor reads GetSpeed(); the encoder feeds the real path.
/// </summary>
public class WheelDriveMotor : MonoBehaviour
{
    [SerializeField] private float targetSpeed;            // commanded (m/s)
    [SerializeField] private float currentSpeed;          // actual (m/s)
    [SerializeField] private float accelLimit = 5.0f;     // ramp (m/s^2)
    [SerializeField] private int   encoderTicks;
    [SerializeField] private int   ticksPerMeter = 5000;
    [SerializeField] private TerrainWheel wheel;          // belt this motor drives

    private float _tickRemainder;

    private void FixedUpdate()
    {
        currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed,
                                         accelLimit * Time.fixedDeltaTime);
        if (wheel != null) wheel.SetLinearSpeed(currentSpeed);

        // Accumulate fractional ticks so slow speeds still register.
        float ticks = currentSpeed * Time.fixedDeltaTime * ticksPerMeter + _tickRemainder;
        int whole = Mathf.FloorToInt(ticks);
        _tickRemainder = ticks - whole;
        encoderTicks += whole;
    }

    public void SetTargetSpeed(float v) => targetSpeed = v;
    public float GetSpeed()  => currentSpeed;
    public int   GetEncoder() => encoderTicks;
}
