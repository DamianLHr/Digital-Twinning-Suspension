using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Quarter-Car Active Suspension Rig with PID + Sky-Hook Controller.
/// Includes a real-time on-screen plot and statistics panel.
///
/// Hierarchy expected:
///   Road (kinematic Rigidbody)
///   Unsprung Mass (Rigidbody + ConfigurableJoint to Road)        [Tire]
///   Sprung Mass   (Rigidbody + ConfigurableJoint to Unsprung)    [Suspension]
///   
/// Made with Claude for testing purpouses, not final code!
/// 
/// </summary>
[DisallowMultipleComponent]
public class QuarterCarRig : MonoBehaviour
{
    public enum ControlMode { Passive, PositionPID, SkyHook, PositionPID_Plus_SkyHook }
    public enum DisturbanceMode { None, Step, Sine, SweptSine }

    // ============================================================================
    [Header("◆ RIGIDBODY REFERENCES ◆")]

    [Tooltip("The chassis 'body' — represents 1/4 of total vehicle mass.\n" +
             "Typical: 200–400 kg. Constraints: Freeze Position X/Z + Freeze Rotation.")]
    public Rigidbody sprungMass;

    [Tooltip("The 'wheel + hub' assembly. Typical: 20–50 kg.\n" +
             "Same constraints as sprung mass.")]
    public Rigidbody unsprungMass;

    [Tooltip("Kinematic Rigidbody acting as the road surface.\n" +
             "MUST have isKinematic = true. Its Y position is the disturbance input.")]
    public Rigidbody road;

    [Header("◆ JOINT REFERENCES ◆")]

    [Tooltip("ConfigurableJoint on the Sprung Mass, ConnectedBody = Unsprung Mass.")]
    public ConfigurableJoint suspensionJoint;

    [Tooltip("ConfigurableJoint on the Unsprung Mass, ConnectedBody = Road.")]
    public ConfigurableJoint tireJoint;

    // ============================================================================
    [Header("◆ SUSPENSION (passive spring/damper) ◆")]

    [Tooltip("Spring k_s in N/m. Rule of thumb: m_sprung * g / desired_sag.")]
    public float suspensionSpring = 24500f;

    [Tooltip("Damping c_s in Ns/m. Aim 30–70% of critical = 2*sqrt(k*m).")]
    public float suspensionDamper = 1800f;

    [Tooltip("Max compression/extension in metres. Real cars: 0.10–0.20.")]
    public float suspensionTravel = 0.15f;

    // ============================================================================
    [Header("◆ TIRE (passive spring/damper) ◆")]

    [Tooltip("Tire spring k_t in N/m. ~10× stiffer than suspension. Typical: 150 000–250 000.")]
    public float tireSpring = 200000f;

    [Tooltip("Tire damping in Ns/m. Keep low; a little helps solver stability.")]
    public float tireDamper = 200f;

    [Tooltip("Max tire deflection in metres. Typical: 0.03–0.05.")]
    public float tireTravel = 0.05f;

    // ============================================================================
    [Header("◆ CONTROL MODE ◆")]

    [Tooltip("Which controller is active. Start Passive, then PositionPID, then SkyHook.")]
    public ControlMode controlMode = ControlMode.PositionPID;

    [Tooltip("Desired world-Y of the sprung mass when controller is active.")]
    public float targetHeight = 1.0f;

    // ============================================================================
    [Header("◆ PID GAINS ◆")]

    [Tooltip("Proportional. Force per metre of error.")]
    public float Kp = 8000f;

    [Tooltip("Integral. Eliminates steady-state error. Add last and sparingly.")]
    public float Ki = 200f;

    [Tooltip("Derivative. Damps overshoot. Sensitive to noise.")]
    public float Kd = 2000f;

    [Tooltip("Sky-hook gain C_sky in Ns/m. Damps absolute body velocity.")]
    public float skyHookGain = 2500f;

    [Tooltip("Hard cap on |active force| in N. Models actuator saturation.")]
    public float maxActiveForce = 5000f;

    [Tooltip("Anti-windup: integral term clamped to ±this value.")]
    public float integralClamp = 1000f;

    [Tooltip("If true, derivative uses d(measurement)/dt. Prevents derivative kick.")]
    public bool derivativeOnMeasurement = true;

    // ============================================================================
    [Header("◆ ROAD DISTURBANCE ◆")]

    [Tooltip("Type of road input. Step = best for tuning. SweptSine = transfer function.")]
    public DisturbanceMode disturbanceMode = DisturbanceMode.Step;

