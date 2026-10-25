"""Label texts as the TradingView and MT5 versions write them: 165, 1.2k, 3.4M; 6.7x."""
from __future__ import annotations

import math


def num(v: float, digits: int) -> str:
    """``v`` rounded to ``digits`` decimals (halves away from zero), trailing zeros dropped: 2.50 -> "2.5"."""
    f = 10 ** digits
    x = math.copysign(math.floor(abs(v) * f + 0.5), v) / f
    s = f"{x:.{digits}f}"
    if digits > 0:
        s = s.rstrip("0").rstrip(".")
    return "0" if s in ("-0", "") else s


def vol_text(v: float, upper_k: bool = False) -> str:
    """Volume label: 165, 1.2k, 12k, 3.4M; crypto volumes keep decimals (18.5, 2.35, 0.042)."""
    a = abs(v)
    k = "K" if upper_k else "k"
    if a >= 1e9:
        return num(v / 1e9, 1) + "B"
    if a >= 1e7:
        return num(v / 1e6, 0) + "M"
    if a >= 1e6:
        return num(v / 1e6, 1) + "M"
    if a >= 1e4:
        return num(v / 1e3, 0) + k
    if a >= 1e3:
        return num(v / 1e3, 1) + k
    if a >= 100:
        return num(v, 0)
    if a >= 10:
        return num(v, 1)
    if a >= 1:
        return num(v, 2)
    return num(v, 3)


def ratio_text(r: float) -> str:
    """Ratio label: 5x, 6.7x, 15x (999x at most)."""
    return "999x" if r >= 999 else num(r, 1) + "x"
