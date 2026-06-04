using System.IO;
using System.Text;
using Unity.Collections;
using UnityEngine;

/// <summary>
/// Reproduces the Python script: same road, same m/c/k, plus a parallel
/// damping sweep. Logs peak/RMS acceleration and (optionally) writes a CSV
/// to persistentDataPath so you can plot it and compare against scipy.
/// </summary>
public class QuarterCarSolverDemo : MonoBehaviour
{
    [Header("System")]
    public float m = 1.0f;
    public float c = 2.0f;
    public float k = 20.0f;

    [Header("Integration")]
    public float duration = 20f;
    public int   steps    = 2000;     // dt = duration / steps = 0.01 s
    public int   roadSamples = 4000;  // fine grid for PCHIP + gradient

    [Header("Damping search")]
    public float cMin = 0.5f;
    public float cMax = 30f;
    public int   cCandidates = 256;
    public bool  minimizeRms = false;

    [Header("Output")]
    public bool writeCsv = true;

    private void Start()
    {
        // Same control points as the scipy version
        float[] tPts = { 0, 2, 2.5f, 3, 3.5f, 4, 20 };
        float[] yPts = { 0, 0, 0.5f, 1, 0.5f, 0, 0 };

        using var road = RoadProfile.FromControlPoints(
            tPts, yPts, 0f, duration, roadSamples, Allocator.TempJob);

        float dt = duration / steps;

        // 1) Trajectory for the nominal c
        using var traj = QuarterCarSolver.SolveTrajectory(
            road, m, c, k, 0f, dt, steps, 0f, 0f, Allocator.TempJob);

        float peak = 0f;
        for (int i = 0; i < traj.A.Length; i++)
            peak = Mathf.Max(peak, Mathf.Abs(traj.A[i]));
        Debug.Log("[QuarterCar] nominal c=" + c + "  peak |accel| = " + peak.ToString("F4"));

        if (writeCsv) WriteCsv(traj);

        // 2) Parallel damping search
        var candidates = QuarterCarSolver.LinSpace(cMin, cMax, cCandidates, Allocator.TempJob);
        int best = QuarterCarSolver.FindBestDamping(
            road, candidates, m, k, 0f, dt, steps, 0f, 0f,
            Allocator.TempJob, out var results, minimizeRms);

        Debug.Log("[QuarterCar] best c = " + results[best].dampingC.ToString("F3") +
                  "  peak = " + results[best].peakAccel.ToString("F4") +
                  "  rms = "  + results[best].rmsAccel.ToString("F4") +
                  "  (over " + cCandidates + " candidates)");

        candidates.Dispose();
        results.Dispose();
    }

    private void WriteCsv(QuarterCarSolver.Trajectory traj)
    {
        var sb = new StringBuilder("t,x,v,a\n");
        for (int i = 0; i < traj.T.Length; i++)
            sb.Append(traj.T[i]).Append(',')
              .Append(traj.X[i]).Append(',')
              .Append(traj.V[i]).Append(',')
              .Append(traj.A[i]).Append('\n');

        string path = Path.Combine(Application.persistentDataPath, "system_response.csv");
        File.WriteAllText(path, sb.ToString());
        Debug.Log("[QuarterCar] wrote " + path);
    }
}