    [Tooltip("Step height in metres.")] public float stepHeight = 0.05f;
    [Tooltip("Time the step fires.")] public float stepTime = 2.0f;
    [Tooltip("Sine amplitude (m).")] public float sineAmplitude = 0.02f;
    [Tooltip("Sine frequency (Hz). Body resonance ~1–2 Hz.")] public float sineFrequency = 1.5f;
    [Tooltip("Sweep start frequency.")] public float sweepStartHz = 0.5f;
    [Tooltip("Sweep end frequency.")] public float sweepEndHz = 20f;
    [Tooltip("Sweep duration (s).")] public float sweepDuration = 30f;

    // ============================================================================
    [Header("◆ DATA LOGGING ◆")]

    [Tooltip("MASTER ON/OFF for CSV logging.\n" +
             "  ✓ Checked  → writes one row per FixedUpdate.\n" +
             "  ☐ Unchecked → no file is created, zero I/O.")]
    public bool logToCsv = false;

    [Tooltip("Output filename. Saved to Application.persistentDataPath.")]
    public string csvFilename = "qcar_log.csv";

    // ============================================================================
    [Header("◆ REAL-TIME PLOT ◆")]

    [Tooltip("MASTER ON/OFF for the on-screen plot + statistics overlay.\n" +
             "Disable for a slight performance gain or cleaner screenshots.")]
    public bool showPlot = true;

    [Tooltip("Plot texture width in pixels.")] public int plotWidth = 600;
    [Tooltip("Plot texture height in pixels.")] public int plotHeight = 220;

    [Tooltip("How many seconds of history to keep on screen.\n" +
             "Longer = more context, but less detail per pixel.")]
    public float historySeconds = 3.0f;

    [Tooltip("Top-left screen pixel of the plot.")]
    public Vector2 plotScreenPos = new Vector2(10, 140);

    // ============================================================================
    [Header("◆ RUNTIME (read-only) ◆")]
    [SerializeField] private float _sprungY;
    [SerializeField] private float _currentError;
    [SerializeField] private float _activeForce;
    [SerializeField] private float _roadY;
    [SerializeField, Tooltip("Root-mean-square error over the plot window.")] private float _rmsError;
    [SerializeField, Tooltip("Peak |error| in the plot window.")] private float _peakError;
    [SerializeField, Tooltip("RMS sprung-mass vertical acceleration (m/s²).\n" +
                             "This is THE ride-comfort metric. Lower = smoother.")]
    private float _rmsAccel;
    [SerializeField, Tooltip("Peak |active force| in the plot window.")] private float _peakForce;

    // ---------- internal state ----------
    private float _integral, _prevError, _prevSprungY, _prevVelY;
    private float _roadBaseY, _startTime;
    private StreamWriter _writer;

    // ---------- plot internals ----------
    private float[] _hRoad, _hUnsprung, _hSprung, _hError, _hForce, _hAccel;
    private int _hCap, _hHead, _hCount;
    private Texture2D _plotTex;
    private Color32[] _plotPixels;
    private bool _plotDirty;

    private static readonly Color32 C_BG = new Color32(20, 20, 28, 230);
    private static readonly Color32 C_GRID = new Color32(60, 60, 72, 255);
    private static readonly Color32 C_ZERO = new Color32(90, 90, 110, 255);
    private static readonly Color32 C_ROAD = new Color32(150, 150, 150, 255);
    private static readonly Color32 C_UNSPRUNG = new Color32(255, 180, 60, 255);
    private static readonly Color32 C_SPRUNG = new Color32(80, 200, 255, 255);
    private static readonly Color32 C_TARGET = new Color32(120, 255, 120, 255);
    private static readonly Color32 C_FORCE = new Color32(255, 100, 130, 255);

    // ============================================================================
    void Start()
    {
        if (!Validate()) { enabled = false; return; }

        ConfigureJoint(suspensionJoint, suspensionSpring, suspensionDamper, suspensionTravel);
        ConfigureJoint(tireJoint, tireSpring, tireDamper, tireTravel);

        _roadBaseY = road.position.y;
        _startTime = Time.time;
        _prevSprungY = sprungMass.position.y;

        InitPlotBuffers();
        if (logToCsv) OpenLog();
    }

    bool Validate()
    {
        if (!sprungMass || !unsprungMass || !road) { Debug.LogError("QuarterCarRig: assign all 3 Rigidbodies."); return false; }
        if (!suspensionJoint || !tireJoint) { Debug.LogError("QuarterCarRig: assign both ConfigurableJoints."); return false; }
        if (!road.isKinematic) { Debug.LogError("QuarterCarRig: road must be kinematic."); return false; }
        return true;
    }

