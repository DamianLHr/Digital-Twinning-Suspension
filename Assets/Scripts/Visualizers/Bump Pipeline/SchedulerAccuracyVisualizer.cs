using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Verifies the DampingCommandScheduler end-to-end: shows WHICH damping C was
/// assigned to each bump and WHETHER the command lands where the bump actually
/// reaches the wheel. Two coupled views:
///
///   • World tags — a label per scheduled bump that rides the terrainWheel (parented to
///     it, so it co-rotates with the physical bump), coloured by state
///     (scheduled → applied). Reads "c=NNN".
///   • Timeline table — recent bumps with their observed / target / applied /
///     jolt belt positions, the residual (jolt − target) in mm and ms, and the
///     belt speed at solve vs at apply (so a speed-induced mismatch is visible).
///
/// Registers as an IVisualizerPanel so the VisualizerManager can toggle it.
/// Drives no model state — pure diagnostics.
/// </summary>
[DisallowMultipleComponent]
public class SchedulerAccuracyVisualizer : MonoBehaviour, IVisualizerPanel
{
    [Header("Wiring")]
    [SerializeField] private DampingCommandScheduler scheduler;
    [Tooltip("The terrainWheel the tags ride on (so they co-rotate with the bumps).")]
    [UnityEngine.Serialization.FormerlySerializedAs("drum")]
    [SerializeField] private TerrainWheel terrainWheel;
    [Tooltip("Where a bump sits when the ToF observes it; tags spawn here.")]
    [SerializeField] private Transform tofEmitter;
    [SerializeField] private Camera viewCamera;

    [Header("World tags")]
    [SerializeField] private bool showTags = true;
    [SerializeField] private int maxTags = 12;
    [Tooltip("A new bump whose observed PHASE (belt-travel mod one revolution) is within this many " +
             "metres of an existing tag is the SAME physical bump coming round again — update that " +
             "tag instead of spawning a duplicate. Scale-independent (belt-travel metres).")]
    [SerializeField] private float tagMergeDistance = 0.02f;
    [SerializeField] private Color tagScheduled = new Color(1f, 0.85f, 0.3f, 1f);
    [SerializeField] private Color tagApplied   = new Color(0.4f, 1f, 0.5f, 1f);

    [Header("Panel")]
    [SerializeField] private bool show = true;
    [SerializeField] private string title = "Scheduler accuracy";
    [SerializeField] private Vector2 anchor = new Vector2(12, 320);
    [SerializeField] private bool floatInWorld = false;
    [SerializeField] private Transform worldAnchorOverride;
    [Tooltip("How many recent bumps the table shows.")]
    [SerializeField] private int tableRows = 8;

    [Header("Jolt matching")]
    [Tooltip("A jolt confirms a command only if it lands within this fraction of the wheel offset " +
             "from the command's target. Keep the window NARROWER than the gap between bumps " +
             "(circumference ÷ bump count) or it can grab a neighbouring bump's jolt. 0.1 of a " +
             "330 mm offset = 33 mm window.")]
    [Range(0.02f, 0.9f)]
    [SerializeField] private float joltMatchFraction = 0.1f;
    [Tooltip("Fallback match tolerance (m) used when the wheel offset isn't known yet.")]
    [SerializeField] private float fallbackMatchTol = 0.05f;

    private class Rec
    {
        public float Observed, Target, C, SpeedSolve;
        public bool  Applied;  public float AppliedPos, SpeedApply;
        public bool  Jolted;   public float JoltPos;
    }
    private readonly List<Rec> _recs = new List<Rec>();
    private const int MaxRecs = 32;

    private class Tag { public GameObject Go; public Rec Rec; }
    private readonly Queue<Tag> _tags = new Queue<Tag>();

    private GUIStyle _label, _tag, _num;
    private bool _hasManagedRect;
    private Vector2 _managedTopLeft;

    private const float Pad = 6f, HeaderH = 18f, LineH = 16f;

    private void OnEnable()
    {
        if (viewCamera == null) viewCamera = Camera.main;
        if (scheduler != null)
        {
            scheduler.OnCommandScheduled.AddListener(OnScheduled);
            scheduler.OnCommandApplied.AddListener(OnApplied);
            scheduler.OnJolt.AddListener(OnJolt);
        }
        VisualizerRegistry.Register(this);
    }

    private void OnDisable()
    {
        if (scheduler != null)
        {
            scheduler.OnCommandScheduled.RemoveListener(OnScheduled);
            scheduler.OnCommandApplied.RemoveListener(OnApplied);
            scheduler.OnJolt.RemoveListener(OnJolt);
        }
        VisualizerRegistry.Unregister(this);
        _hasManagedRect = false;
        ClearTags();
    }

