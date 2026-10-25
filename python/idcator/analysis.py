"""What happened after the signals: for checking the tools on your own data.

- ``zone_outcomes``: for zones (Imbalance Levels, HVN Profile): was the zone touched, how long it took, and after the
  touch whether it held (price moved away from it by its own height first) or broke (a close beyond its far edge
  first), with the best and worst excursion.
- ``marker_outcomes``: for points (Heavy Absorption circles, Pressure bubbles and dots): the close-to-close move after
  1, 5, 10, 20 bars and the best / worst excursion.

Moves are in % of price, so they compare across instruments (NQ, gold, any coin).
- ``summary``: the same, averaged per group (tool, side).

Not trading advice: the numbers describe the past of the data you give.
"""
from __future__ import annotations

import numpy as np
import pandas as pd

from .footprint import Bar


def _arrays(bars: list[Bar]):
    return (np.array([b.high for b in bars]), np.array([b.low for b in bars]), np.array([b.close for b in bars]))


def zone_outcomes(zones: pd.DataFrame, bars: list[Bar], tick: float, lookahead: int = 30,
                  created: str = "bar") -> pd.DataFrame:
    """Adds to each zone: ``touched``, ``bars_to_touch``, ``role`` (support / resistance at the touch: the side
    price came from), ``outcome`` (held / broke / open within ``lookahead`` bars after the touch), ``mfe_pct`` /
    ``mae_pct`` (best / worst move from the zone edge after the touch, % of price). ``created``: the column with the
    bar the zone became active (``bar`` for imbalance zones, ``created_bar`` for HVN zones)."""
    hi, lo, cl = _arrays(bars)
    n = len(bars)
    out = zones.copy()
    cols = {k: [] for k in ("touched", "bars_to_touch", "role", "outcome", "mfe_pct", "mae_pct")}
    for _, z in zones.iterrows():
        e = z.get("end_bar")
        if e is None or pd.isna(e):
            for k, v in (("touched", False), ("bars_to_touch", np.nan), ("role", ""), ("outcome", "untouched"),
                         ("mfe_pct", np.nan), ("mae_pct", np.nan)):
                cols[k].append(v)
            continue
        e = int(e)
        bottom, top = float(z["bottom"]), float(z["top"])
        if "side" in z and z["side"] in ("support", "resistance"):
            role = z["side"]
        else:
            prev = cl[e - 1] if e > 0 else cl[e]
            role = "support" if prev >= top else "resistance" if prev <= bottom else "inside"
        height = max(top - bottom, tick)
        outcome = "open"
        end = min(n, e + 1 + lookahead)
        for i in range(e, end):
            if role == "support":
                if cl[i] < bottom - tick * 0.5:
                    outcome = "broke"
                    break
                if hi[i] >= top + height:
                    outcome = "held"
                    break
            elif role == "resistance":
                if cl[i] > top + tick * 0.5:
                    outcome = "broke"
                    break
                if lo[i] <= bottom - height:
                    outcome = "held"
                    break
        seg_hi, seg_lo = hi[e:end].max(), lo[e:end].min()
        ref = (bottom + top) / 2 / 100
        if role == "resistance":
            mfe, mae = (bottom - seg_lo) / ref, (seg_hi - top) / ref
        else:
            mfe, mae = (seg_hi - top) / ref, (bottom - seg_lo) / ref
        cols["touched"].append(True)
        cols["bars_to_touch"].append(e - int(z[created]))
        cols["role"].append(role)
        cols["outcome"].append(outcome)
        cols["mfe_pct"].append(round(mfe, 6))
        cols["mae_pct"].append(round(mae, 6))
    for k, v in cols.items():
        out[k] = v
    return out


def marker_outcomes(markers: pd.DataFrame, bars: list[Bar], horizons: tuple[int, ...] = (1, 5, 10, 20)) -> pd.DataFrame:
    """Adds to each marker the close-to-close move after each horizon (``fwd_<h>``, % of price; NaN past the data)
    and the best / worst excursion up to the longest horizon (``up_pct`` = highest high above the close of the
    marker's bar, ``down_pct`` = lowest low below it, % of price)."""
    hi, lo, cl = _arrays(bars)
    n = len(bars)
    out = markers.copy()
    hmax = max(horizons)
    for h in horizons:
        out[f"fwd_{h}"] = [100 * (cl[b + h] - cl[b]) / cl[b] if b + h < n else np.nan
                           for b in markers["bar"].astype(int)]
    up, down = [], []
    for b in markers["bar"].astype(int):
        seg = slice(b + 1, min(n, b + 1 + hmax))
        up.append(100 * (hi[seg].max() - cl[b]) / cl[b] if b + 1 < n else np.nan)
        down.append(100 * (cl[b] - lo[seg].min()) / cl[b] if b + 1 < n else np.nan)
    out["up_pct"] = up
    out["down_pct"] = down
    return out


def summary(df: pd.DataFrame, by: list[str]) -> pd.DataFrame:
    """Counts and means per group: zones (touch, held / broke shares, excursions) or markers (forward moves)."""
    if df.empty:
        return pd.DataFrame()
    g = df.groupby(by, dropna=False)
    res = pd.DataFrame({"count": g.size()})
    if "outcome" in df:
        touched = df["touched"].astype(bool)
        res["touched_%"] = g["touched"].mean().mul(100).round(1)
        res["bars_to_touch"] = g["bars_to_touch"].median()
        res["held_%_of_touched"] = df[touched].groupby(by, dropna=False)["outcome"].apply(
            lambda s: round(100 * (s == "held").mean(), 1))
        res["broke_%_of_touched"] = df[touched].groupby(by, dropna=False)["outcome"].apply(
            lambda s: round(100 * (s == "broke").mean(), 1))
        res["mfe_pct"] = g["mfe_pct"].mean().round(3)
        res["mae_pct"] = g["mae_pct"].mean().round(3)
    for c in [c for c in df.columns if c.startswith("fwd_")] + [c for c in ("up_pct", "down_pct") if c in df]:
        res[c] = g[c].mean().round(3)
        if c.startswith("fwd_"):
            res[c + "_up_%"] = g[c].apply(
                lambda s: round(100 * (s.dropna() > 0).mean(), 1) if s.notna().any() else np.nan)
    return res.reset_index()
