# DIAG analysis (Python)

Plots and statistics for the data runs collected by Unity's `RunDataRecorder`.
Unity only **records** (CSV); all plotting/analysis lives here. You can launch it
**from inside Unity** (recommended) or from a terminal.

## 1. Collect data in Unity
Wire `RunDataRecorder`, `DampingPolicySelector` (+ `ConstantDampingPolicy`) and
`ScenarioRunner` in the scene, then run **collect** scenarios. Each run writes two
CSVs (with a `#`-metadata header):

- `<run>_timeseries.csv` — per accelerometer sample
- `<run>_bumps.csv` — per solved bump

In the Editor these go to **`Assets/DiagnosticsData/`** by default (set by the
recorder's *Assets Subfolder*). Standalone builds fall back to
`Application.persistentDataPath`.

## 2. Run the analysis from Unity (recommended)
Menu **Diagnostics ▸ Analysis Runner**. Set the Python executable (default `python`)
and the CSV folder (default `Assets/DiagnosticsData`), then:

- **Install Python requirements** (once)
- **Run Experiment A** — predictive vs constant
- **Run Experiment B** — predicted vs actual jolt

Figures are written next to the CSVs (`experiment_a.png/.pdf`, …), the Project
window refreshes, and the file is revealed when done.

## 3. Or run from a terminal
```bash
cd Assets/Scripts/Diagnostics/Analysis
python -m venv .venv && source .venv/bin/activate   # (Windows: .venv\Scripts\activate)
pip install -r requirements.txt

# Experiment A — two runs over the same scenario (policy Constant, then Predictive):
python experiment_a.py --const run_Constant --pred run_Predictive --data "../../../DiagnosticsData"

# Experiment B — one predictive run:
python experiment_b.py --run run_Predictive --data "../../../DiagnosticsData"
```

## Notes
- Every run is performed entirely in Simulation **or** entirely on the Twin — the
  CSV metadata records which (`mode=…`). The same scripts handle both.
- Figures are saved as high-DPI PNG **and** vector PDF for reports.
- The runner sets `MPLBACKEND=Agg`, so matplotlib runs headless (save-only).
