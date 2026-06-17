using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Bridge between the streaming ToF output and the RK4 damping searcher.
/// Subscribes to ToFSensorOutput.OnDistance, watches for a bump in the
/// stream, accumulates the full profile (with pre-roll), hands it to the
/// solver, and pushes the winning damping value to the actuator.
///
/// Exposes OnBumpCaptured and OnSolveCompleted events with snapshot
/// payloads, intended for BumpPipelineVisualizer / loggers.
/// </summary>
[DisallowMultipleComponent]
public class BumpPipeline : MonoBehaviour, IModeReceiver
{
    public enum State { Idle, Accumulating, Cooldown }

    [Header("Sources")]
    [SerializeField] private ToFSensorOutput tof;
    [UnityEngine.Serialization.FormerlySerializedAs("terrain")]
    [SerializeField] private TerrainWheel terrainWheel;

    [Header("Geometry")]
    [Tooltip("Flat-belt ToF reading (m) in SIMULATION — the digital sensor's standoff in the Unity scene. " +
             "Bump height = standoff − distance, so it must match the flat-belt reading for the active mode.")]
    [UnityEngine.Serialization.FormerlySerializedAs("nominalStandoff")]
    [SerializeField] private float nominalStandoffSim = 0.055f;
    [Tooltip("Flat-belt ToF reading (m) in TWINNING — the physical ToF→drum standoff on the real rig.")]
    [SerializeField] private float nominalStandoffTwin = 0.15f;
    [SerializeField] private float sensorLead = 0.10f;

    // Active standoff for the current mode. ModeManager pushes the mode via OnModeChanged;
    // defaults to the sim value (the default mode) so any reading before that is still correct.
    private float _nominalStandoff = 0.055f;
    private float _baseline = 0.055f;     // adaptive flat-distance estimate (drum-wobble rejection)
    private float _lastSampleTime;

    public float NominalStandoff => _nominalStandoff;
    /// <summary>Known geometric belt distance from the ToF beam to the wheel contact patch.
    /// The scheduler uses this to validate its auto-measured wheel offset.</summary>
    public float SensorLead => sensorLead;

    [Header("Baseline (drum-wobble rejection)")]
    [Tooltip("Track the flat-belt distance with a SLOW baseline so a slowly oscillating drum " +
             "(eccentricity/wobble, ~once per revolution) is not mistaken for a bump. The baseline " +
             "adapts ONLY while flat (Idle), so it can never chase a real bump. Off = use the fixed " +
             "nominal standoff as the reference.")]
    [SerializeField] private bool adaptiveBaseline = true;
    [Tooltip("Baseline tracking time-constant (s): >> a bump's duration but < one drum revolution. " +
             "The once-per-rev wobble is slow and bumps are sharp, so this separates them.")]
    [Min(0.01f)] [SerializeField] private float baselineTau = 0.5f;

    [Header("Detection")]
    [Tooltip("Deviation from flat (m) that starts a capture. Magnitude-based, so " +
             "raised bumps (surface nearer) and potholes (surface farther) both trigger.")]
    [SerializeField] private float triggerHeight = 0.005f;
    [SerializeField] private int endSamples = 5;
    [SerializeField] private int preRollSamples = 32;
    [SerializeField] private int maxProfileSamples = 1024;
    [SerializeField] private int minBumpSamples = 4;
    [SerializeField] private int cooldownSamples = 16;

    [Header("Solver")]
    // Public so QuarterCarConfig can set them directly (single source of truth, no reflection).
    public float mass = 5.0f;
    public float stiffness = 20000f;
    public float cMin = 50f;
    public float cMax = 3000f;
    [SerializeField] private int cCandidates = 256;
    [SerializeField] private int solverSteps = 1500;
    [SerializeField] private float solverDt = 0.001f;
    [Tooltip("Comfort cost minimised per bump = peak + (this λ)·RMS sprung acceleration.\n" +
             "λ = 0  → pure PEAK (best-validated for isolated bumps; but drives c very low → ringing).\n" +
             "λ large → weights RMS / ride-feel (penalises the underdamped ringing of continuous " +
             "roughness, raises the chosen c toward critical). No jerk term by design.")]
    [Min(0f)] public float dampingRmsWeight = 1.0f;

    [Header("Diagnostics (read-only)")]
    public State CurrentState = State.Idle;
    [SerializeField] private int bumpsSeen;
    [SerializeField] private float lastSolveMs;
    [SerializeField] private float lastBestC;
    [SerializeField] private float lastPredictiveSlackMs;

