#!/usr/bin/env python3
"""DIAG Experiment A — predictive vs constant damping.

Compares the sprung-mass vertical acceleration for the two runs (constant baseline
vs current predictive control) and reports the RMS / mean-peak reduction.

    python experiment_a.py --const run_Constant --pred run_Predictive --data <csv folder>

`--const` / `--pred` are the run-name prefixes (the recorder writes
`<name>_timeseries.csv` and `<name>_bumps.csv`). Two figures are written:
  • <out>.png/.pdf      — headline: a readable representative segment + RMS/peak bars.
  • <out>_bump.png/.pdf — a single representative bump (ensemble mean over every bump,
                          individual passes faint behind it), the clean per-bump view.
"""
from __future__ import annotations

import argparse

import numpy as np
import pandas as pd
import matplotlib.pyplot as plt

from diag import io, style

# All accel numbers are in g and small (≈0.0x–0.x), so format tight metrics with 4 dp.
G_FMT = "{:.4f}"
PCT_FMT = "{:+.2f}"


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


def representative_window(df: pd.DataFrame, accel: np.ndarray, targets: np.ndarray,
                          span: float, offset: float = 0.5) -> tuple[np.ndarray, np.ndarray]:
    """A short, readable slice of the run for the overview panel: `span` metres of belt
    travel starting at the first detected bump ~`offset` m into the run (past warm-up).
    x is returned relative to the window start so both runs share an axis."""
    belt = df["beltPos"].to_numpy()
    start_floor = belt[0] + offset
    cand = targets[targets >= start_floor]
    x0 = float(cand[0]) if len(cand) else start_floor
    m = (belt >= x0) & (belt <= x0 + span)
    return belt[m] - x0, accel[m]


def bump_stack(df: pd.DataFrame, accel: np.ndarray, targets: np.ndarray,
               pre: float = 0.02, post: float = 0.04, n: int = 150) -> tuple[np.ndarray, np.ndarray]:
    """Resample every detected bump onto a common grid [-pre, +post] m around its contact
    (target) point. Returns (stack [n_bumps × n], grid_m). post is kept below the minimum
    bump spacing so a neighbour doesn't bleed in. Pass an already-despiked signal."""
    belt = df["beltPos"].to_numpy()
    grid = np.linspace(-pre, post, n)
    rows = []
    for t in targets:
        m = (belt >= t - pre) & (belt <= t + post)
        if np.count_nonzero(m) < 5:
            continue
        xs = belt[m] - t
        order = np.argsort(xs)
        rows.append(np.interp(grid, xs[order], accel[m][order]))
    return (np.array(rows) if rows else np.empty((0, n))), grid


def draw_representative_bump(out: str, pmeta, cdf, pdf, ca, pa,
                             c_targets, p_targets) -> tuple[float, float, int, int]:
    """Separate figure: the ensemble-mean bump for each policy (bold) drawn over every
    individual bump (faint). Averaging is what makes it 'representative' — it cancels the
    residual contact noise and shows the typical response to a bump."""
    cs, grid = bump_stack(cdf, ca, c_targets)
    ps, _ = bump_stack(pdf, pa, p_targets)
    gx = grid * 1000.0  # mm

    fig, ax = plt.subplots(figsize=(8.5, 5.5), layout="constrained")

    for row in cs:
        ax.plot(gx, row, color=style.PALETTE["constant"], lw=0.4, alpha=0.10)
    for row in ps:
        ax.plot(gx, row, color=style.PALETTE["predictive"], lw=0.4, alpha=0.10)

    c_mean = cs.mean(axis=0) if len(cs) else np.zeros_like(grid)
    p_mean = ps.mean(axis=0) if len(ps) else np.zeros_like(grid)
    c_pk = float(np.max(np.abs(c_mean))) if len(cs) else 0.0
    p_pk = float(np.max(np.abs(p_mean))) if len(ps) else 0.0
    imp = (c_pk - p_pk) / c_pk * 100.0 if c_pk else 0.0

    ax.plot(gx, c_mean, color=style.PALETTE["constant"], lw=2.4,
            label=f"Constant   (mean of {len(cs)} bumps · peak {G_FMT.format(c_pk)} g)")
    ax.plot(gx, p_mean, color=style.PALETTE["predictive"], lw=2.4,
            label=f"Predictive (mean of {len(ps)} bumps · peak {G_FMT.format(p_pk)} g)")
    ax.axhline(0, color=style.PALETTE["muted"], lw=0.7, alpha=0.6)
    ax.axvline(0, color=style.PALETTE["muted"], lw=0.8, ls=":", alpha=0.7)

    ax.set_xlabel("belt travel relative to bump contact (mm)")
    ax.set_ylabel("sprung-mass vertical accel (g)")
    ax.set_title("Representative bump — ensemble mean (bold) over individual bumps (faint), despiked")
    ax.legend(loc="upper right")

    mode = pmeta.get("mode", "?")
    spd = pmeta.get("beltSpeed", "?")
    fig.suptitle(f"DIAG Experiment A — representative bump · {mode} · belt {spd} m/s · "
                 f"mean-bump peak {PCT_FMT.format(imp)}%", fontsize=12, fontweight="bold")

    style.save(fig, out + "_bump")
    return c_pk, p_pk, len(cs), len(ps)


