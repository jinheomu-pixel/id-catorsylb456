"""Imbalance Levels: stacked diagonal bid/ask imbalances of every closed bar become support (buy stack, below the
close) and resistance (sell stack, above the close) zones. A zone lasts until price trades back into it.

Same rules and defaults as TradingView/IDC_Imbalance_Levels.pine (the source of truth).
"""
from __future__ import annotations

from dataclasses import dataclass

import pandas as pd

from .fmt import ratio_text, vol_text
from .footprint import Bar, Row


@dataclass
class ImbalanceSettings:
    min_ratio: float = 5.0          # diagonal: buy at a price vs sell one row lower (and sell vs buy one row higher)
    min_stack: int = 3              # min stacked levels
    min_level_vol: float = 0.0      # a level only counts with at least this much aggressive volume
    min_stack_vol: float = 0.0
    zero: str = "min ratio"         # opposite level traded nothing: "min ratio", "one" (ratio = volume) or "skip"
    beyond_close: bool = True       # buy stacks below the close, sell stacks above
    discard_next: bool = False      # drop a zone that the next bar touches


def level_ratio(aggressive: float, opposite: float, s: ImbalanceSettings) -> float | None:
    """Ratio of an aggressive level against its diagonal opposite, None when the level does not count."""
    if aggressive > 0 and aggressive >= s.min_level_vol:
        if opposite > 0:
            return aggressive / opposite
        if s.zero == "min ratio":
            return s.min_ratio
        if s.zero == "one":
            return aggressive
    return None


def stacks_of(rows: list[Row], close: float, tick: float, s: ImbalanceSettings) -> list[dict]:
    """Stacked diagonal imbalances of one footprint (rows ascending in price)."""
    out = []
    n = len(rows)
    if n < 2:
        return out
    thr = s.min_ratio * (1 - 1e-9)  # fractional (crypto) volumes: a ratio of exactly 5 may sum to 4.9999999
    for support in (True, False):
        run_start, run_max, run_vol = -1, 0.0, 0.0
        rng = range(1, n + 1) if support else range(0, n)
        for i in rng:
            r = None
            if support and i < n:
                r = level_ratio(rows[i].buy, rows[i - 1].sell, s)
            elif not support and i < n - 1:
                r = level_ratio(rows[i].sell, rows[i + 1].buy, s)
            if r is not None and r >= thr:
                if run_start < 0:
                    run_start, run_max, run_vol = i, r, 0.0
                run_max = max(run_max, r)
                run_vol += rows[i].buy if support else rows[i].sell
            elif run_start >= 0:
                run_end = i - 1
                bottom, top = rows[run_start].down, rows[run_end].up
                ok_side = not s.beyond_close or (top - tick * 0.5 < close if support else bottom > close + tick * 0.5)
                if run_end - run_start + 1 >= s.min_stack and run_vol >= s.min_stack_vol and ok_side:
                    out.append({"support": support, "bottom": bottom, "top": top, "ratio": run_max, "volume": run_vol})
                run_start = -1
    return out


def imbalance_levels(bars: list[Bar], tick: float, settings: ImbalanceSettings | None = None) -> pd.DataFrame:
    """All zones of the bars, one row each: ``bar`` / ``time`` (the bar that made it), ``side`` (support /
    resistance), ``bottom`` / ``top``, ``ratio`` (strongest in the stack), ``volume`` (stack volume), ``label``
    ("6.7x | 350"), ``end_bar`` / ``end_time`` (the bar that traded back into it; NaN while open)."""
    s = settings or ImbalanceSettings()
    zones: list[dict] = []
    active: list[dict] = []
    for b in bars:
        # touches first (a zone of this bar starts at its close); the highest traded price inside = top minus a tick
        still = []
        for z in active:
            if b.index > z["bar"] and b.high >= z["bottom"] - tick * 0.25 and b.low <= z["top"] - tick * 0.75:
                if s.discard_next and b.index == z["bar"] + 1:
                    z["discarded"] = True
                else:
                    z["end_bar"], z["end_time"] = b.index, b.time
                continue
            still.append(z)
        active = still
        if b.rows:
            for st in stacks_of(b.rows, b.close, tick, s):
                z = {"bar": b.index, "time": b.time, "side": "support" if st["support"] else "resistance",
                     "bottom": st["bottom"], "top": st["top"], "ratio": st["ratio"], "volume": st["volume"],
                     "label": ratio_text(st["ratio"]) + " | " + vol_text(st["volume"]),
                     "end_bar": None, "end_time": None, "discarded": False}
                zones.append(z)
                active.append(z)
    cols = ["bar", "time", "side", "bottom", "top", "ratio", "volume", "label", "end_bar", "end_time", "discarded"]
    df = pd.DataFrame([z for z in zones if not z["discarded"]], columns=cols)
    return df.drop(columns="discarded")
