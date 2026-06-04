using Unity.Collections;

/// <summary>
/// Branch-light math shared by the solver jobs. Everything here is called
/// from inside [BurstCompile] jobs and gets compiled with them.
///
/// Model (single-DOF quarter car):
///   m * x'' = k*(y - x) + c*(y' - v)
/// with state S = (x, v). y is road displacement, y' = dy road velocity.
/// </summary>
public static class RoadMath
{
    /// <summary>O(1) linear sample of a uniformly-spaced array, clamped at the ends.</summary>
    public static float Sample(in NativeArray<float> a, float t0, float dt, float t)
    {
        int n = a.Length;
        if (n == 0) return 0f;
        float fIdx = (t - t0) / dt;
        if (fIdx <= 0f)     return a[0];
        if (fIdx >= n - 1)  return a[n - 1];
        int   i    = (int)fIdx;
        float frac = fIdx - i;
        return a[i] + (a[i + 1] - a[i]) * frac;
    }

    /// <summary>Derivatives of the state: dx = v, dv = acceleration.</summary>
    public static void Deriv(float x, float v, float y, float dy,
                             float m, float c, float k,
                             out float dx, out float dv)
    {
        dx = v;
        dv = (k * (y - x) + c * (dy - v)) / m;
    }

    /// <summary>Acceleration of the sprung mass at the current state/time.</summary>
    public static float Accel(float x, float v, float y, float dy,
                              float m, float c, float k)
        => (k * (y - x) + c * (dy - v)) / m;

    /// <summary>One classic RK4 step, advancing (x, v) by h. Road sampled at t, t+h/2, t+h.</summary>
    public static void Rk4Step(ref float x, ref float v, float t, float h,
                               float m, float c, float k,
                               in NativeArray<float> roadY, in NativeArray<float> roadDy,
                               float roadT0, float roadDt)
    {
        float y, dy;
        float k1x, k1v, k2x, k2v, k3x, k3v, k4x, k4v;

        y  = Sample(roadY,  roadT0, roadDt, t);
        dy = Sample(roadDy, roadT0, roadDt, t);
        Deriv(x, v, y, dy, m, c, k, out k1x, out k1v);

        float tm = t + 0.5f * h;
        y  = Sample(roadY,  roadT0, roadDt, tm);
        dy = Sample(roadDy, roadT0, roadDt, tm);
        Deriv(x + 0.5f * h * k1x, v + 0.5f * h * k1v, y, dy, m, c, k, out k2x, out k2v);
        Deriv(x + 0.5f * h * k2x, v + 0.5f * h * k2v, y, dy, m, c, k, out k3x, out k3v);

        float te = t + h;
        y  = Sample(roadY,  roadT0, roadDt, te);
        dy = Sample(roadDy, roadT0, roadDt, te);
        Deriv(x + h * k3x, v + h * k3v, y, dy, m, c, k, out k4x, out k4v);

        x += (h / 6f) * (k1x + 2f * k2x + 2f * k3x + k4x);
        v += (h / 6f) * (k1v + 2f * k2v + 2f * k3v + k4v);
    }
}
