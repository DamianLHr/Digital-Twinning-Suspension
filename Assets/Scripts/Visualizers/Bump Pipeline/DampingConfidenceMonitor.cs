using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Surfaces, at runtime, the conditions under which a trustworthy damping solve
/// CANNOT be produced — instead of failing silently. Taps the BumpPipeline and
/// DampingCommandScheduler events plus live belt state, evaluates a set of
/// severity-tagged conditions, exposes them via <see cref="Diagnostics"/> for the
/// UI, and draws a compact banner as an IVisualizerPanel.
///
/// Conditions covered (from the data currently available):
///   • Belt stopped / too slow           • Calibration not established
///   • Under-sampled bump profile         • Negative predictive slack (solve too late)
///   • Best damping at the search boundary (widen cMin/cMax)
///   • Belt speed changed between solve and apply (C computed for a different speed)
/// </summary>
[DisallowMultipleComponent]
public class DampingConfidenceMonitor : MonoBehaviour, IVisualizerPanel
{
    public enum Severity { Info, Warning, Critical }
    public struct Diagnostic { public Severity Sev; public string Message; }

    [Header("Wiring")]
    [SerializeField] private BumpPipeline pipeline;
    [SerializeField] private DampingCommandScheduler scheduler;
    [UnityEngine.Serialization.FormerlySerializedAs("drum")]
    [SerializeField] private TerrainWheel terrainWheel;
    [Tooltip("Optional: the Pico serial transport, for link-health diagnostics in Twinning.")]
    [SerializeField] private PicoSerialTransport transport;

    [Header("Thresholds")]
    [Tooltip("Terrain wheel surface speed (m/s) below which predictive control is flagged as inactive.")]
    [UnityEngine.Serialization.FormerlySerializedAs("minBeltSpeed")]
    [SerializeField] private float minTerrainWheelSpeed = 0.02f;
    [Tooltip("A bump captured with fewer than this many samples is under-resolved.")]
    [SerializeField] private int healthyBumpSamples = 8;
    [Tooltip("Flag when |v_apply − v_solve| / v_solve exceeds this fraction.")]
    [SerializeField] private float speedChangeFrac = 0.2f;

    [Header("Panel")]
    [SerializeField] private bool show = true;
    [SerializeField] private string title = "Solve confidence";
    [SerializeField] private Vector2 anchor = new Vector2(12, 120);
    [SerializeField] private bool floatInWorld = false;
    [SerializeField] private Transform worldAnchorOverride;

    // ---- captured event state (persist until the next event) ----
    private int   _lastBumpSamples = -1;
    private float _lastSlackMs;
    private bool  _haveSolve;
    private bool  _bestAtBoundary;
    private float _speedDeltaFrac;
    private bool  _haveApply;

    private readonly List<Diagnostic> _diags = new List<Diagnostic>();
    public IReadOnlyList<Diagnostic> Diagnostics => _diags;

    private GUIStyle _label;
    private bool _hasManagedRect;
    private Vector2 _managedTopLeft;
    private const float Pad = 6f, LineH = 16f;

    // ---- lifecycle ----

    private void OnEnable()
    {
        if (pipeline != null)
        {
            pipeline.OnBumpCaptured.AddListener(OnBumpCaptured);
            pipeline.OnSolveCompleted.AddListener(OnSolveCompleted);
        }
        if (scheduler != null) scheduler.OnCommandApplied.AddListener(OnApplied);
        VisualizerRegistry.Register(this);
    }

    private void OnDisable()
    {
        if (pipeline != null)
        {
            pipeline.OnBumpCaptured.RemoveListener(OnBumpCaptured);
            pipeline.OnSolveCompleted.RemoveListener(OnSolveCompleted);
        }
        if (scheduler != null) scheduler.OnCommandApplied.RemoveListener(OnApplied);
        VisualizerRegistry.Unregister(this);
        _hasManagedRect = false;
    }

    private void OnBumpCaptured(BumpPipeline.BumpSnapshot s) => _lastBumpSamples = s.Count;

    private void OnSolveCompleted(BumpPipeline.SolveSnapshot s)
    {
        _haveSolve = true;
        _lastSlackMs = s.SlackMs;
        _bestAtBoundary = s.Count > 1 && (s.BestIndex == 0 || s.BestIndex == s.Count - 1);
    }

