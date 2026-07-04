using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Global visualization controller. Lists every IVisualizerPanel in the scene
/// in an on-screen menu so you can toggle which ones are visible, then lays the
/// shown panels out so they float next to their world objects without
/// overlapping one another (a leader line ties each floating panel back to its
/// object). Pure IMGUI — drop it on one GameObject; no Canvas or other setup.
///
/// Panels register themselves via VisualizerRegistry, so this picks up the
/// sensor-output visualizers and the bump-pipeline overlay automatically.
/// </summary>
[DisallowMultipleComponent]
public class VisualizerManager : MonoBehaviour
{
    [Header("Selection menu")]
    [Tooltip("Panels discovered for the first time start visible.")]
    [SerializeField] private bool showNewPanelsByDefault = false;
    [Tooltip("Key that shows/hides the selection menu itself.")]
    [SerializeField] private KeyCode menuKey = KeyCode.F1;
    [SerializeField] private bool menuOpen = true;
    [SerializeField] private Vector2 menuAnchor = new Vector2(12, 12);
    [SerializeField] private float menuWidth = 210f;

    [Header("World placement")]
    [Tooltip("Camera used to project world anchors to screen. Defaults to Camera.main.")]
    [SerializeField] private Camera viewCamera;
    [Tooltip("Pixel offset from a projected anchor point to its panel. Larger x " +
             "pushes panels further to the side of the device.")]
    [SerializeField] private Vector2 anchorOffset = new Vector2(72f, 0f);
    [Tooltip("Alternate successive floating panels to the left / right of their anchor " +
             "instead of stacking them all on one side of the model.")]
    [SerializeField] private bool alternateSides = true;
    [Tooltip("Empty gap kept between panels during overlap resolution (px).")]
    [SerializeField] private float panelMargin = 8f;
    [Tooltip("Overlap-resolution passes per frame. More = tighter packing.")]
    [SerializeField] private int separationIterations = 16;
    [Tooltip("A floating panel only re-slots when its world anchor drifts this many screen " +
             "pixels. Smaller vertical motion (a bobbing mass) then leaves the panel STILL " +
             "while only the leader line tracks the object. Larger = steadier panels.")]
    [SerializeField] private float placementDeadzone = 48f;

    [Header("Leader lines")]
    [SerializeField] private bool drawLeaderLines = true;
    [SerializeField] private Color leaderColor = new Color(1f, 1f, 1f, 0.5f);

    // Per-panel desired visibility; the menu edits this and it survives panels
    // appearing / disappearing.
    private readonly Dictionary<IVisualizerPanel, bool> _selected =
        new Dictionary<IVisualizerPanel, bool>();

    // Stabilized placement anchors (screen px).
    private readonly Dictionary<IVisualizerPanel, Vector2> _placeAnchor =
        new Dictionary<IVisualizerPanel, Vector2>();

    // This frame's resolved layout, consumed in OnGUI to draw leader lines.
    private struct Placed { public Rect Rect; public Vector2 Anchor; public bool HasAnchor; }
    private readonly List<Placed> _placed = new List<Placed>();

    private GUIStyle _label;
    private GUIStyle _toggle;
    private static Texture2D _lineTex;

    // ---- lifecycle -----------------------------------------------------

    private void OnEnable()
    {
        if (viewCamera == null) viewCamera = Camera.main;
    }

    private void Update()
    {
        if (menuKey != KeyCode.None && Input.GetKeyDown(menuKey)) menuOpen = !menuOpen;
    }

    // Layout runs once per frame, after every panel's Update, so the positions
    // are fresh by the time OnGUI draws.
    private void LateUpdate()
    {
        if (viewCamera == null) viewCamera = Camera.main;
        Layout();
    }

    // ---- layout --------------------------------------------------------

