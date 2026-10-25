"""HVN Profile: a volume profile per period (by default chosen from the bar length: about 15-25 bars per profile); its
high volume nodes become zones that last until price trades back into them. Label: POC volume | buyers or sellers share ("1.2k | 64%").

Same rules and defaults as TradingView/IDC_HVN_Profile.pine (the source of truth).
"""
from __future__ import annotations

import math
import re
from dataclasses import dataclass, field

import pandas as pd

from .fmt import vol_text
from .footprint import Bar, desc, round_half_up


@dataclass
class HvnSettings:
    period: str = "auto"            # profile length: "auto" (by bar length), "5" / "5min" (minutes), "15S", "1H",
                                    # "D", "W", "M" (month), "3M"
    row_ticks: int = 0              # profile row size in ticks, 0 = auto (target_rows rows per profile)
    target_rows: int = 60
    threshold: float = 70.0         # HVN threshold, % of the smoothed peak
    smoothing: int = 2              # 1-2-1 smoothing passes
    sensitivity: float = 25.0       # 0 keeps the whole cluster above the threshold, 100 shrinks it to its peak rows
    min_rows: int = 2
    gap: int = 1                    # gap tolerance (rows)
    max_clusters: int = 3           # per period
    grace: int = 0                  # bars after the period before a touch counts
    label_value: str = "POC volume"  # or "Cluster volume"
    tz: str = "UTC"                 # day / week / month boundaries of "D", "W" and "M" periods


def auto_period(bar_seconds: int) -> str:
    """The profile timeframe of 'Auto profile timeframe' (TradingView): about 15-25 bars per profile."""
    for limit, tf in ((15, "5"), (30, "10"), (60, "15"), (180, "60"), (300, "120"), (900, "240"), (3600, "D"),
                      (14400, "W"), (86400, "M"), (604800, "3M")):
        if bar_seconds <= limit:
            return tf
    return "12M"


def parse_period(period: str) -> tuple[str, int]:
    """(unit, count) of a period: "5" / "5min" -> ("s", 300), "15S" -> ("s", 15), "1H" -> ("s", 3600),
    "D" -> ("D", 1), "W" -> ("W", 1), "3M" -> ("M", 3) (uppercase M = months, as in TradingView)."""
    m = re.fullmatch(r"\s*(\d*)\s*(S|s|sec|min|m|H|h|D|d|W|w|M)?\s*", str(period))
    if not m:
        raise ValueError(f"unknown period {period!r}")
    n = int(m.group(1) or 1)
    unit = m.group(2) or "min"
    if unit == "M":
        return "M", n
    if unit in ("D", "d"):
        return "D", n
    if unit in ("W", "w"):
        return "W", n
    return "s", n * {"S": 1, "s": 1, "sec": 1, "min": 60, "m": 60, "H": 3600, "h": 3600}[unit]


def period_seconds(period: str) -> int:
    """Length of a period in seconds (a month counts as 30 days)."""
    unit, n = parse_period(period)
    return n * {"s": 1, "D": 86400, "W": 604800, "M": 2592000}[unit]


@dataclass
class _Period:
    key: int
    start_bar: int
    end_bar: int
    start_time: pd.Timestamp
    end_time: pd.Timestamp
    low: int = 2147483647
    high: int = -2147483647
    vol: dict = field(default_factory=dict)
    buy: dict = field(default_factory=dict)
    sell: dict = field(default_factory=dict)


def _row_size(lo: int, hi: int, fp_ticks: int, s: HvnSettings) -> int:
    raw = s.row_ticks if s.row_ticks > 0 else max(1, math.ceil((hi - lo + 1) / max(5.0, s.target_rows)))
    return math.ceil(raw / fp_ticks) * fp_ticks


