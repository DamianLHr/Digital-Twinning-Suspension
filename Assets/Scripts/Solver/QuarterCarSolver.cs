using System;
using Unity.Collections;
using Unity.Jobs;

namespace Suspension.Solver
{
    /// <summary>
    /// Managed entry points that schedule the Burst jobs and hand back results.
    /// Keep allocations on the caller's chosen Allocator and dispose them.
    /// </summary>
    public static class QuarterCarSolver
    {
        public struct Trajectory : IDisposable
        {
            public NativeArray<float> T, X, V, A;
            public void Dispose()
            {
                if (T.IsCreated) T.Dispose();
                if (X.IsCreated) X.Dispose();
                if (V.IsCreated) V.Dispose();
                if (A.IsCreated) A.Dispose();
            }
        }

        /// <summary>Full RK4 trajectory — the solve_ivp equivalent.</summary>
        public static Trajectory SolveTrajectory(
            RoadProfile road, float m, float c, float k,
            float t0, float dt, int steps, float x0, float v0,
            Allocator allocator)
        {
            int len = steps + 1;
            var traj = new Trajectory
            {
                T = new NativeArray<float>(len, allocator),
                X = new NativeArray<float>(len, allocator),
                V = new NativeArray<float>(len, allocator),
                A = new NativeArray<float>(len, allocator)
            };

            new QuarterCarRk4Job
            {
                m = m, c = c, k = k,
                t0 = t0, dt = dt, steps = steps, x0 = x0, v0 = v0,
                roadT0 = road.T0, roadDt = road.Dt, roadY = road.Y, roadDy = road.Dy,
                outT = traj.T, outX = traj.X, outV = traj.V, outA = traj.A
            }.Schedule().Complete();

            return traj;
        }

        /// <summary>
        /// Sweep damping candidates in parallel; returns the index of the best
        /// one and (via out) the full result table. Caller disposes results.
        /// </summary>
        public static int FindBestDamping(
            RoadProfile road, NativeArray<float> candidates,
            float m, float k, float t0, float dt, int steps, float x0, float v0,
            Allocator allocator, out NativeArray<SearchResult> results,
            bool minimizeRms = false, int innerBatch = 16)
        {
            results = new NativeArray<SearchResult>(candidates.Length, allocator);

            new DampingSearchJob
            {
                m = m, k = k,
                t0 = t0, dt = dt, steps = steps, x0 = x0, v0 = v0,
                roadT0 = road.T0, roadDt = road.Dt, roadY = road.Y, roadDy = road.Dy,
                candidatesC = candidates, results = results
            }.Schedule(candidates.Length, innerBatch).Complete();

            int   best = 0;
            float bestCost = float.MaxValue;
            for (int i = 0; i < results.Length; i++)
            {
                float cost = minimizeRms ? results[i].rmsAccel : results[i].peakAccel;
                if (cost < bestCost) { bestCost = cost; best = i; }
            }
            return best;
        }

        /// <summary>Helper: build a linearly-spaced candidate array (remember to Dispose).</summary>
        public static NativeArray<float> LinSpace(float lo, float hi, int count, Allocator allocator)
        {
            var a = new NativeArray<float>(count, allocator);
            if (count == 1) { a[0] = lo; return a; }
            float step = (hi - lo) / (count - 1);
            for (int i = 0; i < count; i++) a[i] = lo + i * step;
            return a;
        }
    }
}