    private void Layout()
    {
        _placed.Clear();
        var panels = VisualizerRegistry.Panels;

        var live = new List<IVisualizerPanel>();
        var rects = new List<Rect>();
        var anchors = new List<Vector2>();
        var anchored = new List<bool>();

        // Fixed-panel stack (anchorless or non-floating panels) starts to the
        // right of the menu.
        float fixedX = menuAnchor.x + menuWidth + 24f;
        float fixedY = menuAnchor.y;
        int anchoredCount = 0;

        for (int i = 0; i < panels.Count; i++)
        {
            IVisualizerPanel p = panels[i];

            if (!_selected.TryGetValue(p, out bool want))
            {
                want = showNewPanelsByDefault;
                _selected[p] = want;
            }

            p.Show = want;
            if (!want) continue;

            Vector2 size = p.PanelSize;
            Vector2 topLeft;
            Vector2 anchorPt = Vector2.zero;
            bool isAnchored = false;

            if (p.FloatInWorld && p.WorldAnchor != null && viewCamera != null)
            {
                Vector3 sp = viewCamera.WorldToScreenPoint(p.WorldAnchor.position);
                if (sp.z <= 0f) { p.Show = false; continue; }   // behind camera

                anchorPt = new Vector2(sp.x, Screen.height - sp.y);   // LIVE point (leader line)

                // Stabilized placement anchor: only follows the live point when it
                // drifts past the deadzone, so a bobbing object doesn't shake the panel.
                if (!_placeAnchor.TryGetValue(p, out Vector2 placeAt) ||
                    Vector2.Distance(placeAt, anchorPt) > placementDeadzone)
                {
                    placeAt = anchorPt;
                    _placeAnchor[p] = placeAt;
                }

                // Alternate sides so panels don't all pile up on one side of the
                // model: even ones go right of the anchor, odd ones go left.
                bool right = !alternateSides || (anchoredCount % 2 == 1);
                float x = right ? placeAt.x + anchorOffset.x
                                : placeAt.x - anchorOffset.x - size.x;
                topLeft = new Vector2(x, placeAt.y - size.y * 0.5f + anchorOffset.y);
                anchoredCount++;
                isAnchored = true;
            }
            else
            {
                topLeft = new Vector2(fixedX, fixedY);
                fixedY += size.y + panelMargin;
            }

            live.Add(p);
            rects.Add(new Rect(topLeft.x, topLeft.y, size.x, size.y));
            anchors.Add(anchorPt);
            anchored.Add(isAnchored);
        }

        Separate(rects);

        for (int i = 0; i < live.Count; i++)
        {
            Rect r = rects[i];
            r.x = Mathf.Clamp(r.x, 0f, Mathf.Max(0f, Screen.width - r.width));
            r.y = Mathf.Clamp(r.y, 0f, Mathf.Max(0f, Screen.height - r.height));
            live[i].ApplyScreenRect(new Vector2(r.x, r.y));
            _placed.Add(new Placed { Rect = r, Anchor = anchors[i], HasAnchor = anchored[i] });
        }
    }

