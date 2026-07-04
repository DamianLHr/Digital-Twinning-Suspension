# Digital-Twinning-Suspension — Master Change List

> Standalone working doc (not committed to the repo). Status legend: ✅ done · 🟡 partial/blocked · ⬜ not started · 🐞 investigate.
> Last updated: Class diagram regenerated to current code (v6, all 5 sheets, verified non-overlapping). Prev: User UI complete (startup + grouped visualizer menus); Experiment-A plotting.

## Tier 0 — Critical bugs

- [x] **✅ C-1 · Digital accelerometer publishes velocity, not acceleration.** Fixed — now computes proper (gravity-compensated) acceleration directly from any assigned Rigidbody; generalized to serve both the sprung- and unsprung-mass accelerometers. `DigitalAccelerometer.cs`.
- [x] **✅ K-2 · Concave MeshCollider on dynamic Rigidbody.** Done (collider removed from the Sprung Mass by the user).

## Tier 1 — Known bug fixes

- [x] **✅ K-1 · Tire stiffness drives the actual tyre joint.** `QuarterCarConfig` now pushes stiffness + damping into the `UnsprungMass` tyre joint via `SetTyreDrive(...)`; single source of truth. *(Verify the tyre values on the existing config instance — it now wins.)*
- [x] **✅ K-3 · Damping scheduler timing.** Burst warm-up solve at startup + trailing-edge belt position carried through `SolveSnapshot` (latency-free observation). `BumpPipeline.cs`, `DampingCommandScheduler.cs`.
- [x] **✅ Removed per-solve debug log spam.**
- [x] **✅ Reflection removed.** Config-written fields made public; `QuarterCarConfig` assigns directly; reflection helper deleted.

## Tier 2 — Mission-critical visualizer & UI

- [x] **✅ Scheduler-accuracy visualizer.** `SchedulerAccuracyVisualizer.cs` — world-space C tags riding the drum + timeline table (observed/target/applied/jolt, residual mm+ms, v_solve→v_apply). Fed by new scheduler events (`OnCommandScheduled`/`OnCommandApplied`/`OnJolt`). *(Needs scene wiring: scheduler, drum, tofEmitter, camera.)*
- [x] **✅ Damping-confidence / operating-envelope warnings.** `DampingConfidenceMonitor.cs` — severity-tagged banner + public `Diagnostics` API. Covers belt stopped, calibration not established, under-sampled bump, negative slack, best-C at boundary, speed-changed solve→apply. *(Needs wiring: pipeline, scheduler, drum.)*
- [x] **✅ V-1 · Stable graph panels.** Latched placement anchor + `placementDeadzone` in `VisualizerManager` — panels hold still while only the leader line tracks the moving object.
- [x] **✅ V-2 · Master show/hide-all toggle** in the F1 visualizer menu.

### Tier 2 follow-ups (IMPORTANT — do later)

- [x] **✅ SA-1 · Scheduler-accuracy table trimmed.** 4 aligned columns (state / C / apply-in countdown / error mm, colour-graded), live time-to-application, narrowed panel.
- [x] **✅ SA-2 · World C-tags now match the bumps.** Drum diameter set to **140 mm**; tags align with the physical bumps and the scheduler offset is correct (≈ 0.75 × π × 0.14 ≈ 330 mm at 90°).

## Tier 3 — Serial integration (Raspberry Pi Pico)

*Decisions resolved: accel = g · belt commanded over serial · wheel speed not sensed · PC owns c→steps · Pico sends incrementing packet_id.*

```
TwinData  (Pico → PC,  header 0xAA 0xBB)        CommandData (PC → Pico, header 0xCC 0xDD)
  packet_id            // ++ each packet           int target_steps   // damping stepper (PC maps c→steps)
  float accel_x/y/z    // in g  → ×9.81            int belt_command   // belt speed (units TBD)
  int   distance_mm
  int   analog_1_raw, analog_2_raw
```

> ✅ **Compiles & runs:** Api Compatibility Level set to .NET Framework; serial **receiving works on hardware**. Sending wired (verify on device). Known issue: intermittent all-zero frame — see INVESTIGATE below.

