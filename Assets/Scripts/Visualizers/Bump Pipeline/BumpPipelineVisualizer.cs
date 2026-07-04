using Unity.Collections;
using UnityEngine;

/// <summary>
/// Debug overlay for BumpPipeline. Shows three things:
///   1. State + counters (Idle/Accumulating/Cooldown, bumpsSeen, slack)
///   2. The last captured bump profile (height vs. position)
///   3. The damping cost curve across candidates, with the winner marked
///
/// Pure OnGUI / Texture2D — no scene setup needed. Drop on the same
/// GameObject as BumpPipeline (or anywhere) and assign the pipeline ref.
///
/// The textures are repainted only when a bump finishes / a solve completes,
/// not per frame. OnGUI just blits.
/// </summary>
[DisallowMultipleComponent]
public class BumpPipelineVisualizer : MonoBehaviour, IVisualizerPanel
{
    [Header("Source")]
    [SerializeField] private BumpPipeline pipeline;

    [Header("Overlay")]
    [SerializeField] private bool show = true;
    [SerializeField] private Vector2 anchor = new Vector2(12, 12);
    [SerializeField] private int plotWidth = 280;
    [SerializeField] private int plotHeight = 110;
    [SerializeField] private KeyCode toggleKey = KeyCode.F2;

    [Header("World placement")]
    [Tooltip("World object to float this overlay next to. Defaults to this transform (the rig).")]
    [SerializeField] private Transform worldAnchorOverride;
    [Tooltip("Float next to the world anchor (positioned by VisualizerManager). " +
             "Off keeps the fixed screen anchor.")]
    [SerializeField] private bool floatInWorld = true;

    [Header("Colors")]
    [SerializeField] private Color bgColor = new Color(0f, 0f, 0f, 0.65f);
    [SerializeField] private Color gridColor = new Color(1f, 1f, 1f, 0.18f);
    [SerializeField] private Color bumpColor = new Color(0.40f, 0.85f, 1f, 1f);
    [SerializeField] private Color costColor = new Color(1f, 0.75f, 0.30f, 1f);
    [SerializeField] private Color markerColor = new Color(0.40f, 1f, 0.50f, 1f);

    [Header("Bump plot smoothing (display only)")]
    [Tooltip("Median-filter window applied to the displayed bump profile to drop " +
             "isolated spikes. <= 1 disables. The solver always uses the raw profile.")]
    [SerializeField] private int bumpDespikeWindow = 3;
    [Tooltip("Moving-average window applied to the displayed bump profile. <= 1 disables.")]
    [SerializeField] private int bumpSmoothWindow = 3;

    [Header("Crop to current bump")]
    [Tooltip("Show only the active part: samples whose deviation from the flat " +
             "baseline exceeds this fraction of the peak are kept; flat lead-in/out " +
             "is cropped away. Deviation is measured in BOTH directions, so raised " +
             "bumps and potholes are both isolated.")]
    [SerializeField] private float activeFraction = 0.1f;
    [Tooltip("Flat margin (samples) kept on each side of the cropped bump.")]
    [SerializeField] private int cropPadding = 4;

    // Cached textures, repainted on events only.
    private Texture2D _bumpTex;
    private Texture2D _costTex;
    private GUIStyle _label;

    // Layout constants (shared by OnGUI and PanelSize).
    private const float Pad = 6f, HeaderH = 18f, FooterH = 36f, Gap = 8f;

    // Screen rect assigned by VisualizerManager; falls back to `anchor` when unmanaged.
    private bool _hasManagedRect;
    private Vector2 _managedTopLeft;

    // Last-known stats (mirrors of pipeline state, captured at events).
    private bool _hasBump;
    private bool _hasCost;
    private float _bumpMinH, _bumpMaxH, _bumpLengthM;
    private int _bumpSamples;
    private float _bestC, _bestPeak, _bestRms;
    private float _cMinSeen, _cMaxSeen, _peakMin, _peakMax;
    private int _candidateCount;
    private float _solveMs, _slackMs;

    private void OnEnable()
    {
        if (pipeline == null) pipeline = GetComponent<BumpPipeline>();
        if (pipeline != null)
        {
            pipeline.OnBumpCaptured.AddListener(OnBumpCaptured);
            pipeline.OnSolveCompleted.AddListener(OnSolveCompleted);
        }
        EnsureTextures();
        VisualizerRegistry.Register(this);
    }

