using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Passive data collector for the DIAG suite. Subscribes ONLY to existing event
/// surfaces and, while recording, writes two CSVs to <c>persistentDataPath</c>:
///
///   &lt;run&gt;_timeseries.csv — one row per accelerometer sample:
///       t, beltPos, speed, accelMag, accelX, accelY, accelZ, travel, c
///   &lt;run&gt;_bumps.csv — one row per solved bump:
///       bumpId, observedPos, bestC, predictedPeak, predictedRms, actualPeak,
///       target, jolt, landingErrorMm, speedAtSolve, speedAtApply
///
/// Both files carry a <c>#</c>-prefixed metadata header (run, date, drum Ø, mode,
/// policy, …). Records nothing until <see cref="StartRecording"/> is called, so a
/// free-run leaves it idle. No edits to any existing script — it only listens.
/// </summary>
[DisallowMultipleComponent]
public class RunDataRecorder : MonoBehaviour
{
    [Header("Sources (existing surfaces only)")]
    [SerializeField] private AccelerometerOutput accel;
    [SerializeField] private PositionOutput position;
    [SerializeField] private DampingCommand dampingCommand;
    [SerializeField] private BumpPipeline pipeline;
    [SerializeField] private DampingCommandScheduler scheduler;
    [SerializeField] private TerrainWheel terrainWheel;

    [Header("Settings")]
    [Tooltip("Rest reading (g) subtracted from accel.y to get the bump's dynamic vertical accel. " +
             "Acceleration is in g, so this is 1.")]
    [SerializeField] private float gravityBaseline = 1.0f;
    [Tooltip("A jolt is attributed to a bump only within this fraction of the wheel offset of its " +
             "target. Keep narrower than the gap between bumps (circumference ÷ bump count) or it " +
             "can grab a neighbour's jolt.")]
    [SerializeField] private float joltMatchFraction = 0.1f;

    [Header("Output")]
    [Tooltip("Subfolder UNDER Assets to write CSVs into (Editor only) — keeps the data in the " +
             "project so the in-editor analysis window finds it. Empty = persistentDataPath. " +
             "Builds always use persistentDataPath.")]
    [SerializeField] private string assetsSubfolder = "DiagnosticsData";

    [Header("Diagnostics (read-only)")]
    [SerializeField] private bool recording;
    [SerializeField] private int rowsWritten;
    [SerializeField] private int bumpsRecorded;
    [SerializeField] private string lastPath;

    public bool Recording => recording;

    // latest snapshots, updated by events between accel samples
    private float _latestTravel, _latestC, _latestSpeed;
    private float _runStart;
    private float _peakSinceJolt;

    private StreamWriter _tsWriter;
    private string _bumpPath, _runNameCache;
    private IDictionary<string, string> _metaCache;
    private readonly StringBuilder _sb = new StringBuilder(160);

    private class BumpRow
    {
        public int Id;
        public float ObservedPos, BestC, PredictedPeak, PredictedRms, Target, SpeedAtSolve;
        public bool Applied; public float AppliedPos, SpeedAtApply;
        public bool Jolted;  public float JoltPos, ActualPeak;
    }
    private readonly List<BumpRow> _bumps = new List<BumpRow>();
    private int _bumpId;

    // ---- lifecycle: subscribe always, gate writes by `recording` ----

    private void OnEnable() => Subscribe();
    private void OnDisable() { Unsubscribe(); if (recording) StopRecording(); }

    private void Subscribe()
    {
        if (accel != null) accel.OnAcceleration.AddListener(OnAcceleration);
        if (position != null) position.OnPosition.AddListener(OnPosition);
        if (dampingCommand != null) dampingCommand.OnDamping.AddListener(OnDamping);
        if (pipeline != null) pipeline.OnSolveCompleted.AddListener(OnSolve);
        if (scheduler != null)
        {
            scheduler.OnCommandApplied.AddListener(OnApplied);
            scheduler.OnJolt.AddListener(OnJolt);
        }
    }