    void ConfigureJoint(ConfigurableJoint j, float spring, float damper, float travel)
    {
        j.xMotion = ConfigurableJointMotion.Locked;
        j.yMotion = ConfigurableJointMotion.Limited;
        j.zMotion = ConfigurableJointMotion.Locked;
        j.angularXMotion = ConfigurableJointMotion.Locked;
        j.angularYMotion = ConfigurableJointMotion.Locked;
        j.angularZMotion = ConfigurableJointMotion.Locked;

        var limit = j.linearLimit; limit.limit = travel; j.linearLimit = limit;

        j.yDrive = new JointDrive
        {
            positionSpring = spring,
            positionDamper = damper,
            maximumForce = Mathf.Infinity
        };
        j.targetPosition = Vector3.zero;

        var rb = j.GetComponent<Rigidbody>();
        rb.solverIterations = 20;
        rb.solverVelocityIterations = 10;
    }

    // ============================================================================
    void InitPlotBuffers()
    {
        _hCap = Mathf.Max(64, Mathf.RoundToInt(historySeconds / Time.fixedDeltaTime));
        _hRoad = new float[_hCap];
        _hUnsprung = new float[_hCap];
        _hSprung = new float[_hCap];
        _hError = new float[_hCap];
        _hForce = new float[_hCap];
        _hAccel = new float[_hCap];
        _hHead = 0; _hCount = 0;

        _plotTex = new Texture2D(plotWidth, plotHeight, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        _plotPixels = new Color32[plotWidth * plotHeight];
    }

    void PushSample(float road, float unsprung, float sprung, float error, float force, float accel)
    {
        _hRoad[_hHead] = road;
        _hUnsprung[_hHead] = unsprung;
        _hSprung[_hHead] = sprung;
        _hError[_hHead] = error;
        _hForce[_hHead] = force;
        _hAccel[_hHead] = accel;
        _hHead = (_hHead + 1) % _hCap;
        if (_hCount < _hCap) _hCount++;
        _plotDirty = true;
    }

    // ============================================================================
    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        float t = Time.time - _startTime;

        road.MovePosition(new Vector3(road.position.x, _roadBaseY + Disturbance(t), road.position.z));

        _sprungY = sprungMass.position.y;
        _roadY = road.position.y;
        _currentError = targetHeight - _sprungY;
        float sprungVelY = (_sprungY - _prevSprungY) / dt;
        float sprungAccY = (sprungVelY - _prevVelY) / dt;

        float force = 0f;
        if (controlMode == ControlMode.PositionPID || controlMode == ControlMode.PositionPID_Plus_SkyHook)
            force += ComputePID(_currentError, _sprungY, dt);
        if (controlMode == ControlMode.SkyHook || controlMode == ControlMode.PositionPID_Plus_SkyHook)
            force += -skyHookGain * sprungVelY;

        force = Mathf.Clamp(force, -maxActiveForce, maxActiveForce);
        _activeForce = force;

        if (controlMode != ControlMode.Passive)
        {
            sprungMass.AddForce(Vector3.up * force, ForceMode.Force);
            unsprungMass.AddForce(Vector3.down * force, ForceMode.Force);
        }

        if (_writer != null)
            _writer.WriteLine($"{t:F4},{_roadY:F5},{unsprungMass.position.y:F5},{_sprungY:F5},{sprungVelY:F5},{_currentError:F5},{force:F2}");

        PushSample(_roadY, unsprungMass.position.y, _sprungY, _currentError, force, sprungAccY);
        ComputeStats();

        _prevSprungY = _sprungY;
        _prevVelY = sprungVelY;
    }

    float ComputePID(float error, float measurement, float dt)
    {
        _integral += error * dt;
        _integral = Mathf.Clamp(_integral, -integralClamp, integralClamp);

        float deriv = derivativeOnMeasurement
            ? -(measurement - _prevSprungY) / dt
            : (error - _prevError) / dt;

        _prevError = error;
        return Kp * error + Ki * _integral + Kd * deriv;
    }

    float Disturbance(float t)
    {
        switch (disturbanceMode)
        {
            case DisturbanceMode.Step: return t >= stepTime ? stepHeight : 0f;
            case DisturbanceMode.Sine: return sineAmplitude * Mathf.Sin(2f * Mathf.PI * sineFrequency * t);
            case DisturbanceMode.SweptSine:
                float u = Mathf.Clamp01(t / sweepDuration);
                float instFreq = Mathf.Lerp(sweepStartHz, sweepEndHz, u);
                return sineAmplitude * Mathf.Sin(2f * Mathf.PI * instFreq * t);
            default: return 0f;
        }
    }

