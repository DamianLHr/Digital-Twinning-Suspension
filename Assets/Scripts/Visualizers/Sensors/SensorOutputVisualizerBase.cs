using UnityEngine;

/// <summary>
/// Base for per-output debug overlays. Each visualizer subscribes to ONE
/// sensor output's UnityEvent, keeps a rolling history of recent values, and
/// renders a scrolling time-series plot via OnGUI. All sensor-data
/// visualization in the project goes through these classes — the sensors and
/// outputs themselves never draw.
///
/// Drop a concrete visualizer on the same GameObject as its output (it will
/// auto-find it) or assign the output reference in the Inspector.
/// </summary>
public abstract class SensorOutputVisualizerBase : MonoBehaviour, IVisualizerPanel
{
    [Header("Overlay")]
    [SerializeField] protected bool show = true;
    [Tooltip("Label shown above the plot.")]
    [SerializeField] protected string title = "Sensor";
    [Tooltip("Unit suffix shown next to the latest value.")]
    [SerializeField] protected string units = "";
    [Tooltip("Top-left corner of this overlay, in pixels.")]
    [SerializeField] protected Vector2 anchor = new Vector2(12, 12);
    [SerializeField] protected int plotWidth = 280;
    [SerializeField] protected int plotHeight = 90;
    [Tooltip("How many recent samples the scrolling plot retains.")]
    [SerializeField] protected int historyLength = 256;
    [Tooltip("Optional key to toggle this overlay on/off at runtime.")]
    [SerializeField] protected KeyCode toggleKey = KeyCode.None;

    [Header("World placement")]
    [Tooltip("World object to float this graph next to. Defaults to this transform (the sensor).")]
    [SerializeField] protected Transform worldAnchorOverride;
    [Tooltip("Float next to the world anchor (positioned by VisualizerManager). " +
             "Off keeps the fixed screen anchor below.")]
    [SerializeField] protected bool floatInWorld = true;

    [Header("Colors")]
    [SerializeField] protected Color bgColor = new Color(0f, 0f, 0f, 0.65f);
    [SerializeField] protected Color gridColor = new Color(1f, 1f, 1f, 0.18f);
    [SerializeField] protected Color traceColor = new Color(0.40f, 0.85f, 1f, 1f);

    private float[] _ring;
    private int _head;
    private int _count;
    private float _latest;
    private float _min, _max;
    private bool _hasValue;
    private bool _dirty;

    private Texture2D _tex;
    private GUIStyle _label;

    // Layout constants (shared by OnGUI and PanelSize).
    private const float Pad = 6f, HeaderH = 16f, FooterH = 16f;

    // Screen rect assigned by VisualizerManager; falls back to `anchor` when unmanaged.
    private bool _hasManagedRect;
    private Vector2 _managedTopLeft;

    // ---- subclass hooks ------------------------------------------------

    /// <summary>Resolve the output reference and add the event listener.</summary>
    protected abstract void Subscribe();

    /// <summary>Remove the event listener.</summary>
    protected abstract void Unsubscribe();

    /// <summary>Concrete visualizers feed each new reading in here.</summary>
    protected void Push(float value)
    {
        if (_ring == null) EnsureBuffers();
        _latest = value;
        _hasValue = true;
        _ring[_head] = value;
        _head = (_head + 1) % _ring.Length;
        if (_count < _ring.Length) _count++;
        _dirty = true;
    }

    // ---- lifecycle -----------------------------------------------------

    protected virtual void OnEnable()
    {
        EnsureBuffers();
        Subscribe();
        VisualizerRegistry.Register(this);
    }

    protected virtual void OnDisable()
    {
        Unsubscribe();
        VisualizerRegistry.Unregister(this);
        _hasManagedRect = false;
    }

    private void Update()
    {
        if (toggleKey != KeyCode.None && Input.GetKeyDown(toggleKey)) show = !show;
        if (_dirty) { Repaint(); _dirty = false; }
    }

    // ---- rendering -----------------------------------------------------

