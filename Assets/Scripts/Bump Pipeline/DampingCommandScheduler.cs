using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Schedules damping changes for a closed-loop drum with a ToF sensor mounted a
/// known angle BEHIND the wheel along the drum's rotation. Because the drum is
/// periodic, a bump the sensor observes returns to the wheel after the remaining
/// (360 − angle) degrees — i.e. on the NEXT revolution. The scheduler queues
/// (target_pos, c) commands and applies them when TraveledDistance catches up by
/// that offset.
///
/// The offset is pure drum geometry: (1 − angle/360) × circumference, with
/// circumference = π × diameter. When the diameter is known it is computed
/// deterministically (cannot drift, no settle artifacts). This is self-consistent
/// in the sim even if the diameter isn't the real-world value, because the same
/// diameter drives both the drum's rotation and the recurrence period. If the
/// diameter is unavailable, a hardened jolt-based auto-calibration measures the
/// offset instead, validated against the same geometric expectation (phase modulo
/// one revolution) so a startup artifact or mis-paired jolt can't be accepted.
/// Set manualWheelOffset to override everything.
/// </summary>
[DisallowMultipleComponent]
public class DampingCommandScheduler : MonoBehaviour
{
    public enum CalibState { WaitingForObservation, WaitingForJolt, Calibrated }

    [Header("Wiring")]
    [SerializeField] private BumpPipeline pipeline;
    [SerializeField] private DampingCommand dampingCommand;
    [SerializeField] private TerrainWheel terrain;
    [SerializeField] private AccelerometerOutput accel;

    [Header("Geometry")]
    [Tooltip("Angle the ToF sits BEHIND the wheel along the drum's rotation. The sensor sees a " +
             "bump this many degrees AFTER the wheel hits it, so the bump returns to the wheel " +
             "after the remaining (360 − this) degrees. 90 = a quarter-turn behind.")]
    [Range(0f, 359f)]
    [SerializeField] private float sensorAngleBehindDeg = 90f;
    [Tooltip("When the drum diameter is known, compute the offset directly from geometry " +
             "(deterministic, recommended). Turn off to force jolt-based auto-calibration.")]
    [SerializeField] private bool useGeometricOffset = true;

    [Header("Calibration")]
    [Tooltip("If you already know the sensor-to-wheel belt offset, set it here and all " +
             "calibration is skipped. Leave at 0 to compute/measure it.")]
    [SerializeField] private float manualWheelOffset = 0f;
    [Tooltip("Acceleration magnitude (m/s^2 above gravity baseline) that counts as a jolt.")]
    [SerializeField] private float joltThreshold = 2.0f;
    [Tooltip("Gravity baseline subtracted before threshold check. Proper acceleration " +
             "reads ~9.81 up at rest, so the baseline magnitude is 9.81.")]
    [SerializeField] private float gravityBaseline = 9.81f;

    [Header("Calibration robustness")]
    [Tooltip("Belt surface speed (m/s) below which observations/jolts are ignored.")]
    [SerializeField] private float minCalibrationSpeed = 0.02f;
    [Tooltip("Revolutions the belt must complete after enable before calibrating — lets " +
             "startup settling pass and the drum prime.")]
    [SerializeField] private float primeRevolutions = 1.25f;
    [Tooltip("Jolt-based fallback only: accept the measured offset only if its phase lands " +
             "within this many degrees of the expected (360 − sensor angle).")]
    [Range(1f, 180f)]
    [SerializeField] private float offsetToleranceDeg = 45f;
    [Tooltip("Used only if the drum diameter is unavailable: expected offset (m) for the " +
             "jolt-based fallback and the prime distance.")]
    [SerializeField] private float fallbackExpectedOffset = 0.1f;

    [Header("Diagnostics (read-only)")]
    public CalibState State = CalibState.WaitingForObservation;
    [SerializeField] private float wheelOffset;        // computed / measured / manual
    [SerializeField] private int queueDepth;
    [SerializeField] private float lastAppliedC;
    [SerializeField] private float lastAppliedAtPos;

    public float WheelOffset => wheelOffset;
    public int QueueDepth => queueDepth;

