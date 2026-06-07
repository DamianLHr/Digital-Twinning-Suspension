using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Schedules damping changes for a closed-loop belt with a downstream ToF
/// sensor: a bump observed at belt position p_obs will reach the wheel
/// after the belt advances by `wheelOffset` metres. The scheduler queues
/// (target_pos, c) commands and applies them when TraveledDistance
/// catches up.
///
/// On startup, `wheelOffset` is unknown and is measured automatically from
/// the first observation/jolt pair. During calibration the scheduler holds
/// all commands (the wheel hits the first few bumps with the default c);
/// once calibrated, it processes the queue normally.
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

    [Header("Calibration")]
    [Tooltip("Acceleration magnitude (m/s^2 above gravity baseline) that counts as a jolt.")]
    [SerializeField] private float joltThreshold = 2.0f;
    [Tooltip("If you already know the sensor-to-wheel belt offset, set it here and " +
             "auto-calibration is skipped. Leave at 0 to auto-calibrate.")]
    [SerializeField] private float manualWheelOffset = 0f;
    [Tooltip("Gravity baseline subtracted before threshold check. Proper acceleration " +
             "reads ~9.81 up at rest, so the baseline magnitude is 9.81.")]
    [SerializeField] private float gravityBaseline = 9.81f;

    [Header("Diagnostics (read-only)")]
    public CalibState State = CalibState.WaitingForObservation;
    [SerializeField] private float wheelOffset;        // measured / manual
    [SerializeField] private int queueDepth;
    [SerializeField] private float lastAppliedC;
    [SerializeField] private float lastAppliedAtPos;

    public float WheelOffset => wheelOffset;
    public int QueueDepth => queueDepth;

    // Pending commands. A simple list used as a FIFO; six bumps means ~6 entries
    // at peak, so List<> with linear remove is fine — no need for a real queue.
    private struct PendingCommand
    {
        public float TargetPos;   // TraveledDistance at which to apply C
        public float C;
    }
    private readonly List<PendingCommand> _pending = new List<PendingCommand>(16);

    // Calibration state
    private float _firstObservationPos;
    private bool _firstObservationCaptured;

    private void OnEnable()
    {
        if (pipeline != null) pipeline.OnSolveCompleted.AddListener(OnSolveCompleted);
        if (accel != null) accel.OnAcceleration.AddListener(OnAcceleration);

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

    // --- event handlers --------------------------------------------------

    private void OnSolveCompleted(BumpPipeline.SolveSnapshot snap)
    {
        // The belt position at which this bump was observed — stamped by the
        // pipeline at the bump's trailing edge (capture time), so it carries NO
        // async solve latency. Using this instead of "TraveledDistance now"
        // removes the latency bias that previously skewed calibration and made
        // the first (Burst-cold) solve poison the wheel-offset measurement.
        float observedAt = snap.ObservedBeltPos;

        if (State == CalibState.WaitingForObservation && !_firstObservationCaptured)
        {
            _firstObservationPos = observedAt;
            _firstObservationCaptured = true;
            State = CalibState.WaitingForJolt;
            Debug.Log($"[Scheduler] first observation at belt pos {observedAt:F3} m, " +
                      $"awaiting jolt to calibrate offset.");
            // We deliberately drop this command — the wheel will hit the bump
            // un-tuned, then we measure the offset from that hit.
            return;
        }

        if (State != CalibState.Calibrated) return;   // still mid-calibration

        // Schedule: the same bump will reach the wheel when TraveledDistance
        // catches up by wheelOffset metres.
        _pending.Add(new PendingCommand
        {
            TargetPos = observedAt + wheelOffset,
            C = snap.BestC
        });
        queueDepth = _pending.Count;
    }

    private void OnAcceleration(Vector3 a)
    {
        if (State != CalibState.WaitingForJolt) return;

        float aMag = Mathf.Abs(a.y - gravityBaseline);   // Y is up in my SprungMass
        if (aMag < joltThreshold) return;

        float joltAt = terrain != null ? terrain.TraveledDistance : 0f;
        wheelOffset = joltAt - _firstObservationPos;
        State = CalibState.Calibrated;
        Debug.Log($"[Scheduler] calibrated: wheelOffset = {wheelOffset * 1000f:F1} mm " +
                  $"(jolt at {joltAt:F3}, observation was at {_firstObservationPos:F3})");
    }

    // --- queue processing ------------------------------------------------

    private void FixedUpdate()
    {
        if (State != CalibState.Calibrated || _pending.Count == 0 || terrain == null) return;

        float now = terrain.TraveledDistance;

        // Apply all matured commands. Last-write-wins: if two commands have
        // matured this tick, the latest one's c is what the actuator ends up at.
        // We sweep from front to back, popping as we go.
        int popped = 0;
        for (int i = 0; i < _pending.Count; i++)
        {
            if (_pending[i].TargetPos > now) break;
            if (dampingCommand != null) dampingCommand.Publish(_pending[i].C);
            lastAppliedC = _pending[i].C;
            lastAppliedAtPos = now;
            popped++;
        }
        if (popped > 0)
        {
            _pending.RemoveRange(0, popped);
            queueDepth = _pending.Count;
        }
    }
}