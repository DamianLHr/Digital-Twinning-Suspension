"""Read the CSVs written by Unity's RunDataRecorder.

Each file has a `#`-prefixed metadata header (`# key=value`) followed by a normal
CSV table. `read_csv_with_meta` returns (meta_dict, DataFrame).
"""
from __future__ import annotations

import os
from io import StringIO

import pandas as pd


def read_csv_with_meta(path: str):
    """Return (meta: dict[str, str], df: pandas.DataFrame) for a recorder CSV."""
    if not os.path.exists(path):
        raise FileNotFoundError(
            f"{path} not found. The recorder writes to Unity's persistentDataPath — "
            f"copy the CSVs next to these scripts or pass --data <folder>."
        )
    meta: dict[str, str] = {}
    rows: list[str] = []
    with open(path, "r", encoding="utf-8") as f:
        for line in f:
            if line.startswith("#"):
                body = line[1:].strip()
                if "=" in body:
                    k, v = body.split("=", 1)
                    meta[k.strip()] = v.strip()
            elif line.strip():
                rows.append(line)
    df = pd.read_csv(StringIO("".join(rows)))
    return meta, df


def timeseries(folder: str, run: str):
    return read_csv_with_meta(os.path.join(folder, f"{run}_timeseries.csv"))


def bumps(folder: str, run: str):
    return read_csv_with_meta(os.path.join(folder, f"{run}_bumps.csv"))
