using UnityEngine;

/// <summary>
/// Scrolling plot of a ToFSensorOutput's distance readings, plus a world-space
/// beam drawn from the emitter to the surface it hits. The beam is owned by this
/// visualizer (not the sensor) and is shown only while this visualizer is shown,
/// so toggling it in the VisualizerManager menu toggles the beam too.
/// </summary>
public class ToFSensorOutputVisualizer : SensorOutputVisualizerBase
{
    [Header("Source")]
    [SerializeField] private ToFSensorOutput output;

    [Header("Height conversion")]
    [Tooltip("Pipeline to read the nominal stand-off from, so the plotted height " +
             "matches what the solver sees. If set, its NominalStandoff overrides " +
             "the fallback below (single source of truth — no silent drift).")]
    [SerializeField] private BumpPipeline pipeline;
    [Tooltip("Fallback flat-surface stand-off (m), used only if no pipeline is " +
             "assigned. Height = standoff - distance, so a raised bump (nearer " +
             "surface) reads positive / upward.")]
    [SerializeField] private float fallbackStandoff = 0.15f;

    [Header("Beam (world space)")]
    [Tooltip("Draw the ToF beam as a line from the emitter to the measured point.")]
    [SerializeField] private bool drawBeam = true;
    [Tooltip("Emitter transform. The beam fires along its -up axis (matching " +
             "DigitalToFSensor). Defaults to this transform if left empty.")]
    [SerializeField] private Transform beamOrigin;
    [Tooltip("Max beam length / raycast range (m). Also the drawn length when the beam hits nothing.")]
    [SerializeField] private float beamMaxLength = 2f;
    [Tooltip("Layers the beam can hit. Set to the belt / road layer (match DigitalToFSensor's mask).")]
    [SerializeField] private LayerMask beamMask = ~0;
    [SerializeField] private float beamWidth = 0.01f;
    [SerializeField] private Color beamColor = new Color(0.40f, 0.85f, 1f, 1f);
    [SerializeField] private Color beamNoHitColor = new Color(1f, 0.40f, 0.30f, 0.9f);

    private LineRenderer _beam;

    private void Reset()
    {
        title = "ToF bump height";
        units = "m";
        traceColor = new Color(0.40f, 0.85f, 1f, 1f);
    }

    protected override void Subscribe()
    {
        if (output == null) output = GetComponent<ToFSensorOutput>();
        if (output != null) output.OnDistance.AddListener(OnDistance);
    }

    protected override void Unsubscribe()
    {
        if (output != null) output.OnDistance.RemoveListener(OnDistance);
        if (_beam != null) _beam.enabled = false;
    }

    private void OnDistance(float distance)
    {
        float standoff = pipeline != null ? pipeline.NominalStandoff : fallbackStandoff;
        float height = distance < 0f ? 0f : standoff - distance;
        Push(height);
    }

    private void LateUpdate()
    {
        if (!drawBeam || !Show)
        {
            if (_beam != null) _beam.enabled = false;
            return;
        }

        EnsureBeam();

        Transform o = beamOrigin != null ? beamOrigin : transform;
        Vector3 start = o.position;
        Vector3 dir = -o.up;                  // matches DigitalToFSensor's aim

        bool hit = Physics.Raycast(start, dir, out RaycastHit info, beamMaxLength,
                                   beamMask, QueryTriggerInteraction.Ignore);
        Vector3 end = hit ? info.point : start + dir * beamMaxLength;

        Color c = hit ? beamColor : beamNoHitColor;
        _beam.enabled = true;
        _beam.startWidth = beamWidth;
        _beam.endWidth = beamWidth;
        _beam.startColor = c;
        _beam.endColor = c;
        _beam.SetPosition(0, start);
        _beam.SetPosition(1, end);
    }

    private void EnsureBeam()
    {
        if (_beam != null) return;

        var go = new GameObject("ToF Beam");
        go.transform.SetParent(transform, false);
        _beam = go.AddComponent<LineRenderer>();
        _beam.useWorldSpace = true;
        _beam.positionCount = 2;
        _beam.numCapVertices = 2;
        _beam.alignment = LineAlignment.View;
        _beam.textureMode = LineTextureMode.Stretch;
        _beam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _beam.receiveShadows = false;
        // Sprites/Default is an always-included built-in shader and honours the
        // LineRenderer's vertex colors. If the beam is invisible in a build, add
        // it under Project Settings > Graphics > Always Included Shaders.
        _beam.material = new Material(Shader.Find("Sprites/Default"));
    }

    private void OnDestroy()
    {
        if (_beam != null && _beam.material != null) Destroy(_beam.material);
    }
}