    private void OnScheduled(DampingCommandScheduler.ScheduledInfo s)
    {
        var r = new Rec { Observed = s.ObservedPos, Target = s.TargetPos, C = s.C, SpeedSolve = s.SpeedAtSolve };
        _recs.Add(r);
        if (_recs.Count > MaxRecs) _recs.RemoveRange(0, _recs.Count - MaxRecs);
        SpawnTag(r);
    }

    private void OnApplied(DampingCommandScheduler.AppliedInfo a)
    {
        Rec r = NearestByTarget(a.TargetPos, requireUnapplied: true);
        if (r == null) return;
        r.Applied = true;
        r.AppliedPos = a.AppliedPos;
        r.SpeedApply = a.SpeedAtApply;
    }

    private void OnJolt(float joltPos)
    {
        float offset = scheduler != null ? scheduler.WheelOffset : 0f;
        float tol = offset > 1e-4f ? joltMatchFraction * offset : fallbackMatchTol;

        Rec best = null; float bestD = float.MaxValue;
        for (int i = 0; i < _recs.Count; i++)
        {
            if (_recs[i].Jolted) continue;
            float d = Mathf.Abs(_recs[i].Target - joltPos);
            if (d <= tol && d < bestD) { bestD = d; best = _recs[i]; }
        }
        if (best != null) { best.Jolted = true; best.JoltPos = joltPos; }
        // else: jolt from an untracked / other bump — ignored.
    }

    private Rec NearestByTarget(float target, bool requireUnapplied)
    {
        Rec best = null; float bestD = float.MaxValue;
        for (int i = 0; i < _recs.Count; i++)
        {
            if (requireUnapplied && _recs[i].Applied) continue;
            float d = Mathf.Abs(_recs[i].Target - target);
            if (d < bestD) { bestD = d; best = _recs[i]; }
        }
        return best;
    }

    private void SpawnTag(Rec r)
    {
        if (!showTags || terrainWheel == null || tofEmitter == null) return;

        float circ = Mathf.PI * terrainWheel.Diameter;
        float phase = circ > 1e-4f ? Mathf.Repeat(r.Observed, circ) : r.Observed;
        foreach (var t in _tags)
        {
            if (t.Go == null) continue;
            float tp = circ > 1e-4f ? Mathf.Repeat(t.Rec.Observed, circ) : t.Rec.Observed;
            float d = Mathf.Abs(phase - tp);
            if (circ > 1e-4f) d = Mathf.Min(d, circ - d);   // wrap at the revolution seam
            if (d <= tagMergeDistance) { t.Rec = r; return; }
        }

        Vector3 spawnPos = tofEmitter.position;
        if (circ > 1e-4f)
        {
            float dAngle = (terrainWheel.TraveledDistance - r.Observed) / circ * 360f;
            Vector3 axis = terrainWheel.transform.TransformDirection(Vector3.back);   // drum spins about local -Z
            Vector3 center = terrainWheel.transform.position;
            spawnPos = Quaternion.AngleAxis(dAngle, axis) * (tofEmitter.position - center) + center;
        }

        var go = new GameObject("C-Tag");
        go.transform.position = spawnPos;
        go.transform.SetParent(terrainWheel.transform, true);   // ride the terrainWheel so it tracks the bump
        _tags.Enqueue(new Tag { Go = go, Rec = r });

        while (_tags.Count > Mathf.Max(1, maxTags))
        {
            var old = _tags.Dequeue();
            if (old.Go != null) Destroy(old.Go);
        }
    }

    private void ClearTags()
    {
        while (_tags.Count > 0)
        {
            var t = _tags.Dequeue();
            if (t.Go != null) Destroy(t.Go);
        }
    }

    public string DisplayName => string.IsNullOrEmpty(title) ? GetType().Name : title;
    public string Group => "Control";
    public bool Show { get => show; set => show = value; }
    public Transform WorldAnchor => worldAnchorOverride != null ? worldAnchorOverride : transform;
    public bool FloatInWorld => floatInWorld;
    public Vector2 PanelSize => new Vector2(
        300f, Pad * 2f + HeaderH + LineH * (Mathf.Max(1, tableRows) + 1));

    public void ApplyScreenRect(Vector2 topLeft) { _managedTopLeft = topLeft; _hasManagedRect = true; }

    private void OnGUI()
    {
        if (!show) return;
        EnsureStyles();

        if (showTags && viewCamera != null) DrawTags();
        DrawTable();
    }

    private void DrawTags()
    {
        foreach (var t in _tags)
        {
            if (t.Go == null) continue;
            Vector3 sp = viewCamera.WorldToScreenPoint(t.Go.transform.position);
            if (sp.z <= 0f) continue;   // behind camera
            var gp = new Vector2(sp.x - 28f, Screen.height - sp.y - 8f);
            _tag.normal.textColor = t.Rec.Applied ? tagApplied : tagScheduled;
            GUI.Label(new Rect(gp.x - 6f, gp.y, 88f, 16f), $"c={t.Rec.C:F3}", _tag);
        }
    }

