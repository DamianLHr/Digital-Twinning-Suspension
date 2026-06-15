#!/usr/bin/env python3
"""DIAG Experiment A — predictive vs constant damping.

Overlays the sprung-mass vertical acceleration for the two runs (constant baseline
vs current predictive control) over the same bumps, and reports the RMS / mean peak
reduction as a percentage improvement.

    python experiment_a.py --const run_Constant --pred run_Predictive --data <csv folder>

`--const` / `--pred` are the run-name prefixes (the recorder writes
`<name>_timeseries.csv` and `<name>_bumps.csv`). Output: experiment_a.png / experiment_a.pdf.
"""
from __future__ import annotations

import argparse

import numpy as np
import pandas as pd
import matplotlib.pyplot as plt

from diag import io, style


def vertical_accel(meta, df) -> np.ndarray:
    """Vertical proper acceleration (accel.y minus the gravity baseline)."""
    g = float(meta.get("gravityBaseline", 9.81))
    return df["accelY"].to_numpy() - g


def despike(a: np.ndarray, k: int = 5) -> np.ndarray:
    """Reject single-tick contact spikes with a short rolling median.

    The digital accelerometer finite-differences rigidbody velocity, so a PhysX rigid
    contact (a near-discontinuous Δv in one physics tick) shows up as a 1–2 sample
    acceleration spike — orders of magnitude above the real ride response and far above
    what a band-limited MPU-6050 could ever register. The sprung mass oscillates at
    ~10 Hz (many samples wide), so a length-k median erases the artifacts and leaves the
    genuine response intact.
    """
    return pd.Series(a).rolling(k, center=True, min_periods=1).median().to_numpy()


def mean_peak(df: pd.DataFrame, accel: np.ndarray, targets: np.ndarray,
              window: float = 0.025) -> tuple[float, int]:
    """Mean per-bump peak |accel| in a ±window belt-position band around each of THIS
    run's OWN detected bump targets. Pass an already-despiked signal.

    Each run records its own _bumps.csv, so we read its real target positions directly
    rather than projecting one run's bump pattern onto the other — the two runs start at
    different drum phases, so projection misaligns after the first bump.
    """
    belt_pos = df["beltPos"].to_numpy()
    abs_a = np.abs(accel)
    peaks = []
    for t in targets:
        mask = (belt_pos >= t - window) & (belt_pos <= t + window)
        if np.any(mask):
            peaks.append(float(np.max(abs_a[mask])))
    return (float(np.mean(peaks)) if peaks else 0.0), len(peaks)


def main() -> None:
    p = argparse.ArgumentParser(description="DIAG Experiment A — predictive vs constant damping")
    p.add_argument("--const", required=True, help="constant-run name prefix")
    p.add_argument("--pred", required=True, help="predictive-run name prefix")
    p.add_argument("--data", default=".", help="folder containing the CSVs")
    p.add_argument("--out", default="experiment_a", help="output figure base path")
    args = p.parse_args()

    style.apply()
    
    # Read timeseries data, then despike (see despike() — removes PhysX contact artifacts
    # so both the RMS and the peak metrics reflect the real, band-limited ride response).
    cmeta, cdf = io.timeseries(args.data, args.const)
    pmeta, pdf = io.timeseries(args.data, args.pred)
    ca = despike(vertical_accel(cmeta, cdf))
    pa = despike(vertical_accel(pmeta, pdf))

    # RMS from the (despiked) timeseries
    c_rms = float(np.sqrt(np.mean(ca ** 2)))
    p_rms = float(np.sqrt(np.mean(pa ** 2)))

    # Mean peak — each run uses its OWN recorded bump targets (no cross-run projection).
    _, cb = io.bumps(args.data, args.const)
    _, pb = io.bumps(args.data, args.pred)
    c_peak, c_count = mean_peak(cdf, ca, cb["target"].dropna().to_numpy())
    p_peak, p_count = mean_peak(pdf, pa, pb["target"].dropna().to_numpy())

    cm = {"rms": c_rms, "peak": c_peak}
    pm = {"rms": p_rms, "peak": p_peak}
    imp = {k: ((cm[k] - pm[k]) / cm[k] * 100.0 if cm[k] else 0.0) for k in cm}

    # Align both runs to start at belt-travel 0 for a like-for-like overlay.
    cx = cdf["beltPos"].to_numpy() - cdf["beltPos"].iloc[0]
    px = pdf["beltPos"].to_numpy() - pdf["beltPos"].iloc[0]

    fig, axd = plt.subplot_mosaic(
        [["overlay", "overlay"], ["rms", "peak"]],
        figsize=(11, 7.5), gridspec_kw={"height_ratios": [2, 1]}, layout="constrained",
    )

    ax = axd["overlay"]
    ax.plot(cx, ca, color=style.PALETTE["constant"], lw=1.0, alpha=0.85,
            label=f"Constant   (RMS {cm['rms']:.2f}, mean peak {cm['peak']:.2f} g)")
    ax.plot(px, pa, color=style.PALETTE["predictive"], lw=1.0, alpha=0.85,
            label=f"Predictive (RMS {pm['rms']:.2f}, mean peak {pm['peak']:.2f} g)")
    ax.axhline(0, color=style.PALETTE["muted"], lw=0.7, alpha=0.6)
    ax.set_xlabel("belt travel (m)")
    ax.set_ylabel("sprung-mass vertical accel (g)")
    ax.set_title("Sprung-mass acceleration — predictive vs constant damping")
    ax.legend(loc="upper right")

    for key, name in [("rms", "RMS acceleration"), ("peak", "Mean Peak |acceleration|")]:
        a2 = axd[key]
        vals = [cm[key], pm[key]]
        bars = a2.bar(["Constant", "Predictive"], vals,
                      color=[style.PALETTE["constant"], style.PALETTE["predictive"]], width=0.6)
        good = imp[key] > 0
        a2.set_title(f"{name}\n{imp[key]:+.1f}% improvement",
                     color=(style.PALETTE["accent"] if good else style.PALETTE["constant"]))
        a2.set_ylabel("g")
        for b, v in zip(bars, vals):
            a2.text(b.get_x() + b.get_width() / 2, v, f"{v:.2f}",
                    ha="center", va="bottom", fontsize=9)
        a2.margins(y=0.25)

    spd = pmeta.get("beltSpeed", "?")
    dia = pmeta.get("drumDiameter", "?")
    mode = pmeta.get("mode", "?")
    fig.suptitle(f"DIAG Experiment A — {mode} · belt {spd} m/s · drum Ø {dia} m",
                 fontsize=12, fontweight="bold")

    style.save(fig, args.out)
    print(f"RMS improvement:       {imp['rms']:+.1f}%")
    print(f"Mean Peak improvement: {imp['peak']:+.1f}%")
    print(f"Peaks detected (Constant):   {c_count} bumps")
    print(f"Peaks detected (Predictive): {p_count} bumps")
    print(f"saved {args.out}.png and {args.out}.pdf")


if __name__ == "__main__":
    main()