    // Iterative pairwise push-apart. Resolves along the axis of least
    // penetration so panels slide the shortest distance to clear each other.
    private void Separate(List<Rect> rects)
    {
        float m = panelMargin;
        for (int iter = 0; iter < separationIterations; iter++)
        {
            bool moved = false;
            for (int i = 0; i < rects.Count; i++)
            {
                for (int j = i + 1; j < rects.Count; j++)
                {
                    Rect a = rects[i], b = rects[j];
                    float overlapX = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin) + m;
                    float overlapY = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin) + m;
                    if (overlapX <= 0f || overlapY <= 0f) continue;

                    if (overlapX < overlapY)
                    {
                        float push = overlapX * 0.5f;
                        float s = (a.center.x <= b.center.x) ? -1f : 1f;
                        a.x += s * push; b.x -= s * push;
                    }
                    else
                    {
                        float push = overlapY * 0.5f;
                        float s = (a.center.y <= b.center.y) ? -1f : 1f;
                        a.y += s * push; b.y -= s * push;
                    }
                    rects[i] = a; rects[j] = b;
                    moved = true;
                }
            }
            if (!moved) break;
        }
    }

    // ---- IMGUI ---------------------------------------------------------

    private void OnGUI()
    {
        EnsureStyles();

        if (drawLeaderLines && Event.current.type == EventType.Repaint)
        {
            for (int i = 0; i < _placed.Count; i++)
            {
                if (!_placed[i].HasAnchor) continue;
                Vector2 from = _placed[i].Anchor;
                Rect r = _placed[i].Rect;
                float edgeX = (from.x <= r.center.x) ? r.xMin : r.xMax;   // side facing the model
                Vector2 to = new Vector2(edgeX, r.y + r.height * 0.5f);
                DrawLine(from, to, leaderColor, 1.5f);
            }
        }

        if (menuOpen) DrawMenu();
    }

    // Preferred group order in the menu; any other groups are appended alphabetically.
    private static readonly string[] GroupOrder = { "Sensors", "Control", "Actuators" };
    private readonly List<string> _groupNames = new List<string>();
    private readonly Dictionary<string, List<IVisualizerPanel>> _groups =
        new Dictionary<string, List<IVisualizerPanel>>();

    // Bucket the live panels by their Group, ordered (preferred first, rest alphabetical).
    // Lists are reused frame-to-frame to avoid per-frame allocation.
    private void RebuildGroups(IReadOnlyList<IVisualizerPanel> panels)
    {
        _groupNames.Clear();
        foreach (var kv in _groups) kv.Value.Clear();

        for (int i = 0; i < panels.Count; i++)
        {
            IVisualizerPanel p = panels[i];
            string g = string.IsNullOrEmpty(p.Group) ? "Other" : p.Group;
            if (!_groups.TryGetValue(g, out var list))
            {
                list = new List<IVisualizerPanel>();
                _groups[g] = list;
            }
            list.Add(p);
        }

        for (int i = 0; i < GroupOrder.Length; i++)
            if (_groups.TryGetValue(GroupOrder[i], out var l) && l.Count > 0) _groupNames.Add(GroupOrder[i]);

        var rest = new List<string>();
        foreach (var kv in _groups)
            if (kv.Value.Count > 0 && System.Array.IndexOf(GroupOrder, kv.Key) < 0) rest.Add(kv.Key);
        rest.Sort();
        _groupNames.AddRange(rest);
    }

    private bool GroupAllShown(List<IVisualizerPanel> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            _selected.TryGetValue(list[i], out bool s);
            if (!s) return false;
        }
        return list.Count > 0;
    }

    private void SetGroupShown(List<IVisualizerPanel> list, bool on)
    {
        for (int i = 0; i < list.Count; i++)
        {
            _selected[list[i]] = on;
            list[i].Show = on;
        }
    }

    private void DrawMenu()
    {
        var panels = VisualizerRegistry.Panels;
        const float pad = 8f, rowH = 20f, indent = 16f;
        RebuildGroups(panels);

        // rows: title + master + Σ(group header + its panels); or title + "(none)".
        int rows = panels.Count == 0 ? 2 : 2 + _groupNames.Count + panels.Count;
        float h = pad * 2f + rowH * rows;
        Rect box = new Rect(menuAnchor.x, menuAnchor.y, menuWidth, h);

        Color old = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.7f);
        GUI.DrawTexture(box, Texture2D.whiteTexture);
        GUI.color = old;

        float x = menuAnchor.x + pad;
        float w = menuWidth - pad * 2f;
        float y = menuAnchor.y + pad;

        GUI.Label(new Rect(x, y, w, rowH), $"<b>Visualizers</b>  ({menuKey})", _label);
        y += rowH;

        if (panels.Count == 0)
        {
            GUI.Label(new Rect(x, y, w, rowH), "(none in scene)", _label);
            return;
        }

        // Master show/hide-all toggle: checked only when every panel is shown.
        bool allShown = true;
        for (int i = 0; i < panels.Count; i++)
        {
            _selected.TryGetValue(panels[i], out bool s);
            if (!s) { allShown = false; break; }
        }
        bool newAll = GUI.Toggle(new Rect(x, y, w, rowH),
                                 allShown, allShown ? "  <b>Hide all</b>" : "  <b>Show all</b>", _toggle);
        if (newAll != allShown)
            for (int i = 0; i < panels.Count; i++) { _selected[panels[i]] = newAll; panels[i].Show = newAll; }
        y += rowH;

        // Per group: a bold group toggle (whole group on/off), then its panels indented.
        for (int gi = 0; gi < _groupNames.Count; gi++)
        {
            List<IVisualizerPanel> list = _groups[_groupNames[gi]];

            bool groupShown = GroupAllShown(list);
            bool newGroup = GUI.Toggle(new Rect(x, y, w, rowH),
                                       groupShown, "  <b>" + _groupNames[gi] + "</b>", _toggle);
            if (newGroup != groupShown) SetGroupShown(list, newGroup);
            y += rowH;

            for (int i = 0; i < list.Count; i++)
            {
                IVisualizerPanel p = list[i];
                _selected.TryGetValue(p, out bool want);
                bool now = GUI.Toggle(new Rect(x + indent, y, w - indent, rowH),
                                      want, "  " + p.DisplayName, _toggle);
                if (now != want) _selected[p] = now;
                y += rowH;
            }
        }
    }

    private void EnsureStyles()
    {
        if (_label == null)
            _label = new GUIStyle(GUI.skin.label)
            { fontSize = 11, richText = true, normal = { textColor = Color.white } };

        if (_toggle == null)
            _toggle = new GUIStyle(GUI.skin.toggle)
            {
                fontSize = 11,
                richText = true,
                normal = { textColor = Color.white },
                onNormal = { textColor = Color.white },
                hover = { textColor = Color.white },
                onHover = { textColor = Color.white }
            };
    }

    // Minimal GUI line via a rotated 1px texture (IMGUI has no line primitive).
    private static void DrawLine(Vector2 a, Vector2 b, Color color, float width)
    {
        if (_lineTex == null)
        {
            _lineTex = new Texture2D(1, 1);
            _lineTex.SetPixel(0, 0, Color.white);
            _lineTex.Apply();
        }

        Matrix4x4 savedMatrix = GUI.matrix;
        Color savedColor = GUI.color;

        float angle = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;
        float length = (b - a).magnitude;

        GUIUtility.RotateAroundPivot(angle, a);
        GUI.color = color;
        GUI.DrawTexture(new Rect(a.x, a.y - width * 0.5f, length, width), _lineTex);

        GUI.matrix = savedMatrix;
        GUI.color = savedColor;
    }
}