    void ComputeStats()
    {
        if (_hCount == 0) return;
        float sumE2 = 0, sumA2 = 0, peakE = 0, peakF = 0;
        for (int i = 0; i < _hCount; i++)
        {
            int idx = (_hHead - 1 - i + _hCap) % _hCap;
            float e = _hError[idx], a = _hAccel[idx], f = Mathf.Abs(_hForce[idx]);
            sumE2 += e * e; sumA2 += a * a;
            if (Mathf.Abs(e) > peakE) peakE = Mathf.Abs(e);
            if (f > peakF) peakF = f;
        }
        _rmsError = Mathf.Sqrt(sumE2 / _hCount);
        _rmsAccel = Mathf.Sqrt(sumA2 / _hCount);
        _peakError = peakE;
        _peakForce = peakF;
    }

    // ============================================================================
    // Plot rendering — runs at frame rate, not physics rate.
    void Update()
    {
        if (!showPlot || _plotTex == null || !_plotDirty) return;
        RebuildPlot();
        _plotTex.SetPixels32(_plotPixels);
        _plotTex.Apply(false);
        _plotDirty = false;
    }

    void RebuildPlot()
    {
        // clear
        for (int i = 0; i < _plotPixels.Length; i++) _plotPixels[i] = C_BG;

        // split: top 65% positions, bottom 35% force
        int topH = (plotHeight * 65) / 100;
        int botStart = topH + 1;
        int botH = plotHeight - botStart;

        // separator
        for (int x = 0; x < plotWidth; x++) _plotPixels[topH * plotWidth + x] = C_GRID;

        // gridlines in top
        for (int g = 1; g < 4; g++)
        {
            int y = (g * topH) / 4;
            for (int x = 0; x < plotWidth; x += 2) _plotPixels[y * plotWidth + x] = C_GRID;
        }

        if (_hCount < 2) return;

        // autoscale Y for top plot
        float minY = float.MaxValue, maxY = float.MinValue;
        for (int i = 0; i < _hCount; i++)
        {
            int idx = (_hHead - _hCount + i + _hCap) % _hCap;
            float a = _hRoad[idx], b = _hUnsprung[idx], c = _hSprung[idx];
            if (a < minY) minY = a; if (a > maxY) maxY = a;
            if (b < minY) minY = b; if (b > maxY) maxY = b;
            if (c < minY) minY = c; if (c > maxY) maxY = c;
        }
        if (targetHeight < minY) minY = targetHeight;
        if (targetHeight > maxY) maxY = targetHeight;
        float pad = (maxY - minY) * 0.1f + 0.005f;
        minY -= pad; maxY += pad;
        float rangeY = Mathf.Max(1e-4f, maxY - minY);

        // target line (dashed green)
        int tgtPx = Mathf.RoundToInt((targetHeight - minY) / rangeY * (topH - 1));
        if (tgtPx >= 0 && tgtPx < topH)
            for (int x = 0; x < plotWidth; x += 6)
                _plotPixels[tgtPx * plotWidth + x] = C_TARGET;

        // series
        DrawSeries(_hRoad, minY, rangeY, 0, topH, C_ROAD);
        DrawSeries(_hUnsprung, minY, rangeY, 0, topH, C_UNSPRUNG);
        DrawSeries(_hSprung, minY, rangeY, 0, topH, C_SPRUNG);

        // bottom: force with zero line in middle
        int midF = botStart + botH / 2;
        for (int x = 0; x < plotWidth; x += 2) _plotPixels[midF * plotWidth + x] = C_ZERO;
        float fMax = Mathf.Max(maxActiveForce, 100f);
        DrawForce(_hForce, fMax, botStart, botH);
    }

    void DrawSeries(float[] arr, float minY, float rangeY, int yStart, int yH, Color32 c)
    {
        int prevX = -1, prevY = -1;
        for (int i = 0; i < _hCount; i++)
        {
            int idx = (_hHead - _hCount + i + _hCap) % _hCap;
            int px = (i * (plotWidth - 1)) / Mathf.Max(1, _hCap - 1);
            int py = yStart + Mathf.Clamp(Mathf.RoundToInt((arr[idx] - minY) / rangeY * (yH - 1)), 0, yH - 1);
            if (prevX >= 0) DrawLine(prevX, prevY, px, py, c);
            prevX = px; prevY = py;
        }
    }

