#!/usr/bin/env python3
"""DIAG Experiment A — predictive vs constant damping.

Overlays the sprung-mass vertical acceleration for the two runs (constant baseline
vs current predictive control) over the same bumps, and reports the RMS / peak
reduction as a percentage improvement.

    python experiment_a.py --const run_Constant --pred run_Predictive --data <csv folder>

`--const` / `--pred` are the run-name prefixes (the recorder writes
`<name>_timeseries.csv`). Output: experiment_a.png / experiment_a.pdf.
"""
from __future__ import annotations

import argparse
import os

import numpy as np
import matplotlib.pyplot as plt

from diag import io, style


def vertical_accel(meta, df) -> np.ndarray:
    """Vertical proper acceleration (accel.y minus the gravity baseline)."""
    g = float(meta.get("gravityBaseline", 9.81))
    return df["accelY"].to_numpy() - g


def metrics(a: np.ndarray) -> dict[str, float]:
    return {"rms": float(np.sqrt(np.mean(a ** 2))), "peak": float(np.max(np.abs(a)))}


def main() -> None:
    p = argparse.ArgumentParser(description="DIAG Experiment A — predictive vs constant damping")
    p.add_argument("--const", required=True, help="constant-run name prefix")
    p.add_argument("--pred", required=True, help="predictive-run name prefix")
    p.add_argument("--data", default=".", help="folder containing the CSVs")
    p.add_argument("--out", default="experiment_a", help="output figure base path")
    args = p.parse_args()

    style.apply()
    cmeta, cdf = io.timeseries(args.data, args.const)
    pmeta, pdf = io.timeseries(args.data, args.pred)
    ca, pa = vertical_accel(cmeta, cdf), vertical_accel(pmeta, pdf)
    cm, pm = metrics(ca), metrics(pa)
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
            label=f"Constant   (RMS {cm['rms']:.2f}, peak {cm['peak']:.2f} g)")
    ax.plot(px, pa, color=style.PALETTE["predictive"], lw=1.0, alpha=0.85,
            label=f"Predictive (RMS {pm['rms']:.2f}, peak {pm['peak']:.2f} g)")
    ax.axhline(0, color=style.PALETTE["muted"], lw=0.7, alpha=0.6)
    ax.set_xlabel("belt travel (m)")
    ax.set_ylabel("sprung-mass vertical accel (g)")
    ax.set_title("Sprung-mass acceleration — predictive vs constant damping")
    ax.legend(loc="upper right")

    for key, name in [("rms", "RMS acceleration"), ("peak", "Peak |acceleration|")]:
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
    print(f"RMS improvement:  {imp['rms']:+.1f}%")
    print(f"Peak improvement: {imp['peak']:+.1f}%")
    print(f"saved {args.out}.png and {args.out}.pdf")


if __name__ == "__main__":
    main()
