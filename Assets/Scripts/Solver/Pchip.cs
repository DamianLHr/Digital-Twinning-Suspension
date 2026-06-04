using System;

/// <summary>
/// Monotone piecewise-cubic Hermite interpolation (Fritsch–Carlson), matching
/// scipy's pchip_interpolate including its end-derivative handling. Managed
/// only — used once to bake the road onto a uniform grid; not Burst code.
/// </summary>
public static class Pchip
{
    public static float[] Interpolate(float[] xs, float[] ys, float[] query)
    {
        int n = xs.Length;
        if (n != ys.Length) throw new ArgumentException("xs and ys length mismatch");

        var d = Slopes(xs, ys);              // Hermite derivatives at the knots
        var h = new float[n - 1];
        for (int i = 0; i < n - 1; i++) h[i] = xs[i + 1] - xs[i];

        var outv = new float[query.Length];
        for (int q = 0; q < query.Length; q++)
        {
            float xq = query[q];
            // clamp into range (road is fully defined over [xs[0], xs[n-1]])
            if (xq <= xs[0])      { outv[q] = ys[0];     continue; }
            if (xq >= xs[n - 1])  { outv[q] = ys[n - 1]; continue; }

            int i = FindInterval(xs, xq);
            float s   = (xq - xs[i]) / h[i];
            float s2  = s * s, s3 = s2 * s;
            float h00 =  2f * s3 - 3f * s2 + 1f;
            float h10 =       s3 - 2f * s2 + s;
            float h01 = -2f * s3 + 3f * s2;
            float h11 =       s3 -      s2;
            outv[q] = h00 * ys[i] + h10 * h[i] * d[i]
                    + h01 * ys[i + 1] + h11 * h[i] * d[i + 1];
        }
        return outv;
    }

    private static float[] Slopes(float[] x, float[] y)
    {
        int n = x.Length;
        var d = new float[n];

        if (n == 2)   // linear
        {
            float s = (y[1] - y[0]) / (x[1] - x[0]);
            d[0] = d[1] = s;
            return d;
        }

        var h     = new float[n - 1];
        var delta = new float[n - 1];
        for (int i = 0; i < n - 1; i++)
        {
            h[i]     = x[i + 1] - x[i];
            delta[i] = (y[i + 1] - y[i]) / h[i];
        }

        // interior: weighted harmonic mean, zeroed at extrema
        for (int i = 1; i < n - 1; i++)
        {
            if (delta[i - 1] * delta[i] > 0f)
            {
                float w1 = 2f * h[i] + h[i - 1];
                float w2 = h[i] + 2f * h[i - 1];
                d[i] = (w1 + w2) / (w1 / delta[i - 1] + w2 / delta[i]);
            }
            else d[i] = 0f;
        }

        d[0]     = EdgeSlope(h[0], h[1], delta[0], delta[1]);
        d[n - 1] = EdgeSlope(h[n - 2], h[n - 3], delta[n - 2], delta[n - 3]);
        return d;
    }

    // scipy's pchip _edge_case for a one-sided endpoint derivative
    private static float EdgeSlope(float h0, float h1, float del0, float del1)
    {
        float d = ((2f * h0 + h1) * del0 - h0 * del1) / (h0 + h1);
        if (Sign(d) != Sign(del0))
            d = 0f;
        else if (Sign(del0) != Sign(del1) && Math.Abs(d) > 3f * Math.Abs(del0))
            d = 3f * del0;
        return d;
    }

    private static int FindInterval(float[] xs, float xq)
    {
        int lo = 0, hi = xs.Length - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) >> 1;
            if (xs[mid] > xq) hi = mid; else lo = mid;
        }
        return lo;
    }

    private static int Sign(float v) => v > 0f ? 1 : (v < 0f ? -1 : 0);
}