    public int BumpsSeen => bumpsSeen;
    public float LastSolveMs => lastSolveMs;
    public float LastBestC => lastBestC;
    public float LastPredictiveSlackMs => lastPredictiveSlackMs;

    // ---- visualization events ---------------------------------------

    public struct BumpSnapshot
    {
        public NativeArray<float> Heights;     // valid until next bump
        public int Count;
        public float LengthMeters;
    }

    public struct SolveSnapshot
    {
        public NativeArray<float> Peaks;       // valid until next solve
        public int Count;
        public int BestIndex;
        public float BestC, BestPeak, BestRms;
        public float CMin, CMax;
        public float PeakMin, PeakMax;
        public float SolveMs, SlackMs;
        public float ObservedBeltPos;   // belt TraveledDistance at the bump's LEADING edge (capture time, NO solve latency)
        public float BumpLengthMeters;  // leading→trailing belt length of the bump (for clearance timing)
    }

    [Serializable] public class BumpEvent : UnityEvent<BumpSnapshot> { }
    [Serializable] public class SolveEvent : UnityEvent<SolveSnapshot> { }

    public BumpEvent OnBumpCaptured = new BumpEvent();
    public SolveEvent OnSolveCompleted = new SolveEvent();

    // ---- internal state ----------------------------------------------

    private struct Sample { public float Pos; public float Height; }
    private Sample[] _ring;
    private int _ringHead;
    private int _ringCount;

    private readonly List<Sample> _profile = new List<Sample>(2048);
    private int _flatStreak;
    private int _cooldownLeft;
    private float _leadingEdgePos;

    // Snapshot buffers handed to listeners (one-deep).
    private NativeArray<float> _snapHeights;
    private NativeArray<float> _snapPeaks;

    private struct DampingSearchInFlight
    {
        public bool Active;
        public JobHandle Handle;
        public NativeArray<float> Candidates;
        public NativeArray<SearchResult> Results;
        public RoadProfile Road;
        public float StartTime;
        public float BumpLengthMeters;
        public float ObservedBeltPos;   // leading-edge belt position, captured at BeginBump
    }
    private DampingSearchInFlight _inFlight;

    // ---- lifecycle ---------------------------------------------------

    private void OnEnable()
    {
        if (_ring == null || _ring.Length != Mathf.Max(1, preRollSamples))
            _ring = new Sample[Mathf.Max(1, preRollSamples)];
        _ringHead = 0;
        _ringCount = 0;
        _baseline = _nominalStandoff;
        _lastSampleTime = Time.time;

        if (tof != null) tof.OnDistance.AddListener(OnDistance);
    }

    private void OnDisable()
    {
        if (tof != null) tof.OnDistance.RemoveListener(OnDistance);
        CompleteInFlightImmediate();
        DisposeSnapshots();
    }

    // ModeManager pushes the active mode. The flat-belt ToF standoff differs between the
    // Unity scene (sim) and the physical rig (twin), so pick the matching baseline.
    public void OnModeChanged(TwinMode mode)
    {
        _nominalStandoff = mode == TwinMode.Twinning ? nominalStandoffTwin : nominalStandoffSim;
        _baseline = _nominalStandoff;   // reseed the wobble baseline for the new mode
    }

    // Burst compiles a job on its first execution, which makes the FIRST real
    // solve dramatically slower than steady state. That anomalous latency used to
    // poison the scheduler's wheel-offset calibration. Run a throwaway solve here
    // so Burst is warm before the first bump is ever captured.
    private void Start() => WarmUpSolver();

    private void WarmUpSolver()
    {
        const int n = 8;
        var dummyRoad = RoadProfile.FromSamples(new float[n], 0f, 0.001f, Allocator.TempJob);
        var cands = QuarterCarSolver.LinSpace(cMin, cMax, 8, Allocator.TempJob);
        var results = new NativeArray<SearchResult>(8, Allocator.TempJob);

        new DampingSearchJob
        {
            m = mass, k = stiffness,
            t0 = 0f, dt = solverDt, steps = 16, x0 = 0f, v0 = 0f,
            roadT0 = dummyRoad.T0, roadDt = dummyRoad.Dt,
            roadY = dummyRoad.Y, roadDy = dummyRoad.Dy,
            candidatesC = cands, results = results
        }.Schedule(8, 1).Complete();

        cands.Dispose();
        results.Dispose();
        dummyRoad.Dispose();
    }

    // ---- ToF stream handler ------------------------------------------