    // ---- telemetry for the accuracy visualizer / confidence monitor ----
    [System.Serializable] public struct ScheduledInfo { public float ObservedPos, TargetPos, C, SpeedAtSolve; }
    [System.Serializable] public struct AppliedInfo   { public float TargetPos, AppliedPos, C, SpeedAtSolve, SpeedAtApply; }
    [System.Serializable] public class ScheduledEvent : UnityEvent<ScheduledInfo> { }
    [System.Serializable] public class AppliedEvent   : UnityEvent<AppliedInfo> { }

    public ScheduledEvent OnCommandScheduled = new ScheduledEvent();
    public AppliedEvent   OnCommandApplied   = new AppliedEvent();
    public FloatEvent     OnJolt             = new FloatEvent();   // belt pos at each detected jolt

    private struct PendingCommand { public float TargetPos; public float C; public float SpeedAtSolve; }
    private readonly List<PendingCommand> _pending = new List<PendingCommand>(16);

    private float _firstObservationPos;
    private bool _firstObservationCaptured;
    private float _enableTravel;        // belt travel at enable, for the prime/settle gate

    private void OnEnable()
    {
        if (pipeline != null) pipeline.OnSolveCompleted.AddListener(OnSolveCompleted);
        if (accel != null) accel.OnAcceleration.AddListener(OnAcceleration);

        _enableTravel = terrain != null ? terrain.TraveledDistance : 0f;

        if (manualWheelOffset > 0f)
        {
            wheelOffset = manualWheelOffset;
            State = CalibState.Calibrated;
        }
    }

    private void OnDisable()
    {
        if (pipeline != null) pipeline.OnSolveCompleted.RemoveListener(OnSolveCompleted);
        if (accel != null) accel.OnAcceleration.RemoveListener(OnAcceleration);
    }

    // --- geometry / guards ----------------------------------------------

    private float Circumference() => terrain != null ? Mathf.PI * terrain.Diameter : 0f;

    /// <summary>Belt-travel from a ToF observation to the bump's NEXT wheel contact:
    /// (1 − angle/360) of a revolution. Falls back to a fixed value if no diameter.</summary>
    private float ExpectedOffset()
    {
        float circ = Circumference();
        if (circ <= 0f) return fallbackExpectedOffset;
        return Mathf.Clamp01(1f - sensorAngleBehindDeg / 360f) * circ;
    }

    private bool BeltMoving() => terrain != null && terrain.LinearSpeed >= minCalibrationSpeed;

    private bool PrimeElapsed()
    {
        if (terrain == null) return false;
        float circ  = Circumference();
        float prime = circ > 0f ? primeRevolutions * circ
                                : primeRevolutions * fallbackExpectedOffset;
        return (terrain.TraveledDistance - _enableTravel) >= prime;
    }

    // --- calibration -----------------------------------------------------

    // Deterministic path: once the drum is known and primed, the offset is just
    // geometry — no jolt pairing needed, so nothing transient can poison it.
    private void TryGeometricCalibrate()
    {
        if (State == CalibState.Calibrated) return;
        if (!useGeometricOffset || Circumference() <= 0f) return;   // -> jolt-based fallback
        if (!BeltMoving() || !PrimeElapsed()) return;

        wheelOffset = ExpectedOffset();
        State = CalibState.Calibrated;
        Debug.Log($"[Scheduler] geometric calibration: wheelOffset = {wheelOffset * 1000f:F1} mm " +
                  $"({360f - sensorAngleBehindDeg:F0}° of a {Circumference() * 1000f:F0} mm/rev drum).");
    }

    // --- event handlers --------------------------------------------------

    private void OnSolveCompleted(BumpPipeline.SolveSnapshot snap)
    {
        // Trailing-edge belt position at capture time — no async solve latency.
        float observedAt = snap.ObservedBeltPos;

        if (State == CalibState.WaitingForObservation && !_firstObservationCaptured)
        {
            // Geometric mode calibrates in FixedUpdate; don't start the jolt dance.
            if (useGeometricOffset && Circumference() > 0f) return;
            // Jolt-based fallback: only anchor on a moving, primed belt.
            if (!BeltMoving() || !PrimeElapsed()) return;

            _firstObservationPos = observedAt;
            _firstObservationCaptured = true;
            State = CalibState.WaitingForJolt;
            Debug.Log($"[Scheduler] first observation at belt pos {observedAt:F3} m, " +
                      $"awaiting jolt (expecting ~{ExpectedOffset() * 1000f:F1} mm).");
            return;
        }

        if (State != CalibState.Calibrated) return;

        // The observed bump returns to the wheel after wheelOffset more metres.
        float targetPos = observedAt + wheelOffset;
        float speedAtSolve = terrain != null ? terrain.LinearSpeed : 0f;
        _pending.Add(new PendingCommand { TargetPos = targetPos, C = snap.BestC, SpeedAtSolve = speedAtSolve });
        queueDepth = _pending.Count;

        OnCommandScheduled.Invoke(new ScheduledInfo
        {
            ObservedPos  = observedAt,
            TargetPos    = targetPos,
            C            = snap.BestC,
            SpeedAtSolve = speedAtSolve
        });
    }

