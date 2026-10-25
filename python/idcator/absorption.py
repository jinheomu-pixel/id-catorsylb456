"""Heavy Absorption: the price with the most aggressive buying (and selling) of each bar is compared with the heaviest
prices of the recent bars (mean + k standard deviations); a heavier one gets a circle with its volume.

Same rules and defaults as TradingView/IDC_Heavy_Absorption.pine (the source of truth).
"""
from __future__ import annotations

import math
from collections import deque
from dataclasses import dataclass

import pandas as pd

from .fmt import vol_text
from .footprint import Bar


@dataclass
class AbsorptionSettings:
    lookback: int = 100        # bars in the statistics
    k: float = 2.0             # std dev multiplier
    min_samples: int = 40      # heaviest buy + heaviest sell prices of the closed bars before marking
    min_vol: float = 0.0
    sides: str = "Both"        # "Both", "Buyers only", "Sellers only"


def row_price(r, tick: float) -> float:
    """Price of a footprint row's centre (the traded price with 1 tick per row)."""
    return r.down + (r.up - r.down - tick) / 2


def heavy_absorption(bars: list[Bar], tick: float, settings: AbsorptionSettings | None = None) -> pd.DataFrame:
    """All circles, one row each: ``bar`` / ``time``, ``side`` (buy / sell), ``price``, ``volume``, ``threshold``
    (mean + k std dev at that bar), ``label`` (the number in the circle)."""
    s = settings or AbsorptionSettings()
    win: deque = deque()
    tot_n = tot_s = tot_q = 0.0
    out = []
    for b in bars:
        mean = tot_s / tot_n if tot_n > 0 else math.nan
        sd = math.sqrt(max(0.0, tot_q / tot_n - mean * mean)) if tot_n > 0 else math.nan
        thr = mean + s.k * sd
        ready = tot_n >= s.min_samples
        if not b.rows:
            continue
        best_buy = best_sell = 0.0
        best_buy_px = best_sell_px = math.nan
        for r in b.rows:
            if r.buy > best_buy:
                best_buy, best_buy_px = r.buy, row_price(r, tick)
            if r.sell > best_sell:
                best_sell, best_sell_px = r.sell, row_price(r, tick)
        if ready:
            # strictly above the mean: a window of identical volumes (sd = 0) marks nothing
            if s.sides != "Sellers only" and best_buy >= thr and best_buy > mean and best_buy >= s.min_vol:
                out.append({"bar": b.index, "time": b.time, "side": "buy", "price": best_buy_px, "volume": best_buy,
                            "threshold": thr, "label": vol_text(best_buy, upper_k=True)})
            if s.sides != "Buyers only" and best_sell >= thr and best_sell > mean and best_sell >= s.min_vol:
                out.append({"bar": b.index, "time": b.time, "side": "sell", "price": best_sell_px,
                            "volume": best_sell, "threshold": thr, "label": vol_text(best_sell, upper_k=True)})
        # the closed bar joins the statistics
        bn = (1 if best_buy > 0 else 0) + (1 if best_sell > 0 else 0)
        bs = best_buy + best_sell
        bq = best_buy * best_buy + best_sell * best_sell
        win.append((bn, bs, bq))
        tot_n += bn
        tot_s += bs
        tot_q += bq
        while len(win) > s.lookback:
            on, os_, oq = win.popleft()
            tot_n -= on
            tot_s -= os_
            tot_q -= oq
    return pd.DataFrame(out, columns=["bar", "time", "side", "price", "volume", "threshold", "label"])