- [x] **✅ S-1** `PicoSerialTransport : ISensorPacketSource, IActuatorPacketSink` — bg read thread + main-thread drain; auto-reconnect.
- [x] **✅ S-2** Framing + robust read (sync `0xAA 0xBB`, read-fully loop, resync).
- [x] **✅ S-3** `Pack=1` struct marshaling. *(packet_id assumed leading uint32 — D-5b.)*
- [x] **✅ S-4** Inbound demux: one `TwinData` → per-channel `SensorPacket`s; host-time + `packet_id` stamp.
- [x] **✅ S-5** Decoder updates: accel ×9.81; ToF int mm→m (40–600); pots ×2 int 0–4096 → ÷4096×stroke 0.06.
- [x] **✅ S-13** `RealWheelSpeedSensor` not used in real mode (leave unwired — wheel speed not sensed).
- [x] **✅ S-7** Outbound mux: aggregate damping + belt into one `CommandData`.
- [x] **✅ S-8** c → `target_steps` calibration on the PC (linear map in RealDampingActuator).
- [x] **✅ S-9** `belt_command` field; `RealBeltActuator` maps speed→belt_command.
- [x] **✅ S-14** Belt-speed estimator (Twinning) drives `TerrainWheel` from the commanded speed.
- [x] **✅ S-15 · Blank-frame stopgap.** `PicoSerialTransport.rejectBlankFrames` skips all-zero frames so subscribers hold their last value (`blankFrames` counter + a one-shot diagnostic log of the offending `packet_id`).
- [x] **✅ Mode-scoped GameObjects.** `ModeScopedObject` (IModeReceiver) toggles GameObjects per mode — wheel bumps set Simulating-only → **smooth drum when Twinning**. No `ModeManager` change (it already notifies inactive objects).
- [x] **✅ Unsprung accelerometer is digital-only** (no physical sibling); `RealAccelerometer` serves the sprung mass only.
- [x] **✅ S-12** Scene fully tuned for the real device (channels wired, sensors calibrated, rig scaled).
- [x] **✅ S-6** Digital/real potentiometer scale reconciled.
- [x] **✅ S-10** Per-sensor rates set (all sensors at 125 Hz).
- [ ] **S-11** Check USB max send rate vs combined rates.
- [ ] **🐞 INVESTIGATE · Zero-packet root cause.** Intermittent all-zero frame. Classify via the one-shot log: valid incrementing `packet_id` → firmware sensor-read miss; whole-frame-zero → framing/TX timing (then add rolling-header sync + CRC). S-15 hides the symptom for now.
- [ ] **D-5b** Confirm `packet_id` width/position.

### Tier 3 — actuator calibration (next up)
- [ ] **A-1 · Damping c → stepper steps (real calibration).** `RealDampingActuator` currently uses a linear `c→steps` map. Replace/tune it with the real stepper's calibration — full step range / microsteps, and a non-linear curve if the damper needs one — so a commanded `c` lands at the correct physical damping.
- [ ] **A-2 · Belt command unit (was D-2b).** Determine what the Pico expects for `belt_command` (steps/s, mm/s, RPM, or raw PWM) and set `RealBeltActuator.commandPerMeterPerSecond` to match.

## Tier 4 — New features

- [x] **✅ Complete the user UI.** Pre-simulation startup menu (`SimulationStartupMenu`, IMGUI modal fullscreen): mode toggle (Simulation/Twin via `ModeManager.SetMode`); **scenario control / DIAG runner** — **Free run** (run forever, collect nothing; always available, the default) and **Run + collect data** (drives `ScenarioRunner` → `RunDataRecorder`); **serial params** (port/baud/connect via `PicoSerialTransport`). Rig frozen until launch; collapsed bottom-left bar (Menu/Stop) returns from a run, and the modal hides the visualizer/credits menus so nothing overlaps. Added public config accessors to `ScenarioRunner` + `PicoSerialTransport` for the menu. Also: **grouped visualizer selection menu** (Sensors / Control / Actuators, per-group toggles + master) via a new `IVisualizerPanel.Group`. Credits remains its existing top-right panel; confidence indicator via `DampingConfidenceMonitor`.
- [ ] Auto-calibration on start for all sensors (`ICalibratable` driven by `ModeManager`).
- [x] **✅ Spring visual + motor assets.** Spring animated by new `SpringStretch` (stretches/scales along its length axis between two assigned points; radius preserved). Motor model imported (static, or can drive the existing spin-indicator hook). Assets free-to-use — **credits still to be listed in the UI credits tab** (part of the Tier-4 UI task).
- [x] **✅ Bring the virtual device to real size (M-4).** Rig scaled (ruler asset), sensors calibrated, **drum diameter set to 140 mm** — scheduler offset and SA-2 now fully resolved.

