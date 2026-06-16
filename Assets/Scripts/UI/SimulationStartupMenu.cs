using System.Globalization;
using UnityEngine;

/// <summary>
/// Pre-simulation startup menu (IMGUI, so it needs no Canvas wiring). The user picks the
/// operating mode, configures the DIAG scenario, optionally sets serial params, then launches:
///   • FREE RUN — apply mode + speed and run forever, collecting NOTHING (always available,
///     the default), or
///   • COLLECT  — warm up, record for a fixed time, flush the CSVs (a DIAG experiment run).
///
/// UX: when expanded it is a MODAL FULLSCREEN overlay — an opaque backdrop drawn on top
/// (GUI.depth) so the 3D scene and the other IMGUI menus don't bleed through; while open it
/// disables the VisualizerManager (top-left) and CreditsPanel (top-right) so nothing overlaps
/// or is clickable behind it. While a run is active it collapses to a small bottom-left bar
/// (the top corners are taken by those two menus). The rig is frozen (Time.timeScale = 0)
/// while the menu is open, so the simulation truly starts only on launch.
///
/// Drives existing systems only — ModeManager.SetMode, ScenarioRunner (free/collect/stop),
/// PicoSerialTransport (port/baud/connect). All auto-found if left unwired.
/// </summary>
[DisallowMultipleComponent]
public class SimulationStartupMenu : MonoBehaviour
{
    [Header("Wiring (auto-found if left empty)")]
    [SerializeField] private ModeManager modeManager;
    [SerializeField] private ScenarioRunner scenario;
    [SerializeField] private PicoSerialTransport transport;
    [SerializeField] private VisualizerManager visualizers;
    [SerializeField] private CreditsPanel credits;

    [Header("Behaviour")]
    [Tooltip("Freeze the rig (Time.timeScale = 0) while the menu is open.")]
    [SerializeField] private bool freezeWhileOpen = true;
    [Tooltip("Open the menu when the scene starts.")]
    [SerializeField] private bool openOnStart = true;

    private enum Tab { Scenario, Serial }
    private Tab _tab = Tab.Scenario;
    private bool _open;

    // Editable text buffers (parsed on launch so a half-typed number never breaks a run).
    private int _policyIdx;     // 0 = Predictive, 1 = Constant
    private string _constantC, _beltSpeed, _runName, _warmup, _collect, _port, _baud;
    private string _status = "";

    // styles + solid textures (IMGUI default skin is small + translucent)
    private bool _stylesReady;
    private GUIStyle _title, _subtitle, _header, _label, _button, _launch, _tabStyle, _field, _bar, _box;
    private Texture2D _backdropTex, _panelTex, _barTex;

    private void Awake()
    {
        if (modeManager == null) modeManager = FindFirstObjectByType<ModeManager>();
        if (scenario == null)    scenario    = FindFirstObjectByType<ScenarioRunner>();
        if (transport == null)   transport   = FindFirstObjectByType<PicoSerialTransport>();
        if (visualizers == null) visualizers = FindFirstObjectByType<VisualizerManager>();
        if (credits == null)     credits     = FindFirstObjectByType<CreditsPanel>();
        SyncBuffersFromState();
    }

    private void Start() => SetOpen(openOnStart);

    private void SyncBuffersFromState()
    {
        if (scenario != null)
        {
            _policyIdx = (int)scenario.Policy;
            _constantC = F(scenario.ConstantC);
            _beltSpeed = F(scenario.BeltSpeed);
            _runName   = scenario.RunName;
            _warmup    = F(scenario.WarmupSeconds);
            _collect   = F(scenario.CollectSeconds);
        }
        if (transport != null)
        {
            _port = transport.PortName;
            _baud = transport.BaudRate.ToString(CultureInfo.InvariantCulture);
        }
    }

    // Open => modal: freeze the rig and hide the other IMGUI menus so nothing overlaps/bleeds.
    private void SetOpen(bool open)
    {
        _open = open;
        if (freezeWhileOpen) Time.timeScale = open ? 0f : 1f;
        if (visualizers != null) visualizers.enabled = !open;
        if (credits != null) credits.enabled = !open;
    }

    // ---- IMGUI ----

    private void OnGUI()
    {
        EnsureStyles();

        if (!_open) { GUI.depth = 0; DrawRunningBar(); return; }

        GUI.depth = -1000;   // draw the modal on top of every other IMGUI panel

        // Opaque fullscreen backdrop.
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _backdropTex, ScaleMode.StretchToFill);

