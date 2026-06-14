#!/usr/bin/env python3
"""DIAG Experiment B — solver-predicted vs actual jolt.

For a predictive run, compares the solver's predicted peak acceleration against the
measured peak when each bump actually reached the wheel, plus the landing-position
error (jolt − target).

    python experiment_b.py --run run_Predictive --data <csv folder>

Output: experiment_b.png / experiment_b.pdf.
"""
from __future__ import annotations

import argparse
import os

import numpy as np
import matplotlib.pyplot as plt

from diag import io, style


def main() -> None:
    p = argparse.ArgumentParser(description="DIAG Experiment B — predicted vs actual jolt")
    p.add_argument("--run", required=True, help="predictive-run name prefix")
    p.add_argument("--data", default=".", help="folder containing the CSVs")
    p.add_argument("--out", default="experiment_b", help="output figure base path")
    args = p.parse_args()

    style.apply()
    meta, df = io.bumps(args.data, args.run)
    d = df.dropna(subset=["actualPeak", "predictedPeak"])
    pred = d["predictedPeak"].to_numpy()
    act = d["actualPeak"].to_numpy()
    err = act - pred
    land = df["landingErrorMm"].dropna().to_numpy()

    fig, axd = plt.subplot_mosaic(
        [["scatter", "err"], ["scatter", "land"]],
        figsize=(11.5, 7.5), layout="constrained",
    )

    # --- predicted vs actual peak ---
    ax = axd["scatter"]
    hi = max(pred.max(), act.max()) * 1.1 if len(pred) else 1.0
    lim = [0.0, hi]
    ax.plot(lim, lim, ls="--", color=style.PALETTE["muted"], lw=1.2, label="1:1 (perfect)")
    ax.scatter(pred, act, s=42, color=style.PALETTE["predictive"], alpha=0.85,
               edgecolor="white", linewidth=0.6, zorder=3)
    ax.set_xlim(lim); ax.set_ylim(lim)
    ax.set_aspect("equal", adjustable="box")
    ax.set_xlabel("predicted peak accel (g)")
    ax.set_ylabel("actual peak accel (g)")
    ax.set_title("Predicted vs actual jolt")
    ax.legend(loc="upper left")

    # --- peak error histogram ---
    a1 = axd["err"]
    if len(err):
        a1.hist(err, bins=20, color=style.PALETTE["accent"], alpha=0.85, edgecolor="white")
        a1.axvline(0, color=style.PALETTE["muted"], ls="--", lw=1)
        a1.set_title(f"peak error   mean {err.mean():+.2f},  σ {err.std():.2f} g")
    else:
        a1.set_title("peak error (no jolted bumps)")
    a1.set_xlabel("actual − predicted (g)")
    a1.set_ylabel("bumps")

    # --- landing error histogram ---
    a2 = axd["land"]
    if len(land):
        a2.hist(land, bins=20, color=style.PALETTE["constant"], alpha=0.8, edgecolor="white")
        a2.axvline(0, color=style.PALETTE["muted"], ls="--", lw=1)
        a2.set_title(f"landing error   mean {land.mean():+.0f},  σ {land.std():.0f} mm")
    else:
        a2.set_title("landing error (no data)")
    a2.set_xlabel("jolt − target (mm)")
    a2.set_ylabel("bumps")

    mode = meta.get("mode", "?")
    fig.suptitle(f"DIAG Experiment B — {mode} · {len(d)} jolted bumps",
                 fontsize=12, fontweight="bold")

    style.save(fig, args.out)
    if len(pred) > 1:
        r = np.corrcoef(pred, act)[0, 1]
        print(f"n={len(d)}  peak MAE={np.mean(np.abs(err)):.2f}  bias={err.mean():+.2f}  r={r:.3f}")
    if len(land):
        print(f"landing: mean {land.mean():+.0f} mm,  sd {land.std():.0f} mm")
    print(f"saved {args.out}.png and {args.out}.pdf")


if __name__ == "__main__":
    main()