def analyze(p: _Period, fp_ticks: int, s: HvnSettings) -> tuple[list[dict], int]:
    """HVN clusters of a period, largest volume first, and the profile row size (ticks)."""
    result: list[dict] = []
    rt = _row_size(p.low, p.high, fp_ticks, s)
    if not p.vol or p.high < p.low:
        return result, rt
    lo = p.low
    n = (p.high - lo) // rt + 1
    vol, buy, sell = [0.0] * n, [0.0] * n, [0.0] * n
    for k, v in p.vol.items():
        vol[(k - lo) // rt] += v
    for k in p.vol:
        r = (k - lo) // rt
        buy[r] += p.buy.get(k, 0.0)
        sell[r] += p.sell.get(k, 0.0)
    sm = vol[:]
    for _ in range(s.smoothing):
        nxt = [0.0] * n
        for r in range(n):
            t, w = 2 * sm[r], 2.0
            if r > 0:
                t += sm[r - 1]
                w += 1
            if r < n - 1:
                t += sm[r + 1]
                w += 1
            nxt[r] = t / w
        sm = nxt
    thr = max(sm) * s.threshold / 100
    row = 0
    while row < n:
        if sm[row] < thr:
            row += 1
            continue
        # grow the cluster, bridging gaps of up to `gap` rows
        first = last = row
        gap = 0
        r2 = row + 1
        while r2 < n:
            if sm[r2] >= thr:
                last, gap = r2, 0
            else:
                gap += 1
                if gap > s.gap:
                    break
            r2 += 1
        row = last + 1
        # trim weak edges relative to the cluster's own peak, then regrow to the minimum size
        peak = max(sm[first:last + 1])
        edge = thr + (peak - thr) * s.sensitivity / 100
        u_first, u_last = first, last
        while first < last and sm[first] < edge:
            first += 1
        while last > first and sm[last] < edge:
            last -= 1
        while last - first + 1 < s.min_rows and (first > u_first or last < u_last):
            if first > u_first and (last >= u_last or sm[first - 1] >= sm[last + 1]):
                first -= 1
            else:
                last += 1
        if last - first + 1 >= s.min_rows:
            c = {"first": first, "last": last, "vol": 0.0, "buy": 0.0, "sell": 0.0, "poc_tick": lo + first * rt,
                 "poc_vol": 0.0}
            for r in range(first, last + 1):
                c["vol"] += vol[r]
                c["buy"] += buy[r]
                c["sell"] += sell[r]
            # POC: busiest single price inside the cluster
            for t in range(lo + first * rt, lo + (last + 1) * rt):
                v = p.vol.get(t, 0.0)
                if v > c["poc_vol"]:
                    c["poc_vol"], c["poc_tick"] = v, t
            result.append(c)
    if len(result) > 1:
        result = desc(result, key=lambda c: c["vol"])[:s.max_clusters]
    return result, rt


def _labels(c: dict, s: HvnSettings) -> tuple[str, str, float]:
    total = c["buy"] + c["sell"]
    share = c["buy"] / total if total > 0 else 0.5
    buyers = share >= 0.5
    pct = max(51, round_half_up(100 * (share if buyers else 1 - share)))
    numv = vol_text(c["poc_vol"] if s.label_value == "POC volume" else c["vol"])
    return numv, (f" | {pct}%" if total > 0 else ""), share


def hvn_profile(bars: list[Bar], tick: float, fp_ticks: int, bar_seconds: int,
                settings: HvnSettings | None = None) -> pd.DataFrame:
    """All HVN zones, one row each: the period (``period_start`` .. ``period_end``, bars ``start_bar`` ..
    ``period_end_bar``), ``created_bar``, ``bottom`` / ``top`` / ``mid``, ``poc`` price, ``primary`` (largest node of
    its period), ``volume``, ``poc_volume``, ``buy_share``, ``label`` ("1.2k | 64%"), ``end_bar`` / ``end_time`` (the
    bar that traded back into it). ``fp_ticks``: ticks per footprint row of the bars."""
    s = settings or HvnSettings()
    period = auto_period(bar_seconds) if s.period == "auto" else s.period
    unit, count = parse_period(period)
    psec = period_seconds(period)
    if bar_seconds >= psec:
        raise ValueError("the profile period must be longer than the bars")
    pms = psec * 1000

    def key_of(t: pd.Timestamp) -> int:
        if unit == "s":
            ms = t.value // 1_000_000
            return ms - ms % pms
        d = t.tz_convert(s.tz).normalize()
        if unit == "W":
            d = d - pd.Timedelta(days=d.weekday())
        elif unit == "M":
            idx = (d.year * 12 + d.month - 1) // count * count
            d = pd.Timestamp(year=idx // 12, month=idx % 12 + 1, day=1, tz=s.tz)
        return d.value // 1_000_000

    zones: list[dict] = []
    active: list[dict] = []
    cur: _Period | None = None

    def finalize(p: _Period, bar: Bar) -> None:
        clusters, rt = analyze(p, fp_ticks, s)
        for i, c in enumerate(clusters):
            bottom = round((p.low + c["first"] * rt) * tick, 10)
            # a partly traded top row ends at the period's highest price
            top = round(min(p.low + (c["last"] + 1) * rt, p.high + fp_ticks) * tick, 10)
            numv, pct, share = _labels(c, s)
            z = {"period_start": p.start_time, "period_end": p.end_time, "start_bar": p.start_bar,
                 "period_end_bar": p.end_bar, "created_bar": bar.index, "bottom": bottom, "top": top,
                 "mid": (bottom + top) / 2, "poc": round((c["poc_tick"] + fp_ticks / 2) * tick, 10), "primary": i == 0,
                 "volume": c["vol"], "poc_volume": c["poc_vol"], "buy_share": share, "label": numv + pct,
                 "end_bar": None, "end_time": None}
            zones.append(z)
            active.append(z)

    for b in bars:
        k = key_of(b.time)
        # a new period started: the previous one is complete (if it was not finalized at its last bar already)
        if cur is not None and k != cur.key:
            finalize(cur, b)
            cur = None
        if cur is None:
            cur = _Period(k, b.index, b.index, b.time, b.end)
        # touches (the highest traded price inside a zone = its top minus a tick)
        still = []
        for z in active:
            if b.index > z["period_end_bar"] + s.grace and b.high >= z["bottom"] - tick * 0.25 \
                    and b.low <= z["top"] - tick * 0.75:
                z["end_bar"], z["end_time"] = b.index, b.time
                continue
            still.append(z)
        active = still
        # the closed bar joins its period; an intraday period ending with this bar closes now
        if b.rows:
            for r in b.rows:
                v = r.total
                if v > 0:
                    t = round_half_up(r.down / tick)
                    cur.vol[t] = cur.vol.get(t, 0.0) + v
                    cur.buy[t] = cur.buy.get(t, 0.0) + r.buy
                    cur.sell[t] = cur.sell.get(t, 0.0) + r.sell
                    cur.low = min(cur.low, t)
                    cur.high = max(cur.high, t)
        cur.end_bar, cur.end_time = b.index, b.end
        if unit == "s" and psec < 86400 and b.end.value // 1_000_000 >= cur.key + pms:
            finalize(cur, b)
            cur = None
    return pd.DataFrame(zones, columns=["period_start", "period_end", "start_bar", "period_end_bar", "created_bar",
                                        "bottom", "top", "mid", "poc", "primary", "volume", "poc_volume", "buy_share",
                                        "label", "end_bar", "end_time"])