        // Header band.
        GUI.Label(new Rect(0, 28, Screen.width, 34), "Digital-Twinning Suspension", _title);
        GUI.Label(new Rect(0, 64, Screen.width, 22), "Simulation setup", _subtitle);

        // Centered content column.
        float colW = Mathf.Min(640f, Screen.width - 80f);
        float colX = (Screen.width - colW) / 2f;
        float top = 104f, footerH = 96f;
        var col = new Rect(colX, top, colW, Screen.height - top - footerH);

        GUILayout.BeginArea(col);
        DrawModeRow();
        GUILayout.Space(12);

        _tab = (Tab)GUILayout.Toolbar((int)_tab, new[] { "Scenario / data", "Serial device" },
                                      _tabStyle, GUILayout.Height(32));
        GUILayout.Space(10);

        GUILayout.BeginVertical(_box);
        if (_tab == Tab.Scenario) DrawScenarioTab(); else DrawSerialTab();
        GUILayout.EndVertical();
        GUILayout.EndArea();

        DrawFooter(colX, colW, footerH);
    }

    private void DrawModeRow()
    {
        GUILayout.Label("Operating mode", _header);
        if (modeManager != null)
        {
            int m = GUILayout.Toolbar((int)modeManager.Mode,
                                      new[] { "Simulation", "Twin (hardware)" }, _tabStyle, GUILayout.Height(34));
            if (m != (int)modeManager.Mode) modeManager.SetMode((TwinMode)m);
        }
        else GUILayout.Label("(no ModeManager in scene)", _label);
    }

    private void DrawScenarioTab()
    {
        GUILayout.Label("Damping policy", _header);
        _policyIdx = GUILayout.Toolbar(_policyIdx, new[] { "Predictive", "Constant" },
                                       _tabStyle, GUILayout.Height(30));
        if (_policyIdx == 1) Row("Constant c (N·s/m)", ref _constantC);
        Row("Belt speed (m/s)", ref _beltSpeed);

        GUILayout.Space(12);
        GUILayout.Label("Data collection", _header);
        Row("Run name", ref _runName);
        Row("Warm-up (s)", ref _warmup);
        Row("Collect (s)", ref _collect);
        GUILayout.Label("Free run ignores warm-up/collect and writes no files.", _label);
    }

    private void DrawSerialTab()
    {
        if (transport == null)
        {
            GUILayout.Label("No PicoSerialTransport in the scene.", _label);
            return;
        }
        GUILayout.Label("Serial port (used in Twin mode)", _header);
        Row("Port", ref _port);
        Row("Baud", ref _baud);
        GUILayout.Space(6);
        GUILayout.Label("Status: " + (transport.Connected ? "connected" : "disconnected"), _label);
        GUILayout.Space(6);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Apply & connect", _button, GUILayout.Height(32))) { ApplySerial(); transport.Connect(); }
        if (GUILayout.Button("Disconnect", _button, GUILayout.Height(32))) transport.Disconnect();
        GUILayout.EndHorizontal();
    }

    private void DrawFooter(float colX, float colW, float footerH)
    {
        var footer = new Rect(colX, Screen.height - footerH + 8f, colW, footerH - 16f);
        GUILayout.BeginArea(footer);

        GUILayout.BeginHorizontal();
        GUI.enabled = scenario != null;
        if (GUILayout.Button("Free run\n(collect nothing)", _launch, GUILayout.Height(52))) Launch(false);

        GUI.enabled = scenario != null && !string.IsNullOrWhiteSpace(_runName);
        if (GUILayout.Button("Run + collect data", _launch, GUILayout.Height(52))) Launch(true);
        GUI.enabled = true;
        GUILayout.EndHorizontal();

        string hint = scenario == null ? "No ScenarioRunner found — cannot start."
                    : string.IsNullOrWhiteSpace(_runName) ? "Set a run name (Scenario tab) to enable data collection."
                    : string.IsNullOrEmpty(_status) ? "Free run is always available."
                    : _status;
        GUILayout.Label(hint, _label);
        GUILayout.EndArea();
    }

    private void DrawRunningBar()
    {
        bool rec = scenario != null && scenario.Running;
        var bar = new Rect(12f, Screen.height - 60f, 300f, 48f);
        GUI.DrawTexture(bar, _barTex, ScaleMode.StretchToFill);

        GUILayout.BeginArea(new Rect(bar.x + 8f, bar.y + 6f, bar.width - 16f, bar.height - 12f));
        string mode = modeManager != null ? modeManager.Mode.ToString() : "?";
        string pol = scenario != null ? scenario.Policy.ToString() : "?";
        GUILayout.Label($"{mode}  -  {pol}" + (rec ? "  -  recording" : ""), _bar);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Menu", GUILayout.Height(22))) ReturnToMenu();
        if (rec && GUILayout.Button("Stop", GUILayout.Height(22))) scenario.Stop();
        GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }

    // ---- actions ----

    private void Launch(bool collect)
    {
        if (scenario == null) { _status = "No ScenarioRunner — cannot start."; return; }
        ApplyScenario();
        SetOpen(false);                       // unfreezes + restores the other menus
        if (collect) scenario.StartCollectRun();
        else scenario.StartFreeRun();
    }

    private void ReturnToMenu()
    {
        if (scenario != null) scenario.Stop();
        SyncBuffersFromState();
        _tab = Tab.Scenario;
        _status = "";
        SetOpen(true);                        // refreezes + hides the other menus
    }

    private void ApplyScenario()
    {
        scenario.Policy = (DampingPolicySelector.Policy)_policyIdx;
        scenario.ConstantC = ParseF(_constantC, scenario.ConstantC);
        scenario.BeltSpeed = ParseF(_beltSpeed, scenario.BeltSpeed);
        scenario.WarmupSeconds = ParseF(_warmup, scenario.WarmupSeconds);
        scenario.CollectSeconds = ParseF(_collect, scenario.CollectSeconds);
        if (!string.IsNullOrWhiteSpace(_runName)) scenario.RunName = _runName.Trim();
        if (modeManager != null) scenario.ModeTag = modeManager.Mode.ToString();   // stamp CSV metadata
    }

    private void ApplySerial()
    {
        transport.PortName = (_port ?? "").Trim();
        if (int.TryParse(_baud, NumberStyles.Integer, CultureInfo.InvariantCulture, out int b))
            transport.BaudRate = b;
    }

    // ---- helpers ----

    private void Row(string label, ref string value)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, _label, GUILayout.Width(150));
        value = GUILayout.TextField(value ?? "", _field, GUILayout.Height(24));
        GUILayout.EndHorizontal();
        GUILayout.Space(3);
    }

    private static float ParseF(string s, float fallback) =>
        float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : fallback;

    private static string F(float v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    private static Texture2D Solid(Color c)
    {
        var t = new Texture2D(1, 1) { hideFlags = HideFlags.DontSave };
        t.SetPixel(0, 0, c);
        t.Apply();
        return t;
    }

    private void EnsureStyles()
    {
        if (_stylesReady) return;
        _stylesReady = true;

        _backdropTex = Solid(new Color(0.07f, 0.08f, 0.10f, 0.98f));   // near-opaque modal backdrop
        _panelTex    = Solid(new Color(0.14f, 0.15f, 0.18f, 1f));
        _barTex      = Solid(new Color(0.10f, 0.11f, 0.14f, 0.95f));

        var white = Color.white;
        var grey = new Color(0.72f, 0.74f, 0.78f);

        _title    = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold,
                                                   alignment = TextAnchor.MiddleCenter, normal = { textColor = white } };
        _subtitle = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleCenter,
                                                   normal = { textColor = grey } };
        _header   = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, normal = { textColor = white } };
        _label    = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true, normal = { textColor = grey } };
        _button   = new GUIStyle(GUI.skin.button) { fontSize = 14 };
        _launch   = new GUIStyle(GUI.skin.button) { fontSize = 16, fontStyle = FontStyle.Bold };
        _tabStyle = new GUIStyle(GUI.skin.button) { fontSize = 14 };
        _field    = new GUIStyle(GUI.skin.textField) { fontSize = 14 };
        _bar      = new GUIStyle(GUI.skin.label) { fontSize = 12, normal = { textColor = white } };

        // Private solid-background box for the content panel (don't mutate the shared
        // GUI.skin.box — CreditsPanel and others use it).
        _box = new GUIStyle(GUI.skin.box)
        {
            padding = new RectOffset(14, 14, 12, 12),
            normal = { background = _panelTex }
        };
    }
}