    private void Unsubscribe()
    {
        if (accel != null) accel.OnAcceleration.RemoveListener(OnAcceleration);
        if (position != null) position.OnPosition.RemoveListener(OnPosition);
        if (dampingCommand != null) dampingCommand.OnDamping.RemoveListener(OnDamping);
        if (pipeline != null) pipeline.OnSolveCompleted.RemoveListener(OnSolve);
        if (scheduler != null)
        {
            scheduler.OnCommandApplied.RemoveListener(OnApplied);
            scheduler.OnJolt.RemoveListener(OnJolt);
        }
    }

    // ---- public control ----

    public void StartRecording(string runName, IDictionary<string, string> meta = null)
    {
        if (recording) StopRecording();

        string dir = ResolveOutputDir();
        string tsPath = Path.Combine(dir, runName + "_timeseries.csv");
        _bumpPath = Path.Combine(dir, runName + "_bumps.csv");

        _tsWriter = new StreamWriter(tsPath, false);
        _runNameCache = runName; _metaCache = meta;
        WriteMeta(_tsWriter);
        _tsWriter.WriteLine("t,beltPos,speed,accelMag,accelX,accelY,accelZ,travel,c");

        _bumps.Clear(); _bumpId = 0; rowsWritten = 0; bumpsRecorded = 0;
        _runStart = Time.time; _peakSinceJolt = 0f;
        recording = true; lastPath = tsPath;
        Debug.Log($"[DIAG] recording → {tsPath}");
    }

    [ContextMenu("Stop recording")]
    public void StopRecording()
    {
        if (!recording) return;
        recording = false;
        if (_tsWriter != null) { _tsWriter.Flush(); _tsWriter.Close(); _tsWriter = null; }
        WriteBumps();
        Debug.Log($"[DIAG] stopped: {rowsWritten} samples, {bumpsRecorded} bumps → {_bumpPath}");
#if UNITY_EDITOR
        UnityEditor.AssetDatabase.Refresh();   // surface the new CSVs in the Project window
#endif
    }

    private string ResolveOutputDir()
    {
#if UNITY_EDITOR
        if (!string.IsNullOrEmpty(assetsSubfolder))
        {
            string dir = Path.Combine(Application.dataPath, assetsSubfolder);
            Directory.CreateDirectory(dir);
            return dir;
        }
#endif
        return Application.persistentDataPath;
    }

    [ContextMenu("Start recording (manual)")]
    private void StartManual() => StartRecording("manual");

    // ---- event handlers ----

    private void OnPosition(float travel) => _latestTravel = travel;
    private void OnDamping(float c) => _latestC = c;

    private void OnAcceleration(Vector3 a)
    {
        float proper = Mathf.Abs(a.y - gravityBaseline);   // vertical proper acceleration
        if (proper > _peakSinceJolt) _peakSinceJolt = proper;

        if (!recording) return;
        _latestSpeed = terrainWheel != null ? terrainWheel.LinearSpeed : 0f;
        float beltPos = terrainWheel != null ? terrainWheel.TraveledDistance : 0f;
        WriteTsRow(Time.time - _runStart, beltPos, _latestSpeed, a);
    }

    private void OnSolve(BumpPipeline.SolveSnapshot snap)
    {
        if (!recording) return;
        float target = snap.ObservedBeltPos + (scheduler != null ? scheduler.WheelOffset : 0f);
        _bumps.Add(new BumpRow
        {
            Id = ++_bumpId,
            ObservedPos = snap.ObservedBeltPos,
            BestC = snap.BestC,
            PredictedPeak = snap.BestPeak,
            PredictedRms = snap.BestRms,
            Target = target,
            SpeedAtSolve = terrainWheel != null ? terrainWheel.LinearSpeed : 0f
        });
    }

