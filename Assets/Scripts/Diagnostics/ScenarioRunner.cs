using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drives a DIAG run. Two modes, both exposed for the UI to call:
///   • FREE RUN (default, always available) — sets the policy + belt speed and runs
///     forever, collecting NOTHING.
///   • COLLECT — sets the policy + belt speed, waits a warm-up (so prime/calibration
///     settle), records for a fixed time, then stops and flushes the CSVs.
///
/// For experiment A, do two COLLECT runs with the same speed/duration: policy =
/// Constant, then policy = Predictive. Python pairs them by metadata.
/// </summary>
[DisallowMultipleComponent]
public class ScenarioRunner : MonoBehaviour
{
    [Header("Wiring")]
    [SerializeField] private DampingPolicySelector policySelector;
    [SerializeField] private ConstantDampingPolicy constantPolicy;
    [SerializeField] private RunDataRecorder recorder;
    [SerializeField] private TerrainWheelSpeedCommand speedCommand;

    [Header("Scenario settings")]
    [SerializeField] private DampingPolicySelector.Policy policy = DampingPolicySelector.Policy.Predictive;
    [SerializeField] private float constantC = 3.0f;
    [SerializeField] private float beltSpeed = 0.15f;
    [SerializeField] private float warmupSeconds = 4f;
    [SerializeField] private float collectSeconds = 30f;
    [SerializeField] private string runName = "run";
    [Tooltip("Free text recorded in the CSV metadata, e.g. 'Simulating' or 'Twinning'.")]
    [SerializeField] private string modeTag = "Simulating";

    [Header("Diagnostics (read-only)")]
    [SerializeField] private bool running;

    public bool Running => running;

    private Coroutine _co;

    /// <summary>Run forever with the chosen policy/speed, collecting nothing.</summary>
    [ContextMenu("Free run (no collection)")]
    public void StartFreeRun()
    {
        StopAll();
        ApplyPolicyAndSpeed();
        // runs indefinitely; recorder stays idle
    }

    /// <summary>Configure, warm up, record for collectSeconds, then stop and flush.</summary>
    [ContextMenu("Start collect run")]
    public void StartCollectRun()
    {
        StopAll();
        _co = StartCoroutine(CollectRoutine());
    }

    [ContextMenu("Stop")]
    public void Stop() => StopAll();

    private IEnumerator CollectRoutine()
    {
        running = true;
        ApplyPolicyAndSpeed();

        yield return new WaitForSeconds(warmupSeconds);   // let prime/calibration settle

        var meta = new Dictionary<string, string>
        {
            { "mode", modeTag },
            { "policy", policy.ToString() },
            { "constantC", constantC.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            { "beltSpeed", beltSpeed.ToString(System.Globalization.CultureInfo.InvariantCulture) },
        };
        if (recorder != null) recorder.StartRecording($"{runName}_{policy}", meta);

        yield return new WaitForSeconds(collectSeconds);

        if (recorder != null) recorder.StopRecording();
        running = false;
        _co = null;
    }

    private void ApplyPolicyAndSpeed()
    {
        if (constantPolicy != null) constantPolicy.ConstantC = constantC;
        if (policySelector != null) policySelector.SetPolicy(policy);
        if (speedCommand != null) speedCommand.Publish(beltSpeed);
    }

    private void StopAll()
    {
        if (_co != null) { StopCoroutine(_co); _co = null; }
        if (recorder != null && recorder.Recording) recorder.StopRecording();
        running = false;
    }
}
