# Mode contract (Simulating ↔ Twinning)

How the rig switches between **Simulating** (the Unity model is the source of truth)
and **Twinning** (real hardware is the source of truth). `ModeManager` is the single
authority — components never read the global mode on their own.

## The three mechanisms

`ModeManager.Apply()` scans **all** MonoBehaviours in the scene
(`FindObjectsByType(... FindObjectsInactive.Include ...)` — note: **including those on
inactive GameObjects**) and, for each:

1. **Marker interfaces → `enabled` toggling**
   - `IDigitalDevice` → `enabled = (mode == Simulating)`
   - `IRealDevice`    → `enabled = (mode == Twinning)`
   These are the digital/real sensor & actuator families, and the serial transport
   (`PicoSerialTransport` is `IRealDevice`, so it only connects in Twinning).
   Their `OnEnable`/`OnDisable` do the subscribe/unsubscribe, so wiring settles
   deterministically after `Apply()` (which runs in `Start`, after every `OnEnable`).

2. **`IModeReceiver.OnModeChanged(mode)` → behavioural change without enable/disable**
   For components that live in *both* modes but behave differently, e.g.
   `PotentiometerVisualizer` (display-only in Simulating, drives the model in Twinning).

3. **`ModeScopedObject` (an `IModeReceiver`) → GameObject activation**
   Toggles `GameObject.SetActive` per mode for objects that should *exist* in only one
   mode — e.g. the wheel bumps (Simulating-only → smooth drum when Twinning).

## Why the inactive-scan matters

A `ModeScopedObject` that deactivates its own GameObject must still be reachable to be
**re**-activated on the next switch. `ModeManager` finds components on inactive objects
(`FindObjectsInactive.Include`) and calls `OnModeChanged` on them, so toggling works in
both directions. **Do not change `ModeManager` to skip inactive objects** without
replacing this guarantee.

## Things that are NOT mode-gated

- `TerrainWheel`, `TerrainWheelSpeedDriver`, `BumpPipeline`, `DampingCommandScheduler`,
  the shared `*Output` / `*Command` channels, and the visualizers run in **both** modes.
- The Terrain Wheel's motion comes from `TerrainWheelSpeedDriver` in both modes (it is
  the drum's drive in Simulating and an open-loop estimate in Twinning).

## When to use which

- Need a device live in only one mode, with subscribe/unsubscribe? → `IDigitalDevice` /
  `IRealDevice`.
- Need a component to *change behaviour* by mode but stay alive? → `IModeReceiver`.
- Need a GameObject (geometry/visual) present in only one mode? → `ModeScopedObject`.
