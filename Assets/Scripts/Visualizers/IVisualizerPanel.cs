using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Implemented by every on-screen debug visualizer (the sensor-output plots,
/// the bump-pipeline overlay, ...). VisualizerManager discovers panels through
/// the registry, lets the user pick which to show, and assigns each shown panel
/// a non-overlapping screen rect — floated next to its world object when it has
/// one.
/// </summary>
public interface IVisualizerPanel
{
    /// <summary>Name shown in the manager's selection list.</summary>
    string DisplayName { get; }

    /// <summary>Whether this panel currently renders. Driven by the manager.</summary>
    bool Show { get; set; }

    /// <summary>Pixel size of the panel, used for layout and overlap resolution.</summary>
    Vector2 PanelSize { get; }

    /// <summary>World object to float the panel next to, or null for a screen-fixed panel.</summary>
    Transform WorldAnchor { get; }

    /// <summary>True if the panel should track its WorldAnchor in screen space.</summary>
    bool FloatInWorld { get; }

    /// <summary>Manager assigns the final, de-overlapped top-left (GUI pixels) each frame.</summary>
    void ApplyScreenRect(Vector2 topLeft);
}

/// <summary>
/// Process-wide list of live visualizer panels. Panels add themselves in
/// OnEnable and remove themselves in OnDisable, so the manager reads this list
/// instead of scanning the scene and spawned/destroyed panels are handled for
/// free. Works with zero or many managers present.
/// </summary>
public static class VisualizerRegistry
{
    private static readonly List<IVisualizerPanel> _panels = new List<IVisualizerPanel>();

    public static IReadOnlyList<IVisualizerPanel> Panels => _panels;

    public static void Register(IVisualizerPanel panel)
    {
        if (panel != null && !_panels.Contains(panel)) _panels.Add(panel);
    }

    public static void Unregister(IVisualizerPanel panel)
    {
        _panels.Remove(panel);
    }
}