    private void OnDistance(float distance)
    {
        // Reference for "flat". With adaptiveBaseline, track the slow flat-belt distance (updated
        // ONLY while Idle, so it can never chase a bump) to reject drum wobble; else fixed standoff.
        float reference = _nominalStandoff;
        if (adaptiveBaseline && distance >= 0f)
        {
            float now = Time.time, dt = now - _lastSampleTime; _lastSampleTime = now;
            if (CurrentState == State.Idle && dt > 0f && dt < 0.5f)
                _baseline += (1f - Mathf.Exp(-dt / Mathf.Max(1e-4f, baselineTau))) * (distance - _baseline);
            reference = _baseline;
        }
        float height = distance < 0f ? 0f : reference - distance;
        float pos = terrainWheel != null ? terrainWheel.TraveledDistance : 0f;

        switch (CurrentState)
        {
            case State.Idle:
                PushRing(pos, height);
                if (Mathf.Abs(height) >= triggerHeight) BeginBump(pos);
                break;

            case State.Accumulating:
                AppendBump(pos, height);
                if (Mathf.Abs(height) < triggerHeight) _flatStreak++; else _flatStreak = 0;
                if (_flatStreak >= endSamples || _profile.Count >= maxProfileSamples)
                    EndBump();
                break;

            case State.Cooldown:
                PushRing(pos, height);
                if (Mathf.Abs(height) >= triggerHeight) { BeginBump(pos); break; }
                if (--_cooldownLeft <= 0) CurrentState = State.Idle;
                break;
        }
    }

    private void PushRing(float pos, float h)
    {
        _ring[_ringHead] = new Sample { Pos = pos, Height = h };
        _ringHead = (_ringHead + 1) % _ring.Length;
        if (_ringCount < _ring.Length) _ringCount++;
    }

    private void BeginBump(float leadingPos)
    {
        CurrentState = State.Accumulating;
        _profile.Clear();
        _flatStreak = 0;
        _leadingEdgePos = leadingPos;

        int start = (_ringHead - _ringCount + _ring.Length) % _ring.Length;
        for (int i = 0; i < _ringCount; i++)
            _profile.Add(_ring[(start + i) % _ring.Length]);
    }

    private void AppendBump(float pos, float h) =>
        _profile.Add(new Sample { Pos = pos, Height = h });

    private void EndBump()
    {
        CurrentState = State.Cooldown;
        _cooldownLeft = cooldownSamples;

        int contentLen = _profile.Count - Mathf.Max(0, _flatStreak - endSamples);
        if (contentLen < minBumpSamples) return;

        bumpsSeen++;
        float bumpLength = _profile[contentLen - 1].Pos - _leadingEdgePos;

        // -- snapshot for visualization --
        if (_snapHeights.IsCreated) _snapHeights.Dispose();
        _snapHeights = new NativeArray<float>(contentLen, Allocator.Persistent);
        for (int i = 0; i < contentLen; i++) _snapHeights[i] = _profile[i].Height;
        OnBumpCaptured.Invoke(new BumpSnapshot
        {
            Heights = _snapHeights,
            Count = contentLen,
            LengthMeters = bumpLength
        });

        ScheduleSolve(contentLen, bumpLength);
    }

    // ---- solver dispatch (async) -------------------------------------

    private void ScheduleSolve(int contentLen, float bumpLengthMeters)
    {
        if (_inFlight.Active)
        {
            Debug.LogWarning("[BumpPipeline] dropping bump: prior solve still running");
            return;
        }

        float terrainSpeed = Mathf.Max(0.01f, terrainWheel != null ? terrainWheel.LinearSpeed : 1f);
        float startPos = _profile[0].Pos;
        float endPos = _profile[contentLen - 1].Pos;
        float lengthM = Mathf.Max(0.001f, endPos - startPos);

        int gridN = Mathf.Min(maxProfileSamples, Mathf.Max(64, contentLen));
        var yFine = new float[gridN];

        int j = 0;
        for (int i = 0; i < gridN; i++)
        {
            float p = startPos + (i / (float)(gridN - 1)) * lengthM;
            while (j < contentLen - 2 && _profile[j + 1].Pos < p) j++;
            float p0 = _profile[j].Pos, p1 = _profile[j + 1].Pos;
            float t = (p1 > p0) ? (p - p0) / (p1 - p0) : 0f;
            yFine[i] = Mathf.Lerp(_profile[j].Height, _profile[j + 1].Height, t);
        }

        float dtRoad = (lengthM / terrainSpeed) / (gridN - 1);
        var road = RoadProfile.FromSamples(yFine, 0f, dtRoad, Allocator.TempJob);
        var cands = QuarterCarSolver.LinSpace(cMin, cMax, cCandidates, Allocator.TempJob);
        var results = new NativeArray<SearchResult>(cCandidates, Allocator.TempJob);

        var job = new DampingSearchJob
        {
            m = mass,
            k = stiffness,
            t0 = 0f,
            dt = solverDt,
            steps = solverSteps,
            x0 = 0f,
            v0 = 0f,
            roadT0 = road.T0,
            roadDt = road.Dt,
            roadY = road.Y,
            roadDy = road.Dy,
            candidatesC = cands,
            results = results
        };

        _inFlight = new DampingSearchInFlight
        {
            Active = true,
            Handle = job.Schedule(cCandidates, 16),
            Candidates = cands,
            Results = results,
            Road = road,
            StartTime = Time.realtimeSinceStartup,
            BumpLengthMeters = bumpLengthMeters,
            // LEADING-edge belt position. The accelerometer jolt fires when the bump's
            // leading edge contacts the wheel, so referencing the schedule to the leading
            // edge makes target = the next contact (error ≈ 0). Using the trailing edge
            // instead landed target ~one bump-length late. Captured at BeginBump → still
            // latency-free (no async solve-completion drift).
            ObservedBeltPos = _leadingEdgePos
        };
    }

