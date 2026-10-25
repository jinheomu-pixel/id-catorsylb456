"""Pressure Toolkit: pressure bubbles (heavy one-sided prices), big trades, absorption dots (heavy volume in a
wick), absorption lines (until price returns) and psychological levels.

Same rules and defaults as TradingView/IDC_Pressure_Toolkit.pine (the source of truth).
"""
from __future__ import annotations

import math
from collections import deque
from dataclasses import dataclass

import pandas as pd

from .fmt import vol_text
from .footprint import Bar, desc, round_half_up


@dataclass
class PressureSettings:
    lookback: int = 50              # bars in the statistics
    weak_k: float = 1.0             # weak bubble: mean + k std dev of each bar's heaviest aggressive price
    strong_k: float = 2.0
    dominance: float = 1.5          # vs the opposite side at the same price
    max_per_side: int = 2           # bubbles per bar and side
    big_k: float = 3.0              # big trade
    abs_k: float = 1.0              # wick level: mean + k std dev of the volume per price
    enh_k: float = 2.5              # enhanced dot
    min_wick_pct: float = 15.0      # min wick share of the bar volume
    max_lines_per_wick: int = 8
    psych_step: float = 0.0         # 0 = auto, about 0.3 % of price


def _row_price(r, tick: float) -> float:
    return r.down + (r.up - r.down - tick) / 2


def psych_step(price: float, tick: float, step: float = 0.0) -> float:
    """Psychological level step: ``step`` or, with 0, a round step of about 0.3 % of the price."""
    result = step
    if result <= 0 and price > 0:
        target = price * 0.003
        mag = 10 ** math.floor(math.log10(target))
        result = 0.0
        for m in (1.0, 2.5, 5.0, 10.0, 25.0, 50.0, 100.0):
            st = mag * m
            ticks = st / tick
            if st >= target * 0.75 and ticks >= 1 - 1e-9 and abs(ticks - round_half_up(ticks)) < 1e-6:
                result = round_half_up(ticks) * tick
                break
        if result <= 0:
            result = max(1, math.ceil(target / tick - 1e-9)) * tick
    return result


