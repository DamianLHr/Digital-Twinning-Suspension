using UnityEngine;

/// <summary>
/// The driven road belt / drum. NOT a rigid body — it is kinematic. Real
/// bump geometry on the wheel (child colliders / mesh) is what the ToF
/// sensor sees by raycasting; this class only keeps track of how far the
/// belt has scrolled, which the predictive solver pairs with each ToF
/// reading to build a bump profile in world space.
/// </summary>
public class TerrainWheel : MonoBehaviour
{
    [Header("Belt")]
    [SerializeField] private float linearSpeed = 1.0f;       // surface speed (m/s)
    [SerializeField] private float diameter = 0.20f;

    private float _traveled;   // distance the belt surface has moved (m)

    public float LinearSpeed => linearSpeed;
    public float Diameter => diameter;

    /// <summary>Cumulative belt travel since startup (m). Use this to stamp ToF samples.</summary>
    public float TraveledDistance => _traveled;

    private void FixedUpdate()
    {
        _traveled += linearSpeed * Time.fixedDeltaTime;

        // Optional visual: rotate the drum so child bump geometry passes the
        // contact point. Comment out if your rig drives rotation elsewhere.
        if (diameter > 0f)
        {
            float angularDeg = (linearSpeed / (Mathf.PI * diameter)) * 360f * Time.fixedDeltaTime;
            transform.Rotate(Vector3.back, angularDeg, Space.Self);
        }
    }

    public void SetLinearSpeed(float v) => linearSpeed = v;
}