    private void Update()
    {
        if (!_inFlight.Active || !_inFlight.Handle.IsCompleted) return;

        _inFlight.Handle.Complete();
        lastSolveMs = (Time.realtimeSinceStartup - _inFlight.StartTime) * 1000f;

        int bestIdx = 0;
        float bestPeak = float.MaxValue, bestRms = 0f, bestCost = float.MaxValue;
        float peakMin = float.MaxValue, peakMax = float.MinValue;
        float cMinSeen = float.MaxValue, cMaxSeen = float.MinValue;

        for (int i = 0; i < _inFlight.Results.Length; i++)
        {
            var r = _inFlight.Results[i];
            // Comfort cost: peak acceleration plus a weighted RMS term (no jerk term —
            // a positive jerk reward would make an active damper increase jerk).
            float cost = r.peakAccel + dampingRmsWeight * r.rmsAccel;
            if (cost < bestCost) { bestCost = cost; bestPeak = r.peakAccel; bestRms = r.rmsAccel; bestIdx = i; }
            if (r.peakAccel < peakMin) peakMin = r.peakAccel;
            if (r.peakAccel > peakMax) peakMax = r.peakAccel;
            if (r.dampingC < cMinSeen) cMinSeen = r.dampingC;
            if (r.dampingC > cMaxSeen) cMaxSeen = r.dampingC;
        }

        lastBestC = _inFlight.Results[bestIdx].dampingC;

        float terrainSpeed = Mathf.Max(0.01f, terrainWheel != null ? terrainWheel.LinearSpeed : 1f);
        float distLeft = sensorLead - _inFlight.BumpLengthMeters;
        lastPredictiveSlackMs = (distLeft / terrainSpeed) * 1000f - lastSolveMs;

        // -- snapshot for visualization --
        if (_snapPeaks.IsCreated) _snapPeaks.Dispose();
        _snapPeaks = new NativeArray<float>(_inFlight.Results.Length, Allocator.Persistent);
        for (int i = 0; i < _inFlight.Results.Length; i++)
            _snapPeaks[i] = _inFlight.Results[i].peakAccel;

        OnSolveCompleted.Invoke(new SolveSnapshot
        {
            Peaks = _snapPeaks,
            Count = _inFlight.Results.Length,
            BestIndex = bestIdx,
            BestC = lastBestC,
            BestPeak = bestPeak,
            BestRms = bestRms,
            CMin = cMinSeen,
            CMax = cMaxSeen,
            PeakMin = peakMin,
            PeakMax = peakMax,
            SolveMs = lastSolveMs,
            SlackMs = lastPredictiveSlackMs,
            ObservedBeltPos = _inFlight.ObservedBeltPos,
            BumpLengthMeters = _inFlight.BumpLengthMeters
        });

        DisposeInFlight();
    }

    private void DisposeInFlight()
    {
        _inFlight.Candidates.Dispose();
        _inFlight.Results.Dispose();
        _inFlight.Road.Dispose();
        _inFlight.Active = false;
    }

    private void CompleteInFlightImmediate()
    {
        if (!_inFlight.Active) return;
        _inFlight.Handle.Complete();
        DisposeInFlight();
    }

    private void DisposeSnapshots()
    {
        if (_snapHeights.IsCreated) _snapHeights.Dispose();
        if (_snapPeaks.IsCreated) _snapPeaks.Dispose();
    }
}