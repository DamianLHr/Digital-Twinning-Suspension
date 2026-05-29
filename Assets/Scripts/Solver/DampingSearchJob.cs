using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Suspension.Solver
{
    /// <summary>Cost of one candidate damping value over the integration window.</summary>
    public struct SearchResult
    {
        public float dampingC;
        public float peakAccel;   // max |a| — the primary objective
        public float rmsAccel;    // RMS a  — secondary / smoothness metric
    }

    /// <summary>
    /// Evaluates many damping candidates in parallel. Each work item runs an
    /// independent RK4 integration with its own c and records only the cost —
    /// no trajectory is stored, so this is cheap enough to run per bump in the
    /// predictive control loop. Mass and stiffness are fixed; only c varies.
    /// </summary>
    [BurstCompile]
    public struct DampingSearchJob : IJobParallelFor
    {
        // Fixed parameters
        public float m, k;

        // Integration
        public float t0, dt;
        public int   steps;
        public float x0, v0;

        // Road
        public float roadT0, roadDt;
        [ReadOnly] public NativeArray<float> roadY;
        [ReadOnly] public NativeArray<float> roadDy;

        // Candidates in, costs out (same length)
        [ReadOnly]  public NativeArray<float>        candidatesC;
        [WriteOnly] public NativeArray<SearchResult> results;

        public void Execute(int index)
        {
            float c = candidatesC[index];
            float x = x0, v = v0;

            // initial sample
            float y  = RoadMath.Sample(roadY,  roadT0, roadDt, t0);
            float dy = RoadMath.Sample(roadDy, roadT0, roadDt, t0);
            float a  = RoadMath.Accel(x, v, y, dy, m, c, k);
            float peak  = math.abs(a);
            float sumSq = a * a;

            for (int i = 1; i <= steps; i++)
            {
                float t = t0 + (i - 1) * dt;
                RoadMath.Rk4Step(ref x, ref v, t, dt, m, c, k,
                                 roadY, roadDy, roadT0, roadDt);

                float tn = t0 + i * dt;
                y  = RoadMath.Sample(roadY,  roadT0, roadDt, tn);
                dy = RoadMath.Sample(roadDy, roadT0, roadDt, tn);
                a  = RoadMath.Accel(x, v, y, dy, m, c, k);

                peak   = math.max(peak, math.abs(a));
                sumSq += a * a;
            }

            results[index] = new SearchResult
            {
                dampingC  = c,
                peakAccel = peak,
                rmsAccel  = math.sqrt(sumSq / (steps + 1))
            };
        }
    }
}