def pressure_toolkit(bars: list[Bar], tick: float, settings: PressureSettings | None = None) -> dict[str, pd.DataFrame]:
    """The signals of every bar as tables: ``bubbles`` (bar, time, side, price, volume, strong, big, label),
    ``dots`` (bar, time, side "above" / "below" the wick, price, enhanced), ``lines`` (absorption lines: bar, time,
    side of the absorbed aggressor, price, end_bar / end_time when price came back)."""
    s = settings or PressureSettings()
    m_win: deque = deque()
    l_win: deque = deque()
    m_n = m_s = m_q = 0.0
    l_n = l_s = l_q = 0.0
    bubbles, dots, lines = [], [], []
    prev_close = math.nan
    for b in bars:
        l_mean = l_s / l_n if l_n > 0 else math.nan
        l_sd = math.sqrt(max(0.0, l_q / l_n - l_mean * l_mean)) if l_n > 0 else math.nan
        ready = m_n >= 20 and l_n >= 30
        m_mean = m_s / m_n if m_n > 0 else math.nan
        m_sd = math.sqrt(max(0.0, m_q / m_n - m_mean * m_mean)) if m_n > 0 else math.nan
        weak_thr = m_mean + s.weak_k * m_sd
        strong_thr = m_mean + s.strong_k * m_sd
        big_thr = max(m_mean + s.big_k * m_sd, weak_thr)
        wick_thr = l_mean + s.abs_k * l_sd
        enh_thr = l_mean + s.enh_k * l_sd
        rows = b.rows or []
        if b.rows and ready:
            bar_total = sum(r.total for r in rows)
            # bubbles: the heaviest qualifying prices per side
            for side in ("buy", "sell"):
                cand = []
                for r in rows:
                    v, o = (r.buy, r.sell) if side == "buy" else (r.sell, r.buy)
                    if v >= weak_thr and v > m_mean and v > o and v >= s.dominance * o:
                        cand.append((v, _row_price(r, tick)))
                cand = desc(cand, key=lambda x: x[0])[:s.max_per_side]
                for k, (v, px) in enumerate(cand):
                    big = k == 0 and v >= big_thr
                    bubbles.append({"bar": b.index, "time": b.time, "side": side, "price": px, "volume": v,
                                    "strong": v >= strong_thr, "big": big, "label": vol_text(v) if big else ""})
            # absorption: heavy volume traded in a wick (rows outside the body)
            body_top, body_bot = max(b.open, b.close), min(b.open, b.close)
            for at_high in (True, False):
                wick_vol = max_level = 0.0
                heavy = []
                for r in rows:
                    in_wick = r.down > body_top + tick * 0.5 if at_high else r.up - tick < body_bot - tick * 0.5
                    if in_wick:
                        v = r.total
                        wick_vol += v
                        max_level = max(max_level, v)
                        if v >= wick_thr:
                            heavy.append((v, _row_price(r, tick)))
                if max_level >= wick_thr and max_level > l_mean and bar_total > 0 \
                        and wick_vol / bar_total * 100 >= s.min_wick_pct:
                    dots.append({"bar": b.index, "time": b.time, "side": "above" if at_high else "below",
                                 "price": b.high if at_high else b.low, "enhanced": max_level >= enh_thr})
                    # aggressor that was absorbed: buyers at an upper wick, sellers at a lower wick
                    for v, px in desc(heavy, key=lambda x: x[0])[:s.max_lines_per_wick]:
                        lines.append({"bar": b.index, "time": b.time, "side": "buy" if at_high else "sell",
                                      "price": px, "volume": v, "end_bar": None, "end_time": None})
        # absorption lines: until price returns (gap aware)
        hi_r = max(b.high, prev_close) if not math.isnan(prev_close) else b.high
        lo_r = min(b.low, prev_close) if not math.isnan(prev_close) else b.low
        for al in lines:
            if al["end_bar"] is None and b.index > al["bar"] \
                    and lo_r - tick * 0.25 <= al["price"] <= hi_r + tick * 0.25:
                al["end_bar"], al["end_time"] = b.index, b.time
        prev_close = b.close
        # bar close: the bar joins the statistics
        if b.rows:
            n2 = s2 = q2 = 0.0
            max_b = max_s = 0.0
            for r in rows:
                t = r.total
                if t > 0:
                    n2 += 1
                    s2 += t
                    q2 += t * t
                max_b = max(max_b, r.buy)
                max_s = max(max_s, r.sell)
            n3 = (1 if max_b > 0 else 0) + (1 if max_s > 0 else 0)
            m_win.append((n3, max_b + max_s, max_b * max_b + max_s * max_s))
            m_n += n3
            m_s += max_b + max_s
            m_q += max_b * max_b + max_s * max_s
            while len(m_win) > s.lookback * 2:
                a, c, d = m_win.popleft()
                m_n -= a
                m_s -= c
                m_q -= d
            l_win.append((n2, s2, q2))
            l_n += n2
            l_s += s2
            l_q += q2
            while len(l_win) > s.lookback:
                a, c, d = l_win.popleft()
                l_n -= a
                l_s -= c
                l_q -= d
    return {
        "bubbles": pd.DataFrame(bubbles, columns=["bar", "time", "side", "price", "volume", "strong", "big", "label"]),
        "dots": pd.DataFrame(dots, columns=["bar", "time", "side", "price", "enhanced"]),
        "lines": pd.DataFrame(lines, columns=["bar", "time", "side", "price", "volume", "end_bar", "end_time"]),
    }


def psych_levels(close: float, tick: float, each_side: int = 6, major_every: int = 4,
                 step: float = 0.0) -> pd.DataFrame:
    """Psychological levels around ``close``: price and whether it is a major level."""
    st = psych_step(close, tick, step)
    if st <= 0:
        return pd.DataFrame(columns=["price", "major"])
    base = math.floor(close / st)
    return pd.DataFrame([{"price": round((base + k) * st, 10), "major": (base + k) % major_every == 0}
                         for k in range(-each_side + 1, each_side + 1)])
