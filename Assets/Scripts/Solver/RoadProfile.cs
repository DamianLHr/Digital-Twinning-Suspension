using System;
using Unity.Collections;

namespace Suspension.Solver
{
    /// <summary>
    /// Road displacement y(t) and velocity dy(t) baked onto a uniform grid,
    /// ready to hand to the Burst jobs. Build once, dispose when done.
    ///
    /// In the live system, feed this from the ToF bump profile + belt speed
    /// instead of the static control points.
    /// </summary>
    public sealed class RoadProfile : IDisposable
    {
        public NativeArray<float> Y;
        public NativeArray<float> Dy;
        public float T0;
        public float Dt;
        public int   Length => Y.IsCreated ? Y.Length : 0;

        /// <summary>Bake from PCHIP control points (the scipy setup).</summary>
        public static RoadProfile FromControlPoints(
            float[] tPoints, float[] yPoints,
            float tStart, float tEnd, int samples, Allocator allocator)
        {
            if (samples < 2) throw new ArgumentException("need >= 2 samples");

            float dt = (tEnd - tStart) / (samples - 1);
            var grid = new float[samples];
            for (int i = 0; i < samples; i++) grid[i] = tStart + i * dt;

            float[] yFine  = Pchip.Interpolate(tPoints, yPoints, grid);
            float[] dyFine = Gradient(yFine, dt);   // mirrors np.gradient

            return new RoadProfile
            {
                T0 = tStart,
                Dt = dt,
                Y  = new NativeArray<float>(yFine,  allocator),
                Dy = new NativeArray<float>(dyFine, allocator)
            };
        }

        /// <summary>Bake directly from an already-sampled displacement array.</summary>
        public static RoadProfile FromSamples(
            float[] yFine, float tStart, float dt, Allocator allocator)
        {
            return new RoadProfile
            {
                T0 = tStart,
                Dt = dt,
                Y  = new NativeArray<float>(yFine, allocator),
                Dy = new NativeArray<float>(Gradient(yFine, dt), allocator)
            };
        }

        // Second-order central differences interior, first-order at the ends
        // (identical to numpy.gradient default behaviour on a uniform grid).
        private static float[] Gradient(float[] f, float h)
        {
            int n = f.Length;
            var g = new float[n];
            if (n == 1) { g[0] = 0f; return g; }
            g[0]     = (f[1] - f[0]) / h;
            g[n - 1] = (f[n - 1] - f[n - 2]) / h;
            for (int i = 1; i < n - 1; i++)
                g[i] = (f[i + 1] - f[i - 1]) / (2f * h);
            return g;
        }

        public void Dispose()
        {
            if (Y.IsCreated)  Y.Dispose();
            if (Dy.IsCreated) Dy.Dispose();
        }
    }
}