    private void OnApplied(DampingCommandScheduler.AppliedInfo a)
    {
        _haveApply = true;
        float v = Mathf.Max(0.001f, a.SpeedAtSolve);
        _speedDeltaFrac = Mathf.Abs(a.SpeedAtApply - a.SpeedAtSolve) / v;
    }

    // ---- evaluation ----

    private void Update() => Evaluate();

    private void Evaluate()
    {
        _diags.Clear();

        if (terrainWheel != null && terrainWheel.LinearSpeed < minTerrainWheelSpeed)
            Add(Severity.Warning, $"Terrain wheel stopped/too slow ({terrainWheel.LinearSpeed:0.000} m/s) — predictive control inactive.");

        if (scheduler != null && scheduler.State != DampingCommandScheduler.CalibState.Calibrated)
            Add(Severity.Info, $"Calibration not established ({scheduler.State}) — running default damping.");

        if (_lastBumpSamples >= 0 && _lastBumpSamples < healthyBumpSamples)
            Add(Severity.Warning, $"Under-sampled bump ({_lastBumpSamples} samples < {healthyBumpSamples}) — slow the belt or raise the ToF rate.");

        if (_haveSolve && _lastSlackMs < 0f)
            Add(Severity.Critical, $"Negative predictive slack ({_lastSlackMs:0} ms) — solve finished too late to apply in time.");

        if (_bestAtBoundary)
            Add(Severity.Warning, "Best damping sits at the search boundary — widen cMin/cMax.");

        if (_haveApply && _speedDeltaFrac > speedChangeFrac)
            Add(Severity.Warning, $"Belt speed changed {_speedDeltaFrac * 100f:0}% between solve and apply — C computed for a different speed.");

        if (transport != null)
        {
            if (!transport.Connected)
                Add(Severity.Critical, "Serial link down — no live device data.");
            else if (transport.DroppedPackets > 0)
                Add(Severity.Warning, $"{transport.DroppedPackets} serial packet(s) dropped (packet_id gaps).");
        }
    }

    private void Add(Severity sev, string msg) => _diags.Add(new Diagnostic { Sev = sev, Message = msg });

    // ---- IVisualizerPanel ----

    public string DisplayName => string.IsNullOrEmpty(title) ? GetType().Name : title;
    public string Group => "Control";
    public bool Show { get => show; set => show = value; }
    public Transform WorldAnchor => worldAnchorOverride != null ? worldAnchorOverride : transform;
    public bool FloatInWorld => floatInWorld;
    public Vector2 PanelSize =>
        new Vector2(440f, Pad * 2f + LineH * (Mathf.Max(1, _diags.Count) + 1));

    public void ApplyScreenRect(Vector2 topLeft) { _managedTopLeft = topLeft; _hasManagedRect = true; }

    private void OnGUI()
    {
        if (!show) return;
        if (_label == null)
            _label = new GUIStyle(GUI.skin.label)
            { fontSize = 11, richText = true, normal = { textColor = Color.white } };

        Vector2 origin = _hasManagedRect ? _managedTopLeft : anchor;
        Vector2 sz = PanelSize;
        var box = new Rect(origin.x, origin.y, sz.x, sz.y);

        var old = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.7f);
        GUI.DrawTexture(box, Texture2D.whiteTexture);
        GUI.color = old;

        float x = box.x + Pad, y = box.y + Pad, w = box.width - Pad * 2f;
        bool allClear = _diags.Count == 0;
        string head = allClear ? "<color=#7f7>OK</color>" : $"<color=#fc6>{_diags.Count} issue(s)</color>";
        GUI.Label(new Rect(x, y, w, LineH), $"<b>{title}</b>   {head}", _label);
        y += LineH;

        if (allClear)
        {
            GUI.Label(new Rect(x, y, w, LineH), "  <color=#7f7>conditions nominal</color>", _label);
            return;
        }

        for (int i = 0; i < _diags.Count; i++)
        {
            GUI.Label(new Rect(x, y, w, LineH), $"  {Tag(_diags[i].Sev)} {_diags[i].Message}", _label);
            y += LineH;
        }
    }

    private static string Tag(Severity s) => s switch
    {
        Severity.Critical => "<color=#f66>[CRIT]</color>",
        Severity.Warning  => "<color=#fc6>[WARN]</color>",
        _                 => "<color=#9cf>[info]</color>",
    };
}