    private void OnDisable()
    {
        if (pipeline != null)
        {
            pipeline.OnBumpCaptured.RemoveListener(OnBumpCaptured);
            pipeline.OnSolveCompleted.RemoveListener(OnSolveCompleted);
        }
        VisualizerRegistry.Unregister(this);
        _hasManagedRect = false;
    }

    private void Update()
    {
        if (Input.GetKeyDown(toggleKey)) show = !show;
    }

    private void EnsureTextures()
    {
        if (_bumpTex == null || _bumpTex.width != plotWidth || _bumpTex.height != plotHeight)
        {
            _bumpTex = new Texture2D(plotWidth, plotHeight, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            _costTex = new Texture2D(plotWidth, plotHeight, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            ClearTexture(_bumpTex);
            ClearTexture(_costTex);
        }
    }

    private void OnBumpCaptured(BumpPipeline.BumpSnapshot snap)
    {
        EnsureTextures();

        int n = snap.Count;
        if (n < 1) return;

        float[] full = new float[n];
        float maxAbs = 0f;
        for (int i = 0; i < n; i++)
        {
            float h = snap.Heights[i];
            full[i] = h;
            float a = Mathf.Abs(h);
            if (a > maxAbs) maxAbs = a;
        }

        float active = Mathf.Max(1e-4f, maxAbs * activeFraction);
        int first = 0, last = n - 1;
        while (first < n && Mathf.Abs(full[first]) < active) first++;
        while (last >= 0 && Mathf.Abs(full[last]) < active) last--;
        if (first > last) { first = 0; last = n - 1; }   // never crossed: show all
        first = Mathf.Max(0, first - cropPadding);
        last = Mathf.Min(n - 1, last + cropPadding);

        int m = last - first + 1;
        float[] crop = new float[m];
        float rawLo = float.PositiveInfinity, rawHi = float.NegativeInfinity;
        for (int i = 0; i < m; i++)
        {
            float h = full[first + i];
            crop[i] = h;
            if (h < rawLo) rawLo = h;
            if (h > rawHi) rawHi = h;
        }

        float lo = Mathf.Min(0f, rawLo);
        float hi = Mathf.Max(0f, rawHi);
        float pad = (hi - lo) * 0.08f + 1e-5f;
        lo -= pad; hi += pad;

        _bumpSamples = m;
        _bumpLengthM = snap.LengthMeters;
        _bumpMinH = rawLo;
        _bumpMaxH = rawHi;

        ClearTexture(_bumpTex, bgColor);
        DrawGrid(_bumpTex);
        DrawBaseline(_bumpTex, lo, hi);
        PlotSeries(_bumpTex, crop, m, lo, hi, bumpColor, bumpDespikeWindow, bumpSmoothWindow);
        _bumpTex.Apply(false, false);
        _hasBump = true;
    }

    private void OnSolveCompleted(BumpPipeline.SolveSnapshot snap)
    {
        EnsureTextures();

        _bestC = snap.BestC;
        _bestPeak = snap.BestPeak;
        _bestRms = snap.BestRms;
        _solveMs = snap.SolveMs;
        _slackMs = snap.SlackMs;
        _candidateCount = snap.Count;
        _cMinSeen = snap.CMin;
        _cMaxSeen = snap.CMax;
        _peakMin = snap.PeakMin;
        _peakMax = snap.PeakMax;
        if (_peakMax - _peakMin < 1e-6f) _peakMax = _peakMin + 1e-6f;

        ClearTexture(_costTex, bgColor);
        DrawGrid(_costTex);
        DrawSeries(_costTex, snap.Peaks, snap.Count, _peakMin, _peakMax, costColor, 0, 0);

        // Marker: vertical line at the best candidate's column.
        int bestCol = Mathf.RoundToInt((snap.BestIndex / (float)(snap.Count - 1)) * (plotWidth - 1));
        for (int y = 0; y < plotHeight; y++) _costTex.SetPixel(bestCol, y, markerColor);

        _costTex.Apply(false, false);
        _hasCost = true;
    }

    private void OnGUI()
    {
        if (!show || pipeline == null) return;

        if (_label == null)
        {
            _label = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                richText = true,
                normal = { textColor = Color.white }
            };
        }

        Vector2 origin = _hasManagedRect ? _managedTopLeft : anchor;
        float x = origin.x, y = origin.y;
        float pad = Pad, headerH = HeaderH, footerH = FooterH, gap = Gap;

        // State banner
        var stateColor = pipeline.CurrentState switch
        {
            BumpPipeline.State.Idle => "#9aa",
            BumpPipeline.State.Accumulating => "#5cf",
            BumpPipeline.State.Cooldown => "#fc6",
            _ => "#fff"
        };
        string banner =
            $"<b>BumpPipeline</b>   state=<color={stateColor}>{pipeline.CurrentState}</color>" +
            $"   bumps={pipeline.BumpsSeen}   solve={_solveMs:F1}ms   slack={_slackMs:+0;-0}ms";
        DrawPanel(x, y, plotWidth + pad * 2, headerH + pad * 2);
        GUI.Label(new Rect(x + pad, y + pad, plotWidth, headerH), banner, _label);
        y += headerH + pad * 2 + gap;

        // Bump plot
        DrawPanel(x, y, plotWidth + pad * 2, plotHeight + footerH + pad * 2);
        GUI.DrawTexture(new Rect(x + pad, y + pad, plotWidth, plotHeight), _bumpTex);
        string bumpFooter = _hasBump
            ? $"bump: {_bumpSamples} samples, {_bumpLengthM * 1000f:F0} mm long, " +
              $"height {_bumpMinH * 1000f:F1}..{_bumpMaxH * 1000f:F1} mm"
            : "bump: (waiting for first bump)";
        GUI.Label(new Rect(x + pad, y + pad + plotHeight + 2, plotWidth, footerH),
                  bumpFooter, _label);
        y += plotHeight + footerH + pad * 2 + gap;

        // Cost plot
        DrawPanel(x, y, plotWidth + pad * 2, plotHeight + footerH + pad * 2);
        GUI.DrawTexture(new Rect(x + pad, y + pad, plotWidth, plotHeight), _costTex);
        string costFooter = _hasCost
            ? $"damping sweep: {_candidateCount} candidates, c={_cMinSeen:F0}..{_cMaxSeen:F0}\n" +
              $"best c = <color=#7f7>{_bestC:F2}</color>   peak={_bestPeak / 9.81f:F2} g   rms={_bestRms / 9.81f:F2} g"
            : "damping sweep: (waiting for first solve)";
        GUI.Label(new Rect(x + pad, y + pad + plotHeight + 2, plotWidth, footerH),
                  costFooter, _label);
    }

    public string DisplayName => "Bump pipeline";
    public string Group => "Control";
    public bool Show { get => show; set => show = value; }
    public Transform WorldAnchor => worldAnchorOverride != null ? worldAnchorOverride : transform;
    public bool FloatInWorld => floatInWorld;

    public Vector2 PanelSize => new Vector2(
        plotWidth + Pad * 2f,
        (HeaderH + Pad * 2f) + Gap
        + (plotHeight + FooterH + Pad * 2f) + Gap
        + (plotHeight + FooterH + Pad * 2f));

    public void ApplyScreenRect(Vector2 topLeft)
    {
        _managedTopLeft = topLeft;
        _hasManagedRect = true;
    }

    private void DrawPanel(float x, float y, float w, float h)
    {
        var tex = Texture2D.whiteTexture;
        var old = GUI.color;
        GUI.color = bgColor;
        GUI.DrawTexture(new Rect(x, y, w, h), tex);
        GUI.color = old;
    }

    private static void ClearTexture(Texture2D tex, Color? color = null)
    {
        var c = color ?? new Color(0, 0, 0, 0);
        var pix = tex.GetRawTextureData<Color32>();
        var c32 = (Color32)c;
        for (int i = 0; i < pix.Length; i++) pix[i] = c32;
        tex.Apply(false, false);
    }

    private void DrawGrid(Texture2D tex)
    {
        int w = tex.width, h = tex.height;
        // 4 horizontal lines, 4 vertical
        for (int i = 1; i < 4; i++)
        {
            int yL = (h * i) / 4;
            for (int x = 0; x < w; x++) tex.SetPixel(x, yL, gridColor);
            int xL = (w * i) / 4;
            for (int yy = 0; yy < h; yy++) tex.SetPixel(xL, yy, gridColor);
        }
    }

    private void DrawBaseline(Texture2D tex, float vMin, float vMax)
    {
        if (0f < vMin || 0f > vMax) return;
        int h = tex.height, w = tex.width;
        float range = Mathf.Max(1e-6f, vMax - vMin);
        int zeroY = Mathf.Clamp(Mathf.RoundToInt(((0f - vMin) / range) * (h - 2)) + 1, 0, h - 1);
        Color c = new Color(1f, 1f, 1f, 0.4f);
        for (int x = 0; x < w; x++) tex.SetPixel(x, zeroY, c);
    }

    // Cost curve etc. arrive as a NativeArray; copy then plot.
    private void DrawSeries(Texture2D tex, NativeArray<float> data, int count,
                            float vMin, float vMax, Color col,
                            int despikeWindow, int smoothWindow)
    {
        if (count < 1) return;
        float[] s = new float[count];
        for (int i = 0; i < count; i++) s[i] = data[i];
        PlotSeries(tex, s, count, vMin, vMax, col, despikeWindow, smoothWindow);
    }

    private void PlotSeries(Texture2D tex, float[] src, int count,
                            float vMin, float vMax, Color col,
                            int despikeWindow, int smoothWindow)
    {
        if (count < 1) return;
        int w = tex.width, h = tex.height;

        float[] s = src;
        if (despikeWindow >= 3) s = MedianFilter(s, despikeWindow);
        if (smoothWindow >= 2) s = MovingAverage(s, smoothWindow);

        float[] cols = Resample(s, w);

        float range = Mathf.Max(1e-6f, vMax - vMin);
        int prevY = -1;
        for (int x = 0; x < w; x++)
        {
            float t01 = (cols[x] - vMin) / range;
            int yy = Mathf.Clamp(Mathf.RoundToInt(t01 * (h - 2)) + 1, 0, h - 1);

            tex.SetPixel(x, yy, col);
            if (prevY >= 0 && Mathf.Abs(yy - prevY) > 1)
            {
                int a = Mathf.Min(prevY, yy), b = Mathf.Max(prevY, yy);
                for (int k = a; k <= b; k++) tex.SetPixel(x, k, col);
            }
            prevY = yy;
        }
    }

    // Centred median filter — removes isolated spikes while preserving edges.
    private static float[] MedianFilter(float[] src, int win)
    {
        int n = src.Length;
        if (win < 3 || n < 3) return src;
        int half = win / 2;
        float[] outp = new float[n];
        float[] window = new float[win];
        for (int i = 0; i < n; i++)
        {
            int cnt = 0;
            for (int k = -half; k <= half; k++)
                window[cnt++] = src[Mathf.Clamp(i + k, 0, n - 1)];
            System.Array.Sort(window, 0, cnt);
            outp[i] = window[cnt / 2];
        }
        return outp;
    }

    // Centred moving average.
    private static float[] MovingAverage(float[] src, int win)
    {
        int n = src.Length;
        if (win < 2 || n < 2) return src;
        int half = win / 2;
        float[] outp = new float[n];
        for (int i = 0; i < n; i++)
        {
            float sum = 0f; int cnt = 0;
            for (int k = -half; k <= half; k++)
            {
                int idx = i + k;
                if (idx < 0 || idx >= n) continue;
                sum += src[idx]; cnt++;
            }
            outp[i] = sum / cnt;
        }
        return outp;
    }

    // Resample to outLen: box-average when shrinking, linear interp when growing.
    private static float[] Resample(float[] src, int outLen)
    {
        int n = src.Length;
        float[] outp = new float[outLen];
        if (n == 1) { for (int i = 0; i < outLen; i++) outp[i] = src[0]; return outp; }

        for (int x = 0; x < outLen; x++)
        {
            float fStart = (x / (float)outLen) * n;
            float fEnd = ((x + 1) / (float)outLen) * n;

            if (fEnd - fStart >= 1f)
            {
                int a = Mathf.Clamp((int)fStart, 0, n - 1);
                int b = Mathf.Clamp((int)Mathf.Ceil(fEnd) - 1, a, n - 1);
                float sum = 0f; int cnt = 0;
                for (int i = a; i <= b; i++) { sum += src[i]; cnt++; }
                outp[x] = sum / cnt;
            }
            else
            {
                float fc = ((x + 0.5f) / outLen) * (n - 1);
                int i0 = Mathf.Clamp((int)fc, 0, n - 1);
                int i1 = Mathf.Min(i0 + 1, n - 1);
                outp[x] = Mathf.Lerp(src[i0], src[i1], fc - i0);
            }
        }
        return outp;
    }
}