    void DrawForce(float[] arr, float fMax, int yStart, int yH)
    {
        int mid = yStart + yH / 2;
        int halfH = (yH / 2) - 1;
        int prevX = -1, prevY = -1;
        for (int i = 0; i < _hCount; i++)
        {
            int idx = (_hHead - _hCount + i + _hCap) % _hCap;
            int px = (i * (plotWidth - 1)) / Mathf.Max(1, _hCap - 1);
            float v = Mathf.Clamp(arr[idx] / fMax, -1f, 1f);
            int py = Mathf.Clamp(mid + Mathf.RoundToInt(v * halfH), yStart, yStart + yH - 1);
            if (prevX >= 0) DrawLine(prevX, prevY, px, py, C_FORCE);
            prevX = px; prevY = py;
        }
    }

    void DrawLine(int x0, int y0, int x1, int y1, Color32 c)
    {
        int dx = Mathf.Abs(x1 - x0), dy = -Mathf.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;
        while (true)
        {
            if ((uint)x0 < (uint)plotWidth && (uint)y0 < (uint)plotHeight)
                _plotPixels[y0 * plotWidth + x0] = c;
            if (x0 == x1 && y0 == y1) break;
            int e2 = err << 1;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    // ============================================================================
    void OpenLog()
    {
        string path = Path.Combine(Application.persistentDataPath, csvFilename);
        _writer = new StreamWriter(path, false, Encoding.UTF8) { AutoFlush = false };
        _writer.WriteLine("time,road_y,unsprung_y,sprung_y,sprung_vel_y,error,active_force_N");
        Debug.Log($"QuarterCarRig: logging to {path}");
    }

    void OnDestroy()
    {
        if (_writer != null) { _writer.Flush(); _writer.Close(); _writer = null; }
        if (_plotTex != null) Destroy(_plotTex);
    }

    void OnValidate()
    {
        if (!Application.isPlaying) return;
        if (suspensionJoint) ConfigureJoint(suspensionJoint, suspensionSpring, suspensionDamper, suspensionTravel);
        if (tireJoint) ConfigureJoint(tireJoint, tireSpring, tireDamper, tireTravel);
    }

    // ============================================================================
    void OnGUI()
    {
        // Live HUD
        GUI.Box(new Rect(10, 10, 260, 120),
            $"Mode: {controlMode}\n" +
            $"Sprung Y: {_sprungY:F4}\n" +
            $"Target:   {targetHeight:F4}\n" +
            $"Error:    {_currentError:+0.0000;-0.0000}\n" +
            $"Force:    {_activeForce:+0;-0} N\n" +
            $"Road Y:   {_roadY:F4}");

        if (!showPlot || _plotTex == null) return;

        float x = plotScreenPos.x, y = plotScreenPos.y;

        // Plot texture
        GUI.DrawTexture(new Rect(x, y, plotWidth, plotHeight), _plotTex);

        // Legend strip
        float ly = y + plotHeight + 4;
        DrawLegendSwatch(x + 0, ly, "Road", new Color(0.59f, 0.59f, 0.59f));
        DrawLegendSwatch(x + 70, ly, "Unsprung", new Color(1.00f, 0.71f, 0.24f));
        DrawLegendSwatch(x + 160, ly, "Sprung", new Color(0.31f, 0.78f, 1.00f));
        DrawLegendSwatch(x + 230, ly, "Target", new Color(0.47f, 1.00f, 0.47f));
        DrawLegendSwatch(x + 300, ly, "Force", new Color(1.00f, 0.39f, 0.51f));

        // Statistics panel
        GUI.Box(new Rect(x + plotWidth + 10, y, 220, 130),
            "STATISTICS (window)\n\n" +
            $"RMS error:    {_rmsError * 1000f,7:F1} mm\n" +
            $"Peak error:   {_peakError * 1000f,7:F1} mm\n" +
            $"RMS accel:    {_rmsAccel,7:F2} m/s²\n" +
            $"Peak |force|: {_peakForce,7:F0} N");
    }

    void DrawLegendSwatch(float x, float y, string label, Color col)
    {
        var prev = GUI.color;
        GUI.color = col;
        GUI.Box(new Rect(x, y + 3, 10, 10), GUIContent.none);
        GUI.color = Color.white;
        GUI.Label(new Rect(x + 14, y, 70, 18), label);
        GUI.color = prev;
    }
}