    private void OnApplied(DampingCommandScheduler.AppliedInfo info)
    {
        if (!recording) return;
        BumpRow b = Match(info.TargetPos, float.MaxValue, requireUnapplied: true, requireUnjolted: false);
        if (b == null) return;
        b.Applied = true; b.AppliedPos = info.AppliedPos;
        b.SpeedAtApply = info.SpeedAtApply; b.SpeedAtSolve = info.SpeedAtSolve;
    }

    private void OnJolt(float joltPos)
    {
        float peak = _peakSinceJolt;
        _peakSinceJolt = 0f;   // start a fresh window for the next bump
        if (!recording) return;

        float offset = scheduler != null ? scheduler.WheelOffset : 0f;
        float tol = offset > 1e-4f ? joltMatchFraction * offset : 0.05f;
        BumpRow b = Match(joltPos, tol, requireUnapplied: false, requireUnjolted: true);
        if (b == null) return;
        b.Jolted = true; b.JoltPos = joltPos; b.ActualPeak = peak;
    }

    private BumpRow Match(float target, float tol, bool requireUnapplied, bool requireUnjolted)
    {
        BumpRow best = null; float bd = float.MaxValue;
        for (int i = 0; i < _bumps.Count; i++)
        {
            var b = _bumps[i];
            if (requireUnapplied && b.Applied) continue;
            if (requireUnjolted && b.Jolted) continue;
            float d = Mathf.Abs(b.Target - target);
            if (d <= tol && d < bd) { bd = d; best = b; }
        }
        return best;
    }

    // ---- writing ----

    private void WriteMeta(StreamWriter w)
    {
        w.WriteLine($"# run={_runNameCache}");
        w.WriteLine($"# date={System.DateTime.Now:o}");
        if (terrainWheel != null) w.WriteLine($"# drumDiameter={F(terrainWheel.Diameter)}");
        w.WriteLine($"# gravityBaseline={F(gravityBaseline)}");
        if (_metaCache != null)
            foreach (var kv in _metaCache) w.WriteLine($"# {kv.Key}={kv.Value}");
    }

    private void WriteTsRow(float t, float beltPos, float speed, Vector3 a)
    {
        _sb.Clear();
        _sb.Append(F(t)).Append(',').Append(F(beltPos)).Append(',').Append(F(speed)).Append(',')
           .Append(F(a.magnitude)).Append(',').Append(F(a.x)).Append(',').Append(F(a.y)).Append(',')
           .Append(F(a.z)).Append(',').Append(F(_latestTravel)).Append(',').Append(F(_latestC));
        _tsWriter.WriteLine(_sb.ToString());
        rowsWritten++;
    }

    private void WriteBumps()
    {
        using var w = new StreamWriter(_bumpPath, false);
        WriteMeta(w);
        w.WriteLine("bumpId,observedPos,bestC,predictedPeak,predictedRms,actualPeak,target,jolt,landingErrorMm,speedAtSolve,speedAtApply");
        foreach (var b in _bumps)
        {
            string landing = b.Jolted ? F((b.JoltPos - b.Target) * 1000f) : "";
            w.WriteLine(string.Join(",", new[]
            {
                b.Id.ToString(CultureInfo.InvariantCulture),
                // Solver predicts in m/s²; convert to g so it matches the measured (g) actualPeak.
                F(b.ObservedPos), F(b.BestC), F(b.PredictedPeak / Ms2PerG), F(b.PredictedRms / Ms2PerG),
                b.Jolted ? F(b.ActualPeak) : "",
                F(b.Target),
                b.Jolted ? F(b.JoltPos) : "",
                landing,
                F(b.SpeedAtSolve),
                b.Applied ? F(b.SpeedAtApply) : ""
            }));
        }
        bumpsRecorded = _bumps.Count;
    }

    private const float Ms2PerG = 9.81f;   // solver outputs m/s²; sensors/CSV use g

    private static string F(float v) => v.ToString("0.######", CultureInfo.InvariantCulture);
}
