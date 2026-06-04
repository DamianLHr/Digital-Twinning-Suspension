using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

/// <summary>
/// Burst RK4 integrator producing the full trajectory (t, x, v, a).
/// Output arrays must have length steps + 1 (index 0 is the initial state).
/// This is the equivalent of the scipy solve_ivp run — use it to verify or plot.
/// </summary>
[BurstCompile]
public struct QuarterCarRk4Job : IJob
{
    // Parameters
    public float m, c, k;

    // Integration
    public float t0;       // start time
    public float dt;       // fixed step
    public int   steps;    // number of RK4 steps
    public float x0, v0;   // initial state

    // Road (uniformly sampled)
    public float roadT0, roadDt;
    [ReadOnly] public NativeArray<float> roadY;
    [ReadOnly] public NativeArray<float> roadDy;

    // Outputs (length steps + 1)
    [WriteOnly] public NativeArray<float> outT;
    [WriteOnly] public NativeArray<float> outX;
    [WriteOnly] public NativeArray<float> outV;
    [WriteOnly] public NativeArray<float> outA;

    public void Execute()
    {
        float x = x0, v = v0;
        Record(0, x, v, t0);

        for (int i = 1; i <= steps; i++)
        {
            float t = t0 + (i - 1) * dt;
            RoadMath.Rk4Step(ref x, ref v, t, dt, m, c, k,
                             roadY, roadDy, roadT0, roadDt);
            Record(i, x, v, t0 + i * dt);   // recompute t to avoid float drift
        }
    }

    private void Record(int i, float x, float v, float t)
    {
        float y  = RoadMath.Sample(roadY,  roadT0, roadDt, t);
        float dy = RoadMath.Sample(roadDy, roadT0, roadDt, t);
        outT[i] = t;
        outX[i] = x;
        outV[i] = v;
        outA[i] = RoadMath.Accel(x, v, y, dy, m, c, k);
    }
}
