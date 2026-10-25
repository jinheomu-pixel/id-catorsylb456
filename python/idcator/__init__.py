"""IDCator in Python: the four order-flow tools of the TradingView / MT5 indicators (Imbalance Levels, HVN Profile,
Heavy Absorption, Pressure Toolkit) on your own data, for analysis.

    import idcator as idc
    trades = idc.read_binance_aggtrades("BTCUSDT-aggTrades-2026-10-20.zip")
    bars = idc.bars_from_trades(trades, bar_seconds=60, tick=0.1)
    res = idc.analyze(bars, tick=0.1, bar_seconds=60)
    res.save("out")            # CSV tables + summary.txt (+ chart.png with matplotlib)
    print(res.summary_text())

Not a buy/sell signal; clean-room replica of the tools shown in ProfitPro's videos, not their code.
"""
from __future__ import annotations

import os
from dataclasses import dataclass, field

import pandas as pd

from .absorption import AbsorptionSettings, heavy_absorption
from .analysis import marker_outcomes, summary, zone_outcomes
from .data import (candles_from_trades, download_binance_aggtrades, read_binance_aggtrades, read_candles,
                   read_mt5_ticks, read_trades)
from .footprint import Bar, Row, auto_ticks_per_row, bars_frame, bars_from_candles, bars_from_trades
from .hvn import HvnSettings, hvn_profile
from .imbalance import ImbalanceSettings, imbalance_levels
from .pressure import PressureSettings, pressure_toolkit, psych_levels

__all__ = ["analyze", "Result", "Bar", "Row", "bars_from_trades", "bars_from_candles", "bars_frame",
           "auto_ticks_per_row", "read_trades", "read_binance_aggtrades", "download_binance_aggtrades",
           "read_mt5_ticks", "read_candles", "candles_from_trades", "imbalance_levels", "hvn_profile",
           "heavy_absorption", "pressure_toolkit", "psych_levels", "zone_outcomes", "marker_outcomes", "summary",
           "ImbalanceSettings", "HvnSettings", "AbsorptionSettings", "PressureSettings"]

TOOLS = ("hvn", "imbalance", "absorption", "pressure")


def _fp_ticks(bars: list[Bar], tick: float) -> int:
    for b in bars:
        if b.rows:
            return max(1, round((b.rows[0].up - b.rows[0].down) / tick))
    return 1


@dataclass
class Result:
    bars: list[Bar]
    tick: float
    imbalance: pd.DataFrame | None = None
    hvn: pd.DataFrame | None = None
    absorption: pd.DataFrame | None = None
    pressure: dict[str, pd.DataFrame] = field(default_factory=dict)
    psych: pd.DataFrame | None = None

    def summaries(self) -> dict[str, pd.DataFrame]:
        """Outcome summaries per tool and side (see idcator.analysis)."""
        out = {}
        if self.imbalance is not None and len(self.imbalance):
            out["imbalance"] = summary(self.imbalance, ["side"])
        if self.hvn is not None and len(self.hvn):
            out["hvn"] = summary(self.hvn, ["primary"])
        if self.absorption is not None and len(self.absorption):
            out["absorption"] = summary(self.absorption, ["side"])
        for k in ("bubbles", "dots"):
            if len(self.pressure.get(k, ())):
                out["pressure_" + k] = summary(self.pressure[k], ["side"])
        return out

    def summary_text(self) -> str:
        lines = [f"{len(self.bars)} bars, {self.bars[0].time} .. {self.bars[-1].end}" if self.bars else "no bars"]
        counts = {"imbalance zones": self.imbalance, "HVN zones": self.hvn, "heavy absorption circles": self.absorption,
                  "pressure bubbles": self.pressure.get("bubbles"), "absorption dots": self.pressure.get("dots"),
                  "absorption lines": self.pressure.get("lines")}
        lines += [f"{k}: {len(v)}" for k, v in counts.items() if v is not None]
        with pd.option_context("display.width", 200, "display.max_columns", 50):
            for name, t in self.summaries().items():
                lines += ["", f"== {name}", t.to_string(index=False)]
        return "\n".join(lines)

    def save(self, out_dir: str, chart: bool = True) -> list[str]:
        """Writes bars.csv, one CSV per table, summary.txt and (with matplotlib) chart.png. Returns the paths."""
        os.makedirs(out_dir, exist_ok=True)
        written = []

        def put(name, df):
            if df is not None:
                p = os.path.join(out_dir, name + ".csv")
                df.to_csv(p, index=False)
                written.append(p)

        put("bars", bars_frame(self.bars))
        put("imbalance_zones", self.imbalance)
        put("hvn_zones", self.hvn)
        put("heavy_absorption", self.absorption)
        for k, v in self.pressure.items():
            put("pressure_" + k, v)
        put("psych_levels", self.psych)
        p = os.path.join(out_dir, "summary.txt")
        with open(p, "w") as f:
            f.write(self.summary_text() + "\n")
        written.append(p)
        if chart:
            try:
                from .plot import plot
                p = os.path.join(out_dir, "chart.png")
                plot(self, p)
                written.append(p)
            except ImportError:
                pass
        return written


def analyze(bars: list[Bar], tick: float, bar_seconds: int, tools=TOOLS, lookahead: int = 30,
            imbalance: ImbalanceSettings | None = None, hvn: HvnSettings | None = None,
            absorption: AbsorptionSettings | None = None, pressure: PressureSettings | None = None) -> Result:
    """Runs the tools on the bars and adds what happened after each signal (see idcator.analysis)."""
    res = Result(bars, tick)
    if "imbalance" in tools:
        res.imbalance = zone_outcomes(imbalance_levels(bars, tick, imbalance), bars, tick, lookahead)
    if "hvn" in tools:
        res.hvn = zone_outcomes(hvn_profile(bars, tick, _fp_ticks(bars, tick), bar_seconds, hvn), bars, tick,
                                lookahead, created="created_bar")
    if "absorption" in tools:
        res.absorption = marker_outcomes(heavy_absorption(bars, tick, absorption), bars)
    if "pressure" in tools:
        p = pressure_toolkit(bars, tick, pressure)
        res.pressure = {"bubbles": marker_outcomes(p["bubbles"], bars), "dots": marker_outcomes(p["dots"], bars),
                        "lines": p["lines"]}
        if bars:
            res.psych = psych_levels(bars[-1].close, tick, step=(pressure or PressureSettings()).psych_step)
    return res
