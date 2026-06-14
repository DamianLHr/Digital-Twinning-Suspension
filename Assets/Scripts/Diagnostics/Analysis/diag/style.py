"""Shared, publication-quality matplotlib styling for the DIAG figures."""
from __future__ import annotations

import matplotlib as mpl

PALETTE = {
    "predictive": "#2166AC",  # blue
    "constant":   "#B2182B",  # red
    "accent":     "#1A9850",  # green
    "muted":      "#7F7F7F",
    "grid":       "#DADADA",
}


def apply() -> None:
    """Apply a clean, modern rcParams theme (call once before plotting)."""
    mpl.rcParams.update({
        "figure.dpi": 120,
        "savefig.dpi": 300,
        "savefig.bbox": "tight",
        "font.size": 11,
        "font.family": "DejaVu Sans",
        "axes.titlesize": 13,
        "axes.titleweight": "bold",
        "axes.labelsize": 11,
        "axes.edgecolor": "#444444",
        "axes.linewidth": 0.8,
        "axes.grid": True,
        "axes.axisbelow": True,
        "axes.spines.top": False,
        "axes.spines.right": False,
        "grid.color": PALETTE["grid"],
        "grid.linewidth": 0.6,
        "grid.alpha": 0.8,
        "legend.frameon": False,
        "legend.fontsize": 10,
        "xtick.color": "#444444",
        "ytick.color": "#444444",
        "xtick.labelsize": 10,
        "ytick.labelsize": 10,
    })


def save(fig, out_base: str) -> None:
    """Write both a high-DPI PNG and a vector PDF."""
    fig.savefig(out_base + ".png")
    fig.savefig(out_base + ".pdf")
