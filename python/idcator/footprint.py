"""Footprints: per bar, the buy and sell volume of every price row.

Two sources, as in the TradingView versions:

- ``bars_from_trades``: real order flow (each trade with its aggressor side), like ``request.footprint()`` in
  IDC Order Flow. Rows are ``ticks_per_row`` ticks tall and aligned like TradingView's:
  row = floor(tick / ticks_per_row).
- ``bars_from_candles``: footprints *estimated* from candles, exactly like IDC Order Flow Lite
  (see ``estimate_candle``).
"""
from __future__ import annotations

import math
from dataclasses import dataclass

import numpy as np
import pandas as pd

#: TradingView returns no footprint for a bar that needs more rows than this.
MAX_ROWS = 2000


@dataclass
class Row:
    down: float
    up: float
    buy: float
    sell: float

    @property
    def total(self) -> float:
        return self.buy + self.sell

    @property
    def delta(self) -> float:
        return self.buy - self.sell


@dataclass
class Bar:
    index: int
    time: pd.Timestamp
    end: pd.Timestamp
    open: float
    high: float
    low: float
    close: float
    volume: float
    rows: list[Row] | None = None  # ascending in price; None = no footprint (like `na` in Pine)

    @property
    def buy(self) -> float:
        return sum(r.buy for r in self.rows) if self.rows else 0.0

    @property
    def sell(self) -> float:
        return sum(r.sell for r in self.rows) if self.rows else 0.0


def round_half_up(x: float) -> int:
    """Pine's math.round: halves round up (toward +infinity)."""
    return int(math.floor(x + 0.5))


def desc(items: list, key) -> list:
    """Largest first, ties last-first: the order of Pine's array.sort_indices(order.descending) (in PineTS)."""
    return list(reversed(sorted(items, key=key)))


def nice_step(px: float) -> float:
    """A round price step near ``px``: 1, 2, 2.5 or 5 times a power of ten."""
    if not px > 0:
        return 0.0
    mag = 10 ** math.floor(math.log10(px))
    f = px / mag
    return (1.0 if f < 1.5 else 2.0 if f < 2.25 else 2.5 if f < 3.5 else 5.0 if f < 7.5 else 10.0) * mag


