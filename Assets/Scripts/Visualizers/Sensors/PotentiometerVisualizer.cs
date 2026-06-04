using UnityEngine;

/// <summary>
/// World-space potentiometer twin. Drives an assigned "nub" Transform along its
/// slide from the shared PositionOutput reading. The body and nub are
/// GameObjects you place in the scene and assign here — this draws no geometry
/// itself.
///
/// Behaviour is set by ModeManager via IModeReceiver; the component never reads
/// the global mode itself:
///   Simulating: the nub tracks the reading (which the digital pot derives from
///               the virtual object). Display only — the model is not touched.
///   Twinning:   the hardware reading drives the nub AND sets the global Y of an
///               assigned model target, so the model mirrors the physical rig.
///
/// The nub / model drive runs every frame regardless of the VisualizerManager
/// toggle (it is functional, not just debug); the toggle only governs the small
/// IMGUI value readout. In Twinning the model target should be kinematic so
/// physics does not fight the hardware-set position.
/// </summary>
public class PotentiometerVisualizer : MonoBehaviour, IVisualizerPanel, IModeReceiver
{
    [Header("Source")]
    [SerializeField] private PositionOutput output;

    [Header("Nub (assigned scene object — not created here)")]
    [Tooltip("Transform moved along the slide. Only its local Y is driven; X and Z " +
             "(and the value captured at Start as the low end) are left untouched.")]
    [SerializeField] private Transform nub;
    [Tooltip("Local-Y travel the nub moves from its starting position to the high " +
             "end, in SCENE units. If the model is upscaled, scale this to match " +
             "(e.g. a 75 mm slide at 10x scale = 0.75).")]
    [SerializeField] private float slideLengthLocalY = 0.075f;

    [Header("Reading mapping")]
    [Tooltip("Reading value mapped to the LOW end of the slide (nub's start Y).")]
    [SerializeField] private float inputMin = 0f;
    [Tooltip("Reading value mapped to the HIGH end of the slide. Set this to the " +
             "actual maximum travel the PositionOutput reports — in the upscaled " +
             "model these readings are large, which is why the nub pinned at max.")]
    [SerializeField] private float inputMax = 0.075f;

    [Header("Twinning (model drive)")]
    [Tooltip("Model object whose world Y is set from the reading when Twinning. " +
             "Should be kinematic in Twinning so physics doesn't fight it.")]
    [SerializeField] private Transform modelTarget;
    [Tooltip("World Y that corresponds to a reading of zero travel.")]
    [SerializeField] private float modelRestY = 0f;

    [Header("Overlay (readout only)")]
    [SerializeField] private bool show = true;
    [SerializeField] private string title = "Potentiometer";
    [SerializeField] private Vector2 anchor = new Vector2(12, 12);
    [SerializeField] private bool floatInWorld = true;
    [SerializeField] private Transform worldAnchorOverride;

    private float _reading;
    private bool _hasValue;
    private TwinMode _mode = TwinMode.Simulating;

    private Vector3 _nubStartLocalPos;   // captured at Start; X/Z preserved, Y is the low end
    private bool _nubStartCaptured;

    private bool _hasManagedRect;
    private Vector2 _managedTopLeft;
    private GUIStyle _label;

    // ---- mode (pushed by ModeManager) ----------------------------------

    public void OnModeChanged(TwinMode mode) => _mode = mode;

    // ---- lifecycle -----------------------------------------------------

    private void OnEnable()
    {
        if (output == null) output = GetComponent<PositionOutput>();
        if (output != null) output.OnPosition.AddListener(OnPosition);
        VisualizerRegistry.Register(this);

        if (nub != null && !_nubStartCaptured)
        {
            _nubStartLocalPos = nub.localPosition;   // this Y is the slide's low end
            _nubStartCaptured = true;
        }
    }

    private void OnDisable()
    {
        if (output != null) output.OnPosition.RemoveListener(OnPosition);
        VisualizerRegistry.Unregister(this);
        _hasManagedRect = false;
    }

    private void OnPosition(float value)
    {
        _reading = value;
        _hasValue = true;
    }

    private void LateUpdate()
    {
        if (!_hasValue) return;

        float span = Mathf.Abs(inputMax - inputMin) < 1e-6f ? 1f : (inputMax - inputMin);
        float t = Mathf.Clamp01((_reading - inputMin) / span);

        // Move the nub along its slide — local Y ONLY, preserving the X/Z it was
        // placed with. The captured start Y is the low end; t scales the travel up.
        if (nub != null)
        {
            if (!_nubStartCaptured)
            {
                _nubStartLocalPos = nub.localPosition;
                _nubStartCaptured = true;
            }
            Vector3 p = nub.localPosition;
            p.y = _nubStartLocalPos.y + t * slideLengthLocalY;
            nub.localPosition = p;
        }

        // In Twinning the hardware reading drives the model's world Y.
        if (_mode == TwinMode.Twinning && modelTarget != null)
        {
            Vector3 p = modelTarget.position;
            p.y = modelRestY + _reading;
            modelTarget.position = p;
        }
    }

    // ---- IVisualizerPanel (small readout only) -------------------------

    public string DisplayName => string.IsNullOrEmpty(title) ? GetType().Name : title;
    public bool Show { get => show; set => show = value; }
    public Transform WorldAnchor =>
        worldAnchorOverride != null ? worldAnchorOverride : (nub != null ? nub : transform);
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

        GUI.Label(new Rect(box.x + 6, box.y + 4, box.width - 12, 16),
                  $"<b>{title}</b>  [{_mode}]", _label);
        string body = _hasValue ? $"{_reading:F4} m" : "(waiting for data)";
        GUI.Label(new Rect(box.x + 6, box.y + 22, box.width - 12, 16), body, _label);
    }
}