    private void OnAcceleration(Vector3 a)
    {
        if (!BeltMoving()) return;

        float aMag = Mathf.Abs(a.y - gravityBaseline);    // Y is up in SprungMass
        if (aMag < joltThreshold) return;

        float joltAt = terrain != null ? terrain.TraveledDistance : 0f;
        OnJolt.Invoke(joltAt);                            // ground-truth marker, fired in any state

        if (State != CalibState.WaitingForJolt) return;   // calibration only in the jolt-based fallback
        float candidate = joltAt - _firstObservationPos;

        // Validate against drum geometry. On a periodic drum, any whole extra
        // revolution is the same bump phase and still schedules correctly, so we
        // validate the phase modulo one revolution against the expected
        // (360 − angle) degrees. A startup artifact or mis-paired jolt is rejected.
        float circ = Circumference();
        float expectedDeg = 360f - sensorAngleBehindDeg;
        float phaseDeg = 0f;
        bool plausible;
        if (circ > 0f && candidate > 0f)
        {
            float phase    = Mathf.Repeat(candidate, circ);
            float expPhase = Mathf.Repeat(ExpectedOffset(), circ);
            float diff     = Mathf.Abs(phase - expPhase);
            diff           = Mathf.Min(diff, circ - diff);           // wrap at the revolution seam
            phaseDeg       = phase / circ * 360f;
            plausible      = diff <= (offsetToleranceDeg / 360f) * circ;
        }
        else
        {
            float e   = ExpectedOffset();
            plausible = candidate > 0f && candidate >= 0.5f * e && candidate <= 1.5f * e;
        }

        if (!plausible)
        {
            Debug.LogWarning(
                $"[Scheduler] rejected offset {candidate * 1000f:F1} mm (phase {phaseDeg:F0}° " +
                $"vs expected {expectedDeg:F0}° ±{offsetToleranceDeg:F0}°); retrying. " +
                $"If this persists, check the ToF angle and TerrainWheel.diameter.");
            State = CalibState.WaitingForObservation;
            _firstObservationCaptured = false;
            return;
        }

        wheelOffset = candidate;
        State = CalibState.Calibrated;
        Debug.Log($"[Scheduler] jolt calibration: wheelOffset = {wheelOffset * 1000f:F1} mm " +
                  $"(phase {phaseDeg:F0}°, expected ~{expectedDeg:F0}°).");
    }

    // --- queue processing ------------------------------------------------

    private void FixedUpdate()
    {
        if (State != CalibState.Calibrated) TryGeometricCalibrate();
        if (State != CalibState.Calibrated || _pending.Count == 0 || terrain == null) return;

        float now = terrain.TraveledDistance;

        // Apply all matured commands. Last-write-wins if several mature this tick.
        int popped = 0;
        for (int i = 0; i < _pending.Count; i++)
        {
            if (_pending[i].TargetPos > now) break;
            if (dampingCommand != null) dampingCommand.Publish(_pending[i].C);
            lastAppliedC = _pending[i].C;
            lastAppliedAtPos = now;
            OnCommandApplied.Invoke(new AppliedInfo
            {
                TargetPos    = _pending[i].TargetPos,
                AppliedPos   = now,
                C            = _pending[i].C,
                SpeedAtSolve = _pending[i].SpeedAtSolve,
                SpeedAtApply = terrain.LinearSpeed
            });
            popped++;
        }
        if (popped > 0)
        {
            _pending.RemoveRange(0, popped);
            queueDepth = _pending.Count;
        }
    }
}
