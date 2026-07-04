using UnityEngine;

/// <summary>
/// Base for actuator debug panels. Draws no world geometry (you place the
/// actuator GameObject yourself); it shows a small IMGUI readout of the latest
/// commanded value and registers with VisualizerManager so it can be toggled and
/// floated next to the actuator. Subclasses subscribe to their command channel
/// and feed values via Push, and may optionally animate an assigned indicator
/// transform by overriding OnValue.
///
/// As with the sensor visualizers, any optional indicator animation is driven by
/// the subclass and is independent of the panel toggle (the toggle only governs
/// the IMGUI readout).
/// </summary>
public abstract class ActuatorVisualizerBase : MonoBehaviour, IVisualizerPanel
{
    [Header("Overlay")]
    [SerializeField] protected bool show = true;
    [SerializeField] protected string title = "Actuator";
    [SerializeField] protected string units = "";
    [SerializeField] protected Vector2 anchor = new Vector2(12, 12);
    [SerializeField] protected bool floatInWorld = true;
    [SerializeField] protected Transform worldAnchorOverride;

    protected float _value;
    protected bool _hasValue;

    private bool _hasManagedRect;
    private Vector2 _managedTopLeft;
    private GUIStyle _label;

    /// <summary>Resolve the command reference and add the listener.</summary>
    protected abstract void Subscribe();

    /// <summary>Remove the listener.</summary>
    protected abstract void Unsubscribe();

    /// <summary>Concrete visualizers feed each new commanded value in here.</summary>
    protected virtual void Push(float value)
    {
        _value = value;
        _hasValue = true;
        OnValue(value);
    }

    /// <summary>Optional hook for subclasses to animate an indicator transform.</summary>
    protected virtual void OnValue(float value) { }

    protected virtual void OnEnable()
    {
        Subscribe();
        VisualizerRegistry.Register(this);
    }

    protected virtual void OnDisable()
    {
        Unsubscribe();
        VisualizerRegistry.Unregister(this);
        _hasManagedRect = false;
    }

    public string DisplayName => string.IsNullOrEmpty(title) ? GetType().Name : title;
    public virtual string Group => "Actuators";
    public bool Show { get => show; set => show = value; }
    public Transform WorldAnchor => worldAnchorOverride != null ? worldAnchorOverride : transform;
    public bool FloatInWorld => floatInWorld;
    public Vector2 PanelSize => new Vector2(180f, 44f);

    public void ApplyScreenRect(Vector2 topLeft)
    {
        _managedTopLeft = topLeft;
        _hasManagedRect = true;
    }

    private void OnGUI()
    {
        if (!show) return;
        if (_label == null)
            _label = new GUIStyle(GUI.skin.label)
            { fontSize = 11, richText = true, normal = { textColor = Color.white } };

        Vector2 origin = _hasManagedRect ? _managedTopLeft : anchor;
        var box = new Rect(origin.x, origin.y, PanelSize.x, PanelSize.y);

        var old = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.65f);
        GUI.DrawTexture(box, Texture2D.whiteTexture);
        GUI.color = old;

        GUI.Label(new Rect(box.x + 6, box.y + 4, box.width - 12, 16), $"<b>{title}</b>", _label);
        string body = _hasValue ? $"{_value:F3} {units}" : "(no command yet)";
        GUI.Label(new Rect(box.x + 6, box.y + 22, box.width - 12, 16), body, _label);
    }
}
