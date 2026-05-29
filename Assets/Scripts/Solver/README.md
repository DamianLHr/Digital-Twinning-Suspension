# Quarter-Car RK4 Solver (Burst Jobs)

C# / Burst port of the scipy solve_ivp quarter-car script. Single-DOF model:

    m * x'' = k*(y - x) + c*(y' - v)      state S = (x, v)

Verified against scipy adaptive RK45: peak |accel| 9.189 vs 9.184 (~0.05%).

## Files
- RoadMath.cs            Shared Sample / Deriv / Accel / Rk4Step (compiled into the jobs).
- QuarterCarRk4Job.cs    [BurstCompile] IJob -> full trajectory (t, x, v, a). The solve_ivp equivalent.
- DampingSearchJob.cs    [BurstCompile] IJobParallelFor -> peak & RMS accel per candidate c. The control-loop workhorse.
- Pchip.cs               Managed Fritsch-Carlson PCHIP (matches pchip_interpolate).
- RoadProfile.cs         Bakes y(t)/dy(t) onto a uniform grid (PCHIP + numpy-style gradient) into NativeArrays.
- QuarterCarSolver.cs    Managed API: SolveTrajectory, FindBestDamping, LinSpace.
- QuarterCarSolverDemo.cs  MonoBehaviour reproducing the Python run + a parallel damping sweep; optional CSV.
- Suspension.Solver.asmdef Assembly def referencing Unity.Burst / Collections / Mathematics.

## Packages required
com.unity.burst, com.unity.collections, com.unity.mathematics (install via Package Manager).

## Design notes / why it differs from scipy
- PCHIP + gradient run once on the managed side (Burst can't do monotone-cubic cheaply); the
  jobs linear-interpolate the baked arrays in O(1) — exactly as the Python does for dy via np.interp.
- Fixed-step RK4 replaces adaptive RK45: deterministic, branch-free, Burst-friendly. dt=0.01 is
  ample for this system (wn ~ 4.47 rad/s).
- The search job stores only the cost per candidate (no trajectories), so it is cheap to run
  every bump. Objective is peak sprung-mass acceleration (set minimizeRms=true for RMS instead).
- float throughout (Unity physics). Swap to double in RoadMath/jobs if you want scipy-grade precision.

## Live use
Replace RoadProfile.FromControlPoints with RoadProfile.FromSamples fed by the ToF bump profile
(ToFSensorOutput.OnBumpProfile) scaled by belt speed, then call FindBestDamping each bump and push
the winning c to your DampingActuator.

## Memory
Every NativeArray / Trajectory / results array is caller-owned — Dispose them (the demo uses
`using` and explicit Dispose). TempJob allocations must be released within ~4 frames.
