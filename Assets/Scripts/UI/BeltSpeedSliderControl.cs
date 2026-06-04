using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives a SpeedCommand from a UI Slider — the producer the belt actuator
/// chain was missing. Map the slider's 0..1 (or custom min/max) range to a belt
/// speed in m/s and publish it on the shared channel; whichever belt actuator
/// ModeManager left enabled (Digital* -> model, Real* -> packet) consumes it.
///
/// Wiring: put this on the slider GameObject (or any object), assign the Slider
/// and the SpeedCommand. It hooks the slider's onValueChanged, so it publishes
/// only when the value moves — plus once on enable so the belt starts at the
/// slider's current position rather than stale.
/// </summary>
public class BeltSpeedSliderControl : MonoBehaviour
{
    [Header("Wiring")]
    [Tooltip("Slider whose value drives the belt speed. Defaults to a Slider on this object.")]
    [SerializeField] private Slider slider;
    [Tooltip("Speed command channel to publish to (the belt actuators consume this).")]
    [SerializeField] private SpeedCommand speedCommand;

    [Header("Mapping (slider value -> m/s)")]
    [Tooltip("Belt speed when the slider is at its minimum.")]
    [SerializeField] private float speedAtSliderMin = 0f;
    [Tooltip("Belt speed when the slider is at its maximum.")]
    [SerializeField] private float speedAtSliderMax = 2f;

    [Header("Diagnostics (read-only)")]
    [SerializeField] private float lastPublishedSpeed;

    private void OnEnable()
    {
        if (slider == null) slider = GetComponent<Slider>();
        if (slider != null) slider.onValueChanged.AddListener(OnSliderChanged);

        // Publish the current position immediately so the belt isn't left on a
        // stale command from before this control existed.
        if (slider != null) OnSliderChanged(slider.value);
    }

    private void OnDisable()
    {
        if (slider != null) slider.onValueChanged.RemoveListener(OnSliderChanged);
    }

    private void OnSliderChanged(float raw)
    {
        if (slider == null || speedCommand == null) return;

        // Normalize the slider into 0..1 using its own configured range, then map
        // to the speed range. Works whether the slider is 0..1 or something else.
        float lo = slider.minValue, hi = slider.maxValue;
        float t = Mathf.Approximately(hi, lo) ? 0f : Mathf.InverseLerp(lo, hi, raw);
        float speed = Mathf.Lerp(speedAtSliderMin, speedAtSliderMax, t);

        lastPublishedSpeed = speed;
        speedCommand.Publish(speed);
    }

    /// <summary>Publish a speed directly (e.g. from a button or another script).</summary>
    public void PublishSpeed(float metersPerSecond)
    {
        if (speedCommand == null) return;
        lastPublishedSpeed = metersPerSecond;
        speedCommand.Publish(metersPerSecond);
    }
}