## Diagnostic & data-collection suite (DIAG — IMPLEMENTED 2026-06-09; UI hookup pending)

**Goal:** quantify how much the predictive controller actually helps, and how accurate the solver is, by collecting data in Unity and plotting/analysing in **Python** (plots are out of scope for Unity — Unity only records). Architecture principle: a **passive observer + swappable-producer** design so existing scripts are essentially untouched (minimal hooks, minimal added complexity).

**Experiments to support** — each run is run *entirely* in Simulation OR *entirely* on the Twin (the same suite, only the settings differ):
- **A — predictive vs constant damping.** Two runs over the *same* bump sequence: (1) spring at a fixed constant damping coefficient (no prediction), (2) current predictive damping. Collect sprung-mass acceleration time-series for both; Python overlays them on one plot and reports a **% improvement** (e.g. RMS and peak acceleration reduction).
- **B — predicted vs actual jolt.** Compare the solver's predicted response (`SolveSnapshot.BestPeak`, and target position) against the actual measured jolt (peak accel + belt position from the accelerometer) per bump → magnitude error and timing/landing error stats.

**Proposed architecture (minimal hooks):**
- [x] **✅ DIAG-1 · `RunDataRecorder` (passive).** `Assets/Scripts/Diagnostics/RunDataRecorder.cs` — subscribes only to existing surfaces, writes `<run>_timeseries.csv` + `<run>_bumps.csv` (with `#`-metadata header) to **`Assets/DiagnosticsData/`** in the Editor (configurable; `persistentDataPath` in builds) and refreshes the AssetDatabase. Zero edits to existing scripts.
- [x] **✅ DIAG-2 · Damping policy swap.** `ConstantDampingPolicy` + `DampingPolicySelector` (predictive vs constant; one writer at a time). No change to the actuator.
- [x] **✅ DIAG-3 · `ScenarioRunner`** (component done): FREE-RUN default (runs forever, collects nothing) + COLLECT (warm-up → record N s → flush), context-menu triggers. **UI buttons still belong to the Tier-4 UI task.**
- [x] **✅ DIAG-4 · Experiment A (plotting finalized).** `Analysis/experiment_a.py`: headline figure now shows a **readable ~1.3-revolution representative segment** (the full-run overlay was unreadably dense — dropped) plus RMS/peak bars with % improvement, and a **separate single representative-bump figure** (`experiment_a_bump`: ensemble mean over faint individual bumps, aligned at contact). Accel is **despiked** (rolling median) to drop the 1-tick PhysX contact spikes; peaks use each run's **own** bump targets (cross-run projection removed — phase mismatch); small-g metrics print to **4 dp**. Verified end-to-end (PNG+PDF). *Note: the constant-vs-predictive gap is currently within noise (Welch p≈0.66) — re-record + more bumps before trusting any few-% verdict.*
- [x] **✅ DIAG-5 · Experiment B** — `Analysis/experiment_b.py`: predicted-vs-actual peak scatter (1:1), peak-error + landing-error histograms. Smoke-tested.
- [x] **✅ DIAG-6 · Python analysis package** — `Assets/Scripts/Diagnostics/Analysis/` (`diag/io.py`, `diag/style.py`, experiment scripts, `requirements.txt`, `README.md`); high-DPI PNG + vector PDF, publication styling. **Runnable from inside Unity** via an editor window: menu **Diagnostics ▸ Analysis Runner** (`Diagnostics/Editor/DiagAnalysisWindow.cs`) — sets Python path + CSV folder, installs requirements, runs each experiment headless (MPLBACKEND=Agg), reveals the figure. Verified end-to-end.