def auto_ticks_per_row(high, low, tick: float) -> int:
    """About 20 rows across the median bar (of the last 200), as in the MT5 version: 1 tick on NQ 1-minute bars,
    a few dollars on Bitcoin, cents on gold."""
    r = (np.asarray(high, float) - np.asarray(low, float))[-200:]
    r = r[np.isfinite(r)]
    if len(r) < 5 or tick <= 0:
        return 1
    return max(1, round_half_up(nice_step(float(np.sort(r)[len(r) // 2]) / 20) / tick))


def _price(row: int, ticks_per_row: int, tick: float) -> float:
    return round(row * ticks_per_row * tick, 10)


def _clean(v: float) -> float:
    """A row volume without float noise (2.1199999999999997 -> 2.12): 10 decimals, as TradingView data has."""
    return math.floor(float(v) * 1e10 + 0.5) / 1e10


def _ms(t) -> np.ndarray:
    """Times as integer milliseconds since 1970 (UTC)."""
    s = pd.Series(t)
    if pd.api.types.is_numeric_dtype(s):
        return s.to_numpy(dtype="int64")
    s = pd.to_datetime(s, utc=True)
    return ((s - pd.Timestamp(0, tz="UTC")) // pd.Timedelta(milliseconds=1)).to_numpy(dtype="int64")


def _ts(ms: int) -> pd.Timestamp:
    return pd.Timestamp(int(ms), unit="ms", tz="UTC")


def bars_from_trades(trades: pd.DataFrame, bar_seconds: int, tick: float, ticks_per_row: int | None = None,
                     origin_ms: int = 0) -> list[Bar]:
    """Bars with real footprints from trades.

    ``trades``: columns ``time`` (datetime or ms), ``price``, ``size``, ``side`` (+1 aggressive buy, -1 aggressive
    sell), in time order. Bars start every ``bar_seconds`` from ``origin_ms`` (default: the epoch, like TradingView's
    intraday bars); bars without trades are skipped. ``ticks_per_row`` None = automatic.
    """
    if not len(trades):
        return []
    ms = _ms(trades["time"])
    order = np.argsort(ms, kind="stable")
    ms = ms[order]
    price = trades["price"].to_numpy(float)[order]
    size = trades["size"].to_numpy(float)[order]
    side = np.sign(trades["side"].to_numpy(float))[order]
    bar_ms = int(bar_seconds * 1000)
    bid = (ms - origin_ms) // bar_ms
    starts = np.flatnonzero(np.r_[True, bid[1:] != bid[:-1]])
    ends = np.r_[starts[1:], len(bid)]
    highs = np.maximum.reduceat(price, starts)
    lows = np.minimum.reduceat(price, starts)
    if ticks_per_row is None:
        ticks_per_row = auto_ticks_per_row(highs, lows, tick)
    ticks = np.floor(price / tick + 0.5).astype("int64")
    rows_of = np.floor_divide(ticks, ticks_per_row)
    bars: list[Bar] = []
    for i, (a, b) in enumerate(zip(starts, ends)):
        k0, k1 = int(rows_of[a:b].min()), int(rows_of[a:b].max())
        t0 = int(bid[a]) * bar_ms + origin_ms
        bar = Bar(i, _ts(t0), _ts(t0 + bar_ms), float(price[a]), float(highs[i]), float(lows[i]), float(price[b - 1]),
                  float(size[a:b].sum()))
        n = k1 - k0 + 1
        if n <= MAX_ROWS:
            # volume per price first (in trade order), then rows from the prices in order of appearance, like
            # TradingView's footprint (the same sums to the last bit, with fractional crypto sizes too)
            tk, sd, sz = ticks[a:b], side[a:b], size[a:b]
            uniq, first, inv = np.unique(tk, return_index=True, return_inverse=True)
            lvl_buy = np.zeros(len(uniq))
            lvl_sell = np.zeros(len(uniq))
            np.add.at(lvl_buy, inv[sd > 0], sz[sd > 0])
            np.add.at(lvl_sell, inv[sd < 0], sz[sd < 0])
            seq = np.argsort(first, kind="stable")
            buy = np.zeros(n)
            sell = np.zeros(n)
            r = np.floor_divide(uniq[seq], ticks_per_row) - k0
            np.add.at(buy, r, lvl_buy[seq])
            np.add.at(sell, r, lvl_sell[seq])
            bar.rows = [Row(_price(k0 + j, ticks_per_row, tick), _price(k0 + j + 1, ticks_per_row, tick),
                            _clean(buy[j]), _clean(sell[j])) for j in range(n)]
        bars.append(bar)
    return bars


# ───────────────────────────────────────────── estimated footprints (IDC Order Flow Lite)

def _est_leg(buy, sell, k0: int, rt: int, a: int, b: int, v: float) -> None:
    """One leg of a candle's path, tick ``a`` to tick ``b`` with ``v`` volume: buying going up, selling going down.
    80 % is spread along the leg; 20 % stays at its end, where price turned."""
    if b == a or not v > 0:
        return
    side = buy if b > a else sell
    n = len(side)
    lo, hi = (a + 1, b) if b > a else (b, a - 1)
    per = v * 0.8 / (hi - lo + 1)
    for k in range(lo // rt, hi // rt + 1):
        i = max(0, min(n - 1, k - k0))
        ov = min(hi, (k + 1) * rt - 1) - max(lo, k * rt) + 1
        side[i] += per * ov
    e = max(0, min(n - 1, b // rt - k0))
    side[e] += v * 0.2


def estimate_candle(buy, sell, k0: int, rt: int, o: float, h: float, l: float, c: float, v: float, tick: float) -> None:
    """Adds a candle's estimated buying and selling to the rows (row ``k0`` first, ``rt`` ticks each), the model of
    IDC Order Flow Lite:

    - the candle travels open -> low -> high -> close when it closes up (or flat), open -> high -> low -> close when
      it closes down; 90 % of its volume is spread along that path by distance (each tick travelled weighs the same);
      up legs are buying, down legs selling; each leg leaves 20 % of its share at its end, where price turned;
    - the other 10 % trades both ways around the close (its row and the rows above and below).
    """
    if not v > 0 or any(x is None or not np.isfinite(x) for x in (o, h, l, c)):
        return
    to, th, tl, tc = (round_half_up(x / tick) for x in (o, h, l, c))
    p1, p2 = (tl, th) if tc >= to else (th, tl)
    dist = abs(p1 - to) + abs(p2 - p1) + abs(tc - p2)
    n = len(buy)
    e = tc // rt - k0
    settle = v if dist == 0 else v * 0.1
    for k in (e - 1, e, e + 1):
        i = max(0, min(n - 1, k))
        buy[i] += settle / 6
        sell[i] += settle / 6
    if dist > 0:
        pv = v - settle
        _est_leg(buy, sell, k0, rt, to, p1, pv * abs(p1 - to) / dist)
        _est_leg(buy, sell, k0, rt, p1, p2, pv * abs(p2 - p1) / dist)
        _est_leg(buy, sell, k0, rt, p2, tc, pv * abs(tc - p2) / dist)


def bars_from_candles(candles: pd.DataFrame, bar_seconds: int, tick: float, ticks_per_row: int | None = None,
                      intrabars: pd.DataFrame | None = None) -> list[Bar]:
    """Bars with footprints estimated from candles (no trade data needed), like IDC Order Flow Lite.

    ``candles``: columns ``time`` (bar open), ``open``, ``high``, ``low``, ``close``, ``volume``. ``intrabars``:
    optional lower-timeframe candles (same columns); each goes to the chart bar containing its open time and the
    estimate is built from them, which is finer than from the chart bar alone.
    """
    c = candles.reset_index(drop=True)
    ms = _ms(c["time"])
    if ticks_per_row is None:
        ticks_per_row = auto_ticks_per_row(c["high"], c["low"], tick)
    rt = ticks_per_row
    groups: dict[int, list[tuple]] = {}
    if intrabars is not None and len(intrabars):
        ims = _ms(intrabars["time"])
        pos = np.searchsorted(ms, ims, side="right") - 1
        cols = [intrabars[k].to_numpy(float) for k in ("open", "high", "low", "close", "volume")]
        for j, p in enumerate(pos):
            if p >= 0 and ims[j] < ms[p] + bar_seconds * 1000:
                groups.setdefault(int(p), []).append(tuple(col[j] for col in cols))
    bars: list[Bar] = []
    for i in range(len(c)):
        o, h, l, cl, v = (float(c.loc[i, k]) for k in ("open", "high", "low", "close", "volume"))
        bar = Bar(i, _ts(ms[i]), _ts(ms[i] + bar_seconds * 1000), o, h, l, cl, v)
        k0 = round_half_up(l / tick) // rt
        k1 = round_half_up(h / tick) // rt
        n = k1 - k0 + 1
        if v > 0 and n <= MAX_ROWS:
            buy = [0.0] * n
            sell = [0.0] * n
            parts = groups.get(i)
            for (io, ih, il, ic, iv) in (parts if parts else [(o, h, l, cl, v)]):
                estimate_candle(buy, sell, k0, rt, io, ih, il, ic, iv, tick)
            bar.rows = [Row(_price(k0 + j, rt, tick), _price(k0 + j + 1, rt, tick), buy[j], sell[j]) for j in range(n)]
        bars.append(bar)
    return bars


def bars_frame(bars: list[Bar]) -> pd.DataFrame:
    """The bars as a table (OHLCV, buy / sell / delta of the footprint)."""
    return pd.DataFrame([{"bar": b.index, "time": b.time, "open": b.open, "high": b.high, "low": b.low,
                          "close": b.close, "volume": b.volume, "buy": b.buy, "sell": b.sell, "delta": b.buy - b.sell,
                          "rows": len(b.rows) if b.rows else 0} for b in bars])
