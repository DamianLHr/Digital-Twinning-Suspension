using UnityEngine;

/// <summary>
/// Visual-only: stretches/compresses a spring mesh so it always spans between two
/// scene points (e.g. the base and the sprung mass). It orients the spring's long
/// axis from A toward B and scales ONLY that axis by the A–B distance, leaving the
/// coil radius (the other two local axes) untouched. Pure cosmetic — drives no
/// physics, reads no commands.
///
/// Setup: put this on the spring GameObject, assign Point A (fixed/base end) and
/// Point B (moving end), pick which LOCAL axis is the spring's length, and set
/// Rest Length — the spring's world length at its authored scale (leave 0 to
/// auto-measure from the mesh bounds, parent scale included).
///
/// Note: if the spring sits under a non-uniformly scaled or rotating parent, set
/// Rest Length by hand (the auto-measure assumes ~uniform parent scale).
/// </summary>
[DisallowMultipleComponent]
public class SpringStretch : MonoBehaviour
{
    public enum Axis { X, Y, Z }

    [Header("Endpoints")]
    [Tooltip("The spring spans between these. A = fixed/base end, B = moving end.")]
    [SerializeField] private Transform pointA;
    [SerializeField] private Transform pointB;

    [Header("Spring geometry")]
    [Tooltip("Which LOCAL axis of the model is the spring's length (the coil direction).")]
    [SerializeField] private Axis lengthAxis = Axis.Y;
    [Tooltip("World length of the spring at its authored scale — the distance that maps to no " +
             "extra stretch. Leave 0 to auto-measure from the mesh bounds.")]
    [SerializeField] private float restLength = 0f;
    [Tooltip("Model pivot is at the spring's centre (placed at the A–B midpoint). " +
             "Off = pivot at the A end (placed at A, extends toward B).")]
    [SerializeField] private bool pivotAtCentre = true;

    [Header("Limits")]
    [Tooltip("Clamp the length-scale factor so the spring never collapses or explodes.")]
    [SerializeField] private float minScaleFactor = 0.1f;
    [SerializeField] private float maxScaleFactor = 4f;

    private Vector3 _baseScale;
    private bool _captured;

    private void Awake() => Capture();

    private void Capture()
    {
        _baseScale = transform.localScale;
        if (restLength <= 0f) restLength = MeasureRestLength();
        _captured = true;
    }

    private void LateUpdate() => Fit();

    [ContextMenu("Fit now")]
    private void Fit()
    {
        if (!_captured) Capture();
        if (pointA == null || pointB == null) return;

        Vector3 a = pointA.position, b = pointB.position;
        Vector3 delta = b - a;
        float dist = delta.magnitude;
        if (dist < 1e-5f) return;
        Vector3 dir = delta / dist;

        // Orient: map the chosen local axis onto A→B (the free spin around the axis
        // doesn't matter for a radially-symmetric spring).
        transform.rotation = Quaternion.FromToRotation(AxisVector(lengthAxis), dir);

        // Position: centre on the midpoint, or sit at the A end.
        transform.position = pivotAtCentre ? (a + b) * 0.5f : a;

        // Scale ONLY the length axis; keep the coil radius (the other two axes).
        float factor = Mathf.Clamp(dist / Mathf.Max(1e-4f, restLength), minScaleFactor, maxScaleFactor);
        Vector3 s = _baseScale;
        int i = (int)lengthAxis;
        s[i] = _baseScale[i] * factor;
        transform.localScale = s;
    }

    private static Vector3 AxisVector(Axis axis) => axis switch
    {
        Axis.X => Vector3.right,
        Axis.Y => Vector3.up,
        _      => Vector3.forward,
    };

    // World length of the spring at its authored scale, along the chosen axis.
    private float MeasureRestLength()
    {
        var mf = GetComponentInChildren<MeshFilter>();
        if (mf != null && mf.sharedMesh != null)
        {
            int i = (int)lengthAxis;
            return mf.sharedMesh.bounds.size[i] * Mathf.Abs(transform.lossyScale[i]);
        }
        return 1f;   // safe fallback if no mesh found
    }
}