    // Column colours (shared with the world tags where it makes sense).
    private static readonly Color ColSched = new Color(1f, 0.85f, 0.30f);
    private static readonly Color ColLive  = new Color(0.40f, 1f, 0.50f);
    private static readonly Color ColHit   = new Color(0.50f, 0.80f, 1f);
    private static readonly Color ColGood  = new Color(0.40f, 1f, 0.50f);
    private static readonly Color ColWarn  = new Color(1f, 0.85f, 0.30f);
    private static readonly Color ColBad   = new Color(1f, 0.45f, 0.40f);

    private void DrawTable()
    {
        Vector2 origin = _hasManagedRect ? _managedTopLeft : anchor;
        Vector2 sz = PanelSize;
        var box = new Rect(origin.x, origin.y, sz.x, sz.y);

        var old = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.78f);
        GUI.DrawTexture(box, Texture2D.whiteTexture);
        GUI.color = old;

        float x = box.x + Pad, y = box.y + Pad, w = box.width - Pad * 2f;
        float offsetMm = scheduler != null ? scheduler.WheelOffset * 1000f : 0f;
        string state = scheduler != null ? scheduler.State.ToString() : "-";

        GUI.Label(new Rect(x, y, w, HeaderH),
                  $"<b>{title}</b>   <color=#9cf>{state}</color>   offset {offsetMm:F0} mm", _label);
        y += HeaderH;

        // Column x-positions (fixed → real alignment regardless of font).
        const float wStatus = 46f, wC = 56f, wApply = 78f, wErr = 76f;
        float cStatus = x;
        float cC      = x + 48f;
        float cApply  = x + 116f;
        float cErr    = x + 200f;

        DrawCell(cStatus, y, wStatus, "<b>state</b>", _label, Color.white);
        DrawCell(cC,      y, wC,      "<b>C</b>",        _num,   Color.white);
        DrawCell(cApply,  y, wApply,  "<b>apply in</b>", _label, Color.white);
        DrawCell(cErr,    y, wErr,    "<b>error</b>",    _label, Color.white);
        y += LineH;

        if (_recs.Count == 0)
        {
            GUI.Label(new Rect(x, y, w, LineH), "<i>(waiting for scheduled commands…)</i>", _label);
            return;
        }

        float now   = terrainWheel != null ? terrainWheel.TraveledDistance : 0f;
        float speed = terrainWheel != null ? terrainWheel.LinearSpeed : 0f;

        int shown = Mathf.Min(tableRows, _recs.Count);
        for (int i = 0; i < shown; i++)
        {
            Rec r = _recs[_recs.Count - 1 - i];   // newest first

            // state
            string st; Color stc;
            if (r.Jolted)       { st = "HIT";   stc = ColHit; }
            else if (r.Applied) { st = "LIVE";  stc = ColLive; }
            else                { st = "SCHED"; stc = ColSched; }
            DrawCell(cStatus, y, wStatus, st, _label, stc);

            // C
            DrawCell(cC, y, wC, $"{r.C:F3}", _num, Color.white);

            // apply in — time until the coefficient is applied at the wheel
            string apply;
            if (r.Applied) apply = "applied";
            else
            {
                float dist = r.Target - now;
                if (speed > 1e-4f) { float t = dist / speed; apply = t > 0f ? $"{t:0.00} s" : "now"; }
                else               apply = dist > 0f ? "—" : "now";
            }
            DrawCell(cApply, y, wApply, apply, _label, Color.white);

            // error — how far the bump's actual jolt landed from the target
            if (r.Jolted)
            {
                float residMm = (r.JoltPos - r.Target) * 1000f;
                float frac = Mathf.Abs(residMm) / Mathf.Max(1f, offsetMm);
                Color ec = frac < 0.05f ? ColGood : frac < 0.15f ? ColWarn : ColBad;
                DrawCell(cErr, y, wErr, $"{residMm:+0;-0} mm", _label, ec);
            }
            else DrawCell(cErr, y, wErr, "—", _label, Color.white);

            y += LineH;
        }
    }

    private void DrawCell(float x, float y, float w, string text, GUIStyle style, Color color)
    {
        Color prev = style.normal.textColor;
        style.normal.textColor = color;
        GUI.Label(new Rect(x, y, w, LineH), text, style);
        style.normal.textColor = prev;
    }

    private void EnsureStyles()
    {
        if (_label == null)
            _label = new GUIStyle(GUI.skin.label)
            { fontSize = 11, richText = true, normal = { textColor = Color.white } };
        if (_tag == null)
            _tag = new GUIStyle(GUI.skin.label)
            { fontSize = 11, richText = true, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        if (_num == null)
            _num = new GUIStyle(GUI.skin.label)
            { fontSize = 11, richText = true, alignment = TextAnchor.MiddleRight, normal = { textColor = Color.white } };
    }
}