**Design considerations:**
- *Reproducibility:* runs must share the same drum, speed, and bump geometry; Unity physics isn't perfectly deterministic, so prefer identical scenarios and/or average over several bumps. Record enough metadata to pair runs.
- *Correlation:* per-bump matching (predicted↔actual↔applied) is the same nearest-target logic the accuracy visualizer already does — share it rather than duplicate.
- *Real vs sim:* the recorder is mode-agnostic (it reads the shared outputs), so the same suite works for hardware runs once Twinning is live; add the `packet_id`/dropped-frame columns for real runs.
- *Keep Unity to data only* — all plotting and stats live in Python to avoid bloating the runtime.

## Post-resize tuning & known issues (found 2026-06-09)

- [x] **✅ TUNE-1 · Rescale damping to the resized rig (fixes "solver always picks cMin").** With m≈0.13 kg, k=27 → critical damping ≈ **3.75 N·s/m**, but the search range is 50–3000 (≈13×–800× critical) → all overdamped → peak accel is monotonic in c → solver returns the floor (cMin=50). Fix: set `cMin/cMax` to bracket c_crit (≈[0.2, 20]), `initialDamping ≈ 3–4`, and rescale the tyre stiffness/damping and the `DampingActuatorVisualizer` cMin/cMax too. **Best:** auto-derive `cMin/cMax` from the computed `criticalDamping` (e.g. [0.1, 5]×c_crit) so the range can't desync from mass/stiffness again. The search algorithm itself is fine — only the range is wrong. (Config's "critical damping outside range" + confidence "best-C at boundary" warnings confirm this.)
- [x] **✅ SA-3 · Robust jolt↔command attribution.** `SchedulerAccuracyVisualizer.OnJolt` now confirms a command only with a jolt within a tolerance window of its Target (`joltMatchFraction` × wheelOffset, fallback `fallbackMatchTol`), one jolt per command, discarding unmatched jolts — kills the bogus error ≈ −wheelOffset.
- [x] **✅ TUNE-2 · Comfort cost = peak + λ·RMS.** `BumpPipeline` now selects the damping candidate minimising `peakAccel + dampingRmsWeight·rmsAccel` (was pure peak, which drove c far below critical → underdamped ringing → worse RMS than constant). **No jerk term** (a positive jerk reward would push an active damper to *increase* jerk; the paper's −0.052 is a duration confound). `dampingRmsWeight` (λ) is an inspector field: λ=0 = pure peak (discrete bumps), higher = ride-feel/RMS (continuous roughness). Start ~1 and A/B. *(Problem 2 — predicted≪actual model gap — intentionally untouched.)*
- [x] **✅ CTRL-1 · Early coefficient application.** `DampingCommandScheduler` applies each C as early as the previous bump's clear position allows (`applyAsEarlyAsPossible`, `bumpClearanceMeters`; or a fixed `applyLeadMeters` lead), giving the real stepper max slew time and never changing C mid-bump. `TargetPos` stays the expected-arrival reference so the error metric is unaffected.

## Architecture refactor pass (done 2026-06-07)

- [x] **✅ M1 · Single Terrain Wheel speed driver.** Removed `WheelDriveMotor` + `DigitalBeltActuator` + `BeltSpeedEstimator`; replaced with one `TerrainWheelSpeedDriver` (runs in both modes). **Scene cleanup required:** `BeltActuator.prefab` and `QuarterCarRig.prefab` now have missing-script components — replace with `TerrainWheelSpeedDriver` (wire `SpeedCommand` + `TerrainWheel`).
- [x] **✅ M2 · Centralized channel payload contract.** `PicoChannelCodec` holds each channel's byte layout; transport encodes / Real* sensors decode through it. Behaviour unchanged.
- [x] **✅ M3 · Extracted framing codec.** `PicoFrameParser` (pure, rolling-sync, unit-testable) pulled out of the transport; transport block-reads and feeds it. Hardens framing vs the old 2-byte sync.
- [x] **✅ M4 · Mode contract doc.** `Assets/Scripts/Mode/MODE_CONTRACT.md` — the 3 mode mechanisms + the inactive-scan dependency.
- [x] **✅ M5 · Removed vestigial wheel-speed family** (`DigitalWheelSpeedSensor`, `RealWheelSpeedSensor`, `WheelSpeedOutput`) — none were wired. **`Terrain Wheel.prefab` kept** as requested.
- [ ] **M6 · Namespaces — intentionally NOT done** (deferred at user's request).
- [x] **✅ Spring constant.** `QuarterCarConfig.springStiffness` → `springConstant`, default **27** (measured), mapped k→`positionSpring`, c→`positionDamper`, maxForce→`maximumForce`. Rename resets the scene's orphaned `springStiffness:200000` to 27 (verify in inspector).
- [x] **✅ Naming sweep (§1).** Renamed classes (file+meta GUID preserved): `SpeedCommand`→`TerrainWheelSpeedCommand`, `RealBeltActuator`→`RealTerrainWheelActuator`, `BeltSpeedSliderControl`→`TerrainWheelSpeedSliderControl`, `BeltActuatorVisualizer`→`TerrainWheelActuatorVisualizer`; prefab `BeltActuator.prefab`→`TerrainWheelActuator.prefab`. Fields `terrain`/`drum`→`terrainWheel` (with `[FormerlySerializedAs]` so scene wiring migrates), `vBelt`→`terrainSpeed`, `BeltMoving`→`TerrainWheelMoving`, `minBeltSpeed`→`minTerrainWheelSpeed`. **Kept (intentional):** `PicoChannels.Belt` + `CommandData.belt_command` (firmware wire term); the suspension-wheel "wheel" names (`wheelOffset`, `wheelRadius`) per §1's tyre-vs-terrain-wheel distinction.
- [x] **✅ Deprecated API.** `ModeManager` now uses `FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include)` (dropped the obsolete `FindObjectsSortMode`).
- [x] **✅ Masses kinematic in Twinning.** `SprungMass` + `UnsprungMass` implement `IModeReceiver` and set `Rigidbody.isKinematic = (mode == Twinning)` so physics doesn't fight the sensor-driven model.

## Tier 5 — Medium refactors

- [x] **✅ Update diagrams to current codebase.** Regenerated as `suspension_twin_class_diagram_v6.drawio` (v5 kept as backup) — full sync across all 5 sheets: stale design-era names renamed, removed classes dropped (wheel-speed family), all new subsystems added (DampingCommandScheduler, BumpPipeline + solver/RoadMath/RoadProfile, DIAG suite, SimulationStartupMenu, Pico stack, VisualizerManager + control visualizers). Built by a Python generator (`_generate_v6.py`, kept alongside) using a deterministic column-stack layout + orthogonal channel router; **geometrically verified: 0 box-overlaps / 0 edges-through-boxes / 0 edge-overlaps** on every sheet. Sheet 1 (overview) laid out as a slide-friendly ~2:1 grid. *(Remaining nicety: crossing-reduction pass to thin out line crossings — overlaps are already gone.)*

---

## Scheduler offset model (reference)
ToF mounted ~90° BEHIND the wheel (downstream) on a periodic drum → predictive offset = (1 − angle/360) × π × diameter = **0.75 × circumference** for 90°. Computed deterministically by `DampingCommandScheduler` (`useGeometricOffset`, `sensorAngleBehindDeg`). `sensorLead` is irrelevant to scheduling. **Diameter set to 140 mm → offset ≈ 330 mm; scheduler fully resolved and SA-2 fixed.**

## Reference data
**Weights** — Top Plate 130.4 g · Belt 88.3 g · Wheel 70.2 g
**Dimensions** — Wheel 6.5 cm dia · rig scaled to real size via ruler asset · **Drum diameter: 140 mm (set — scheduler & SA-2 resolved)**
**Sensors** — Pot: 6 cm, 0–4096 · ToF: 40–600 mm, 125 Hz · Accel: g→×9.81, 1 kHz · sensors calibrated · USB rate: *verify* · **unsprung accel = digital only**
**Serial** — 115200 baud, USB CDC; in `0xAA 0xBB`+`TwinData`, out `0xCC 0xDD`+`CommandData{target_steps, belt_command}`
