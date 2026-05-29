using Unity.Collections;
using UnityEngine;
using Suspension.Solver;

namespace Suspension.Sensors
{
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
    public class BumpPipelineVisualizer : MonoBehaviour
    {
        [Header("Source")]
        [SerializeField] private BumpPipeline pipeline;

        [Header("Overlay")]
        [SerializeField] private bool   show      = true;
        [SerializeField] private Vector2 anchor   = new Vector2(12, 12);
        [SerializeField] private int    plotWidth  = 280;
        [SerializeField] private int    plotHeight = 110;
        [SerializeField] private KeyCode toggleKey = KeyCode.F2;

        [Header("Colors")]
        [SerializeField] private Color bgColor      = new Color(0f, 0f, 0f, 0.65f);
        [SerializeField] private Color gridColor   = new Color(1f, 1f, 1f, 0.18f);
        [SerializeField] private Color bumpColor   = new Color(0.40f, 0.85f, 1f, 1f);
        [SerializeField] private Color costColor   = new Color(1f, 0.75f, 0.30f, 1f);
        [SerializeField] private Color markerColor = new Color(0.40f, 1f, 0.50f, 1f);

        // Cached textures, repainted on events only.
        private Texture2D _bumpTex;
        private Texture2D _costTex;
        private GUIStyle  _label;

        // Last-known stats (mirrors of pipeline state, captured at events).
        private bool   _hasBump;
        private bool   _hasCost;
        private float  _bumpMinH, _bumpMaxH, _bumpLengthM;
        private int    _bumpSamples;
        private float  _bestC, _bestPeak, _bestRms;
        private float  _cMinSeen, _cMaxSeen, _peakMin, _peakMax;
        private int    _candidateCount;
        private float  _solveMs, _slackMs;

        private void OnEnable()
        {
            if (pipeline == null) pipeline = GetComponent<BumpPipeline>();
            if (pipeline != null)
            {
                pipeline.OnBumpCaptured.AddListener(OnBumpCaptured);
                pipeline.OnSolveCompleted.AddListener(OnSolveCompleted);
            }
            EnsureTextures();
        }

        private void OnDisable()
        {
            if (pipeline != null)
            {
                pipeline.OnBumpCaptured.RemoveListener(OnBumpCaptured);
                pipeline.OnSolveCompleted.RemoveListener(OnSolveCompleted);
            }
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

        // --------- event handlers (filled in from BumpPipeline) ---------

        // Called when a bump's accumulation ends. We get a frozen snapshot of
        // the captured (pos, height) series.
        private void OnBumpCaptured(BumpPipeline.BumpSnapshot snap)
        {
            EnsureTextures();

            _bumpSamples = snap.Count;
            _bumpLengthM = snap.LengthMeters;
            _bumpMinH    = float.PositiveInfinity;
            _bumpMaxH    = float.NegativeInfinity;
            for (int i = 0; i < snap.Count; i++)
            {
                float h = snap.Heights[i];
                if (h < _bumpMinH) _bumpMinH = h;
                if (h > _bumpMaxH) _bumpMaxH = h;
            }
            if (_bumpMaxH - _bumpMinH < 1e-4f) { _bumpMaxH = _bumpMinH + 1e-4f; }

            ClearTexture(_bumpTex, bgColor);
            DrawGrid(_bumpTex);
            DrawSeries(_bumpTex, snap.Heights, snap.Count, _bumpMinH, _bumpMaxH, bumpColor);
            _bumpTex.Apply(false, false);
            _hasBump = true;
        }

        // Called when the damping search finishes. We get the full cost array.
        private void OnSolveCompleted(BumpPipeline.SolveSnapshot snap)
        {
            EnsureTextures();

            _bestC          = snap.BestC;
            _bestPeak       = snap.BestPeak;
            _bestRms        = snap.BestRms;
            _solveMs        = snap.SolveMs;
            _slackMs        = snap.SlackMs;
            _candidateCount = snap.Count;
            _cMinSeen       = snap.CMin;
            _cMaxSeen       = snap.CMax;
            _peakMin        = snap.PeakMin;
            _peakMax        = snap.PeakMax;
            if (_peakMax - _peakMin < 1e-6f) _peakMax = _peakMin + 1e-6f;

            ClearTexture(_costTex, bgColor);
            DrawGrid(_costTex);
            DrawSeries(_costTex, snap.Peaks, snap.Count, _peakMin, _peakMax, costColor);

            // Marker: vertical line at the best candidate's column.
            int bestCol = Mathf.RoundToInt((snap.BestIndex / (float)(snap.Count - 1)) * (plotWidth - 1));
            for (int y = 0; y < plotHeight; y++) _costTex.SetPixel(bestCol, y, markerColor);

            _costTex.Apply(false, false);
            _hasCost = true;
        }

        // ----------------- OnGUI -----------------

        private void OnGUI()
        {
            if (!show || pipeline == null) return;

            if (_label == null)
            {
                _label = new GUIStyle(GUI.skin.label)
                {
                    fontSize  = 11,
                    richText  = true,
                    normal    = { textColor = Color.white }
                };
            }

            float x = anchor.x, y = anchor.y;
            const float pad = 6f, headerH = 18f, footerH = 36f, gap = 8f;

            // State banner
            var stateColor = pipeline.CurrentState switch
            {
                BumpPipeline.State.Idle         => "#9aa",
                BumpPipeline.State.Accumulating => "#5cf",
                BumpPipeline.State.Cooldown     => "#fc6",
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
                  $"best c = <color=#7f7>{_bestC:F2}</color>   peak={_bestPeak:F2} m/s²   rms={_bestRms:F2} m/s²"
                : "damping sweep: (waiting for first solve)";
            GUI.Label(new Rect(x + pad, y + pad + plotHeight + 2, plotWidth, footerH),
                      costFooter, _label);
        }

        // ----------------- drawing helpers -----------------

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

        // Plot a 1-D series stretched horizontally to fill the texture.
        private void DrawSeries(Texture2D tex, NativeArray<float> data, int count,
                                float vMin, float vMax, Color col)
        {
            int w = tex.width, h = tex.height;
            int prevY = -1;
            float range = Mathf.Max(1e-6f, vMax - vMin);
            for (int x = 0; x < w; x++)
            {
                int idx = (int)((x / (float)(w - 1)) * (count - 1));
                if (idx < 0) idx = 0;
                if (idx >= count) idx = count - 1;
                float v   = data[idx];
                float t01 = (v - vMin) / range;
                int   yy  = Mathf.Clamp(Mathf.RoundToInt(t01 * (h - 2)) + 1, 0, h - 1);

                tex.SetPixel(x, yy, col);
                // connect with previous column
                if (prevY >= 0 && Mathf.Abs(yy - prevY) > 1)
                {
                    int a = Mathf.Min(prevY, yy), b = Mathf.Max(prevY, yy);
                    for (int k = a; k <= b; k++) tex.SetPixel(x, k, col);
                }
                prevY = yy;
            }
        }
    }
}
