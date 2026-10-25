"""A chart of the result in TradingView's dark colors (needs matplotlib): candles, imbalance and HVN zones,
heavy absorption circles, pressure bubbles, absorption dots and lines."""
from __future__ import annotations

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt  # noqa: E402
from matplotlib.patches import Rectangle  # noqa: E402

BG, GRID, TXT = "#131722", "#2a2e39", "#d1d4dc"
UP, DOWN = "#26a69a", "#ef5350"
SUP, RES = "#22ab5c", "#e9393d"
PRIMARY, OTHER = "#ffa500", "#6e7da0"


def plot(res, path: str, last: int = 120, title: str = "") -> None:
    """Draws the last ``last`` bars of a Result (idcator.analyze) to ``path`` (PNG)."""
    bars = res.bars[-last:]
    if not bars:
        return
    x0, x1 = bars[0].index, bars[-1].index
    lo = min(b.low for b in bars)
    hi = max(b.high for b in bars)
    pad = (hi - lo) * 0.05 or res.tick * 5
    fig, ax = plt.subplots(figsize=(16, 9), dpi=100)
    fig.patch.set_facecolor(BG)
    ax.set_facecolor(BG)
    ax.grid(color=GRID, linewidth=0.6)
    ax.tick_params(colors=TXT)
    for s in ax.spines.values():
        s.set_color(GRID)
    ax.set_xlim(x0 - 1, x1 + 6)
    ax.set_ylim(lo - pad, hi + pad)

    def zone(left, right, bottom, top, color, alpha, text, text_color=TXT):
        left, right = max(left, x0 - 1), right if right is not None else x1 + 1
        if right < x0 - 1 or top < lo - pad or bottom > hi + pad:
            return
        ax.add_patch(Rectangle((left, bottom), right - left, top - bottom, facecolor=color, alpha=alpha,
                               edgecolor="none", zorder=1))
        if text:
            ax.text(right, (bottom + top) / 2, text + " ", color=text_color, fontsize=7, ha="right", va="center",
                    fontweight="bold", zorder=5)

    if res.hvn is not None:
        for _, z in res.hvn.iterrows():
            end = z["end_bar"] if z["end_bar"] is not None and z["end_bar"] == z["end_bar"] else None
            zone(z["start_bar"], end, z["bottom"], z["top"], PRIMARY if z["primary"] else OTHER, 0.22, z["label"])
    if res.imbalance is not None:
        for _, z in res.imbalance.iterrows():
            end = z["end_bar"] if z["end_bar"] is not None and z["end_bar"] == z["end_bar"] else None
            zone(z["bar"], end, z["bottom"], z["top"], SUP if z["side"] == "support" else RES, 0.3, z["label"])
    width = 0.6
    for b in bars:
        c = UP if b.close >= b.open else DOWN
        ax.plot([b.index, b.index], [b.low, b.high], color=c, linewidth=1, zorder=3)
        ax.add_patch(Rectangle((b.index - width / 2, min(b.open, b.close)), width,
                               max(abs(b.close - b.open), res.tick * 0.2), color=c, zorder=4))
    p = res.pressure or {}
    if p.get("lines") is not None:
        for _, l in p["lines"].iterrows():
            if l["bar"] < x0 - 1:
                continue
            end = l["end_bar"] if l["end_bar"] is not None and l["end_bar"] == l["end_bar"] else x1 + 1
            ax.plot([l["bar"], end], [l["price"], l["price"]], color=UP if l["side"] == "buy" else DOWN,
                    linewidth=0.8, alpha=0.8, zorder=2)
    if p.get("bubbles") is not None:
        bb = p["bubbles"][p["bubbles"]["bar"] >= x0]
        for _, m in bb.iterrows():
            ax.scatter(m["bar"], m["price"], s=60 if m["strong"] else 30, color=UP if m["side"] == "buy" else DOWN,
                       alpha=0.75 if m["strong"] else 0.35, zorder=6)
            if m["big"]:
                ax.scatter(m["bar"], m["price"], s=90, marker="s", facecolors="none",
                           edgecolors="#78e678" if m["side"] == "buy" else "#f06e64", zorder=7)
    if p.get("dots") is not None:
        dd = p["dots"][p["dots"]["bar"] >= x0]
        for _, d in dd.iterrows():
            off = pad * 0.25 * (1 if d["side"] == "above" else -1)
            ax.scatter(d["bar"], d["price"] + off, s=28 if d["enhanced"] else 14,
                       color="#c62828" if d["side"] == "above" else "#32cd32", zorder=6)
    if res.absorption is not None:
        aa = res.absorption[res.absorption["bar"] >= x0]
        vmax = max(aa["volume"].max(), 1e-9) if len(aa) else 1
        for _, m in aa.iterrows():
            size = 150 + 650 * (m["volume"] / vmax) ** 0.5
            c = SUP if m["side"] == "buy" else RES
            ax.scatter(m["bar"], m["price"], s=size, color=c, alpha=0.35, zorder=8)
            ax.scatter(m["bar"], m["price"], s=size, facecolors="none", edgecolors=c, linewidths=2, zorder=8)
            ax.text(m["bar"], m["price"], m["label"], color="white", fontsize=7, ha="center", va="center",
                    fontweight="bold", zorder=9)
    ticks = list(range(x0, x1 + 1, max(1, (x1 - x0) // 8)))
    ax.set_xticks(ticks)
    ax.set_xticklabels([res.bars[i].time.strftime("%m-%d %H:%M") for i in ticks], color=TXT, fontsize=8)
    ax.yaxis.tick_right()
    ax.set_title(title or "IDCator (Python)", color=TXT, loc="left")
    fig.tight_layout()
    fig.savefig(path, facecolor=BG)
    plt.close(fig)