    private void EnsureBuffers()
    {
        int cap = Mathf.Max(2, historyLength);
        if (_ring == null || _ring.Length != cap)
        {
            _ring = new float[cap];
            _head = 0;
            _count = 0;
        }
        if (_tex == null || _tex.width != plotWidth || _tex.height != plotHeight)
        {
            _tex = new Texture2D(plotWidth, plotHeight, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            ClearTexture(_tex, bgColor);
            _tex.Apply(false, false);
        }
    }

    private void Repaint()
    {
        EnsureBuffers();
        ClearTexture(_tex, bgColor);
        DrawGrid(_tex);
        DrawTrace(_tex);
        _tex.Apply(false, false);
    }

    private void OnGUI()
    {
        if (!show) return;

        if (_label == null)
        {
            _label = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                richText = true,
                normal = { textColor = Color.white }
            };
        }

        float pad = Pad, headerH = HeaderH, footerH = FooterH;
        Vector2 origin = _hasManagedRect ? _managedTopLeft : anchor;
        float x = origin.x, y = origin.y;
        float panelW = plotWidth + pad * 2;
        float panelH = headerH + plotHeight + footerH + pad * 2;

        DrawPanel(x, y, panelW, panelH);

        GUI.Label(new Rect(x + pad, y + pad, plotWidth, headerH), $"<b>{title}</b>", _label);

        if (_tex != null)
            GUI.DrawTexture(new Rect(x + pad, y + pad + headerH, plotWidth, plotHeight), _tex);

        string footer = _hasValue
            ? $"{_latest:F3} {units}    range {_min:F3}..{_max:F3}"
            : "(waiting for data)";
        GUI.Label(new Rect(x + pad, y + pad + headerH + plotHeight, plotWidth, footerH),
                  footer, _label);
    }

    // ---- IVisualizerPanel ----------------------------------------------

    public string DisplayName => string.IsNullOrEmpty(title) ? GetType().Name : title;
    public virtual string Group => "Sensors";
    public bool Show { get => show; set => show = value; }
    public Transform WorldAnchor => worldAnchorOverride != null ? worldAnchorOverride : transform;
    public bool FloatInWorld => floatInWorld;
    public Vector2 PanelSize => new Vector2(plotWidth + Pad * 2f, HeaderH + plotHeight + FooterH + Pad * 2f);

    public void ApplyScreenRect(Vector2 topLeft)
    {
        _managedTopLeft = topLeft;
        _hasManagedRect = true;
    }

    private void DrawPanel(float x, float y, float w, float h)
    {
        var old = GUI.color;
        GUI.color = bgColor;
        GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
        GUI.color = old;
    }

    // Plot the rolling history oldest-to-newest, auto-ranged on Y.
    private void DrawTrace(Texture2D tex)
    {
        if (_count < 1) return;

        int w = tex.width, h = tex.height;
        int start = (_head - _count + _ring.Length) % _ring.Length;

        float lo = float.PositiveInfinity, hi = float.NegativeInfinity;
        for (int i = 0; i < _count; i++)
        {
            float v = _ring[(start + i) % _ring.Length];
            if (v < lo) lo = v;
            if (v > hi) hi = v;
        }
        _min = lo; _max = hi;
        float range = Mathf.Max(1e-6f, hi - lo);

        int prevY = -1;
        for (int col = 0; col < w; col++)
        {
            int idx = (_count == 1)
                ? 0
                : Mathf.Clamp((int)((col / (float)(w - 1)) * (_count - 1)), 0, _count - 1);
            float v = _ring[(start + idx) % _ring.Length];
            float t01 = (v - lo) / range;
            int yy = Mathf.Clamp(Mathf.RoundToInt(t01 * (h - 2)) + 1, 0, h - 1);

            tex.SetPixel(col, yy, traceColor);
            if (prevY >= 0 && Mathf.Abs(yy - prevY) > 1)
            {
                int a = Mathf.Min(prevY, yy), b = Mathf.Max(prevY, yy);
                for (int k = a; k <= b; k++) tex.SetPixel(col, k, traceColor);
            }
            prevY = yy;
        }
    }

    private void DrawGrid(Texture2D tex)
    {
        int w = tex.width, h = tex.height;
        for (int i = 1; i < 4; i++)
        {
            int yL = (h * i) / 4;
            for (int px = 0; px < w; px++) tex.SetPixel(px, yL, gridColor);
            int xL = (w * i) / 4;
            for (int py = 0; py < h; py++) tex.SetPixel(xL, py, gridColor);
        }
    }

    private static void ClearTexture(Texture2D tex, Color color)
    {
        var pix = tex.GetRawTextureData<Color32>();
        var c32 = (Color32)color;
        for (int i = 0; i < pix.Length; i++) pix[i] = c32;
    }
}