def main() -> None:
    p = argparse.ArgumentParser(description="DIAG Experiment A — predictive vs constant damping")
    p.add_argument("--const", required=True, help="constant-run name prefix")
    p.add_argument("--pred", required=True, help="predictive-run name prefix")
    p.add_argument("--data", default=".", help="folder containing the CSVs")
    p.add_argument("--out", default="experiment_a", help="output figure base path")
    args = p.parse_args()

    style.apply()

    # Read timeseries, then despike (see despike() — removes PhysX contact artifacts so
    # both the RMS and the peak metrics reflect the real, band-limited ride response).
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
    c_targets = cb["target"].dropna().to_numpy()
    p_targets = pb["target"].dropna().to_numpy()
    c_peak, c_count = mean_peak(cdf, ca, c_targets)
    p_peak, p_count = mean_peak(pdf, pa, p_targets)

    cm = {"rms": c_rms, "peak": c_peak}
    pm = {"rms": p_rms, "peak": p_peak}
    imp = {k: ((cm[k] - pm[k]) / cm[k] * 100.0 if cm[k] else 0.0) for k in cm}

    dia = float(pmeta.get("drumDiameter", 0.14) or 0.14)
    circ = np.pi * dia if dia > 0 else 0.44
    span = 1.3 * circ   # ~1.3 drum revolutions: readable (~a handful of bumps), not the whole run

    fig, axd = plt.subplot_mosaic(
        [["overlay", "overlay"], ["rms", "peak"]],
        figsize=(11, 7.5), gridspec_kw={"height_ratios": [2, 1]}, layout="constrained",
    )

    # --- top: a readable representative segment (NOT the full run, which is unreadably dense) ---
    ax = axd["overlay"]
    cwx, cwy = representative_window(cdf, ca, c_targets, span)
    pwx, pwy = representative_window(pdf, pa, p_targets, span)
    ax.plot(cwx, cwy, color=style.PALETTE["constant"], lw=1.1, alpha=0.9,
            label=f"Constant   (RMS {G_FMT.format(cm['rms'])} g · mean peak {G_FMT.format(cm['peak'])} g)")
    ax.plot(pwx, pwy, color=style.PALETTE["predictive"], lw=1.1, alpha=0.9,
            label=f"Predictive (RMS {G_FMT.format(pm['rms'])} g · mean peak {G_FMT.format(pm['peak'])} g)")
    ax.axhline(0, color=style.PALETTE["muted"], lw=0.7, alpha=0.6)
    ax.set_xlabel("belt travel within a representative segment (m)")
    ax.set_ylabel("sprung-mass vertical accel (g)")
    ax.set_title(f"Representative ~{span / circ:.1f}-rev segment (despiked) — full-run stats in legend")
    ax.legend(loc="upper right")

    # --- bottom: the headline quantitative comparison ---
    for key, name in [("rms", "RMS acceleration"), ("peak", "Mean Peak |acceleration|")]:
        a2 = axd[key]
        vals = [cm[key], pm[key]]
        bars = a2.bar(["Constant", "Predictive"], vals,
                      color=[style.PALETTE["constant"], style.PALETTE["predictive"]], width=0.6)
        good = imp[key] > 0
        a2.set_title(f"{name}\n{PCT_FMT.format(imp[key])}% improvement",
                     color=(style.PALETTE["accent"] if good else style.PALETTE["constant"]))
        a2.set_ylabel("g")
        for b, v in zip(bars, vals):
            a2.text(b.get_x() + b.get_width() / 2, v, G_FMT.format(v),
                    ha="center", va="bottom", fontsize=9)
        a2.margins(y=0.30)

    spd = pmeta.get("beltSpeed", "?")
    mode = pmeta.get("mode", "?")
    fig.suptitle(f"DIAG Experiment A — {mode} · belt {spd} m/s · drum Ø {dia} m",
                 fontsize=12, fontweight="bold")

    style.save(fig, args.out)

    # --- separate figure: the single representative bump ---
    bc_pk, bp_pk, bc_n, bp_n = draw_representative_bump(args.out, pmeta, cdf, pdf, ca, pa,
                                                        c_targets, p_targets)

    print(f"RMS improvement:        {PCT_FMT.format(imp['rms'])}%   "
          f"(Constant {G_FMT.format(c_rms)} g -> Predictive {G_FMT.format(p_rms)} g)")
    print(f"Mean-peak improvement:  {PCT_FMT.format(imp['peak'])}%   "
          f"(Constant {G_FMT.format(c_peak)} g -> Predictive {G_FMT.format(p_peak)} g)")
    print(f"Peaks detected:         Constant {c_count}, Predictive {p_count} bumps")
    print(f"Representative bump:     Constant peak {G_FMT.format(bc_pk)} g ({bc_n} bumps), "
          f"Predictive peak {G_FMT.format(bp_pk)} g ({bp_n} bumps)")
    print(f"saved {args.out}.png/.pdf and {args.out}_bump.png/.pdf")


if __name__ == "__main__":
    main()
