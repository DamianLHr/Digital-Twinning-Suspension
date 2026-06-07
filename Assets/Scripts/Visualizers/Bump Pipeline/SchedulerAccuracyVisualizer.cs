using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Verifies the DampingCommandScheduler end-to-end: shows WHICH damping C was
/// assigned to each bump and WHETHER the command lands where the bump actually
/// reaches the wheel. Two coupled views:
///
///   • World tags — a label per scheduled bump that rides the drum (parented to
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
    [Tooltip("The drum the tags ride on (so they co-rotate with the bumps).")]
    [SerializeField] private TerrainWheel drum;
    [Tooltip("Where a bump sits when the ToF observes it; tags spawn here.")]
    [SerializeField] private Transform tofEmitter;
    [SerializeField] private Camera viewCamera;

    [Header("World tags")]
    [SerializeField] private bool showTags = true;
    [SerializeField] private int maxTags = 12;
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

    // ---- per-bump record ----
    private class Rec
    {
        public float Observed, Target, C, SpeedSolve;
        public bool  Applied;  public float AppliedPos, SpeedApply;
        public bool  Jolted;   public float JoltPos;
    }
    private readonly List<Rec> _recs = new List<Rec>();
    private const int MaxRecs = 32;

    // ---- world tags (parented to the drum) ----
    private class Tag { public GameObject Go; public Rec Rec; }
    private readonly Queue<Tag> _tags = new Queue<Tag>();

    private GUIStyle _label, _tag;
    private bool _hasManagedRect;
    private Vector2 _managedTopLeft;

    // ---- layout constants ----
    private const float Pad = 6f, HeaderH = 18f, LineH = 15f, FooterH = 16f;

    // ---- lifecycle ----

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

    // ---- event handlers ----

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
        // Attribute the jolt to the scheduled bump whose target is closest to it.
        Rec best = null; float bestD = float.MaxValue;
        for (int i = 0; i < _recs.Count; i++)
        {
            if (_recs[i].Jolted) continue;
            float d = Mathf.Abs(_recs[i].Target - joltPos);
            if (d < bestD) { bestD = d; best = _recs[i]; }
        }
        if (best != null) { best.Jolted = true; best.JoltPos = joltPos; }
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

    // ---- world tags ----

    private void SpawnTag(Rec r)
    {
        if (!showTags || drum == null || tofEmitter == null) return;

        var go = new GameObject("C-Tag");
        go.transform.position = tofEmitter.position;
        go.transform.SetParent(drum.transform, true);   // ride the drum so it tracks the bump
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

    // ---- IVisualizerPanel ----

    public string DisplayName => string.IsNullOrEmpty(title) ? GetType().Name : title;
    public bool Show { get => show; set => show = value; }
    public Transform WorldAnchor => worldAnchorOverride != null ? worldAnchorOverride : transform;
    public bool FloatInWorld => floatInWorld;
    public Vector2 PanelSize => new Vector2(
        420f, HeaderH + FooterH + Pad * 3f + LineH * (Mathf.Max(1, tableRows) + 1));

    public void ApplyScreenRect(Vector2 topLeft) { _managedTopLeft = topLeft; _hasManagedRect = true; }

    // ---- rendering ----

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
            GUI.Label(new Rect(gp.x, gp.y, 70f, 16f), $"c={t.Rec.C:F0}", _tag);
        }
    }

    private void DrawTable()
    {
        Vector2 origin = _hasManagedRect ? _managedTopLeft : anchor;
        Vector2 sz = PanelSize;
        var box = new Rect(origin.x, origin.y, sz.x, sz.y);

        var old = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.7f);
        GUI.DrawTexture(box, Texture2D.whiteTexture);
        GUI.color = old;

        float x = box.x + Pad, y = box.y + Pad, w = box.width - Pad * 2f;
        string offset = scheduler != null ? $"{scheduler.WheelOffset * 1000f:F0} mm" : "-";
        string state  = scheduler != null ? scheduler.State.ToString() : "-";
        GUI.Label(new Rect(x, y, w, HeaderH),
                  $"<b>{title}</b>   offset={offset}   state={state}", _label);
        y += HeaderH + Pad;

        GUI.Label(new Rect(x, y, w, LineH),
                  "<b>  c        obs      tgt    appliedΔ   joltΔ(resid)   v_solve→v_apply</b>", _label);
        y += LineH;

        int shown = Mathf.Min(tableRows, _recs.Count);
        for (int i = 0; i < shown; i++)
        {
            Rec r = _recs[_recs.Count - 1 - i];   // newest first
            string appliedD = r.Applied ? $"{(r.AppliedPos - r.Target) * 1000f:+0.0;-0.0}" : "—";
            string residMm, residMs;
            if (r.Jolted)
            {
                float resid = (r.JoltPos - r.Target) * 1000f;        // mm
                float v = Mathf.Max(0.001f, r.Applied ? r.SpeedApply : r.SpeedSolve);
                residMm = $"{resid:+0.0;-0.0}";
                residMs = $"{(r.JoltPos - r.Target) / v * 1000f:+0;-0}ms";
            }
            else { residMm = "—"; residMs = ""; }

            string speeds = (r.Applied)
                ? $"{r.SpeedSolve:0.00}→{r.SpeedApply:0.00}"
                : $"{r.SpeedSolve:0.00}→…";

            GUI.Label(new Rect(x, y, w, LineH),
                $"  {r.C,5:F0}   {r.Observed * 1000f,6:F0}  {r.Target * 1000f,6:F0}    " +
                $"{appliedD,6}     {residMm,6} {residMs,-5}   {speeds}", _label);
            y += LineH;
        }

        if (_recs.Count == 0)
            GUI.Label(new Rect(x, y, w, LineH), "  (waiting for scheduled commands…)", _label);
    }

    private void EnsureStyles()
    {
        if (_label == null)
            _label = new GUIStyle(GUI.skin.label)
            { fontSize = 11, richText = true, normal = { textColor = Color.white } };
        if (_tag == null)
            _tag = new GUIStyle(GUI.skin.label)
            { fontSize = 11, richText = true, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
    }
}
