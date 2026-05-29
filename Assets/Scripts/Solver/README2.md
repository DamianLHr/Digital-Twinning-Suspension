# BumpPipeline — the bridge between ToF and the solver

## Where it sits

```
ToFSensorOutput  --OnDistance(float)-->  BumpPipeline  --SetDamping(c)-->  DampingActuator
                                              |
                                              | schedules
                                              v
                                       DampingSearchJob (Burst)
```

NOT the main controller. ControlOrchestrator (which already exists in the
diagram) sits one level up: it owns mode/lifecycle, wires sensors per the
SensorBindingConfig, and enables/disables this pipeline as a unit. The pipeline
itself only does signal processing for one channel: ToF -> solver -> actuator.

## State machine

- **Idle** — flat road. Distance samples go into a pre-roll ring buffer.
  When height >= triggerHeight, transition to Accumulating.
- **Accumulating** — collecting the bump. Each sample is appended. When height
  falls below the trigger for endSamples consecutive samples (hysteresis) OR
  maxProfileSamples is hit, the bump ends.
- **Cooldown** — refractory period (cooldownSamples) before re-arming, so a
  noisy trailing edge can't immediately retrigger.

## Geometry

- height = nominalStandoff - distance      (bumps are positive)
- Each sample is paired with TerrainWheel.TraveledDistance for spatial context.
- On end-of-bump, the (Pos, Height) series is resampled onto a uniform position
  grid, converted to a uniform time grid via dt = (length / beltSpeed) / (N-1),
  and handed to the Burst solver as a RoadProfile.

## Async solver dispatch

ScheduleSolve() schedules the parallel DampingSearchJob and returns immediately.
Update() polls JobHandle.IsCompleted; when true, picks the candidate with the
lowest peak |accel| and pushes it to DampingActuator.

Predictive slack (lastPredictiveSlackMs) reports how much time the actuator has
between "solver done" and "leading edge reaches the contact patch". If this
goes negative, your sensor lead L is too small for the belt speed — increase L
or reduce solverSteps / cCandidates.

## If two bumps overlap

If a new bump triggers while the previous solve is still running, the new bump
is dropped with a warning. This is the right default — chaining solves would
blow the time budget. If you see drops, your solver is too slow OR your bumps
are too close together; tune cCandidates / solverSteps.

## Wiring (Inspector)

1. Add BumpPipeline to a GameObject in the rig.
2. Drag in the ToFSensorOutput, TerrainWheel, and DampingActuator.
3. Set nominalStandoff to the real distance from the ToF emitter to flat road.
4. Set sensorLead to the physical distance from the emitter to the contact patch.
5. Set mass/stiffness/cMin/cMax to match your rig.

## Resource ownership

Every NativeArray and RoadProfile allocated by ScheduleSolve is disposed in
DisposeInFlight, which runs both on normal completion and from OnDisable. No
leaks across scene reloads.
