"""Command line:

    python -m idcator analyze --trades trades.csv --tick 0.25 --bar 60 --out out
    python -m idcator analyze --binance BTCUSDT-aggTrades-2026-10-20.zip --tick 0.1 --bar 60 --out out
    python -m idcator analyze --mt5 XAUUSD_ticks.csv --tick 0.01 --bar 60 --out out
    python -m idcator analyze --candles XAUUSD_1m.csv --tick 0.001 --bar 60 --out out     (estimated, like Lite)
    python -m idcator download-binance BTCUSDT 2026-10-20 [--spot] [--dest data]
"""
from __future__ import annotations

import argparse
import sys

from . import (HvnSettings, analyze, bars_from_candles, bars_from_trades, download_binance_aggtrades,
               read_binance_aggtrades, read_candles, read_mt5_ticks, read_trades)

TOOL_NAMES = {"hvn": "hvn", "imb": "imbalance", "imbalance": "imbalance", "ha": "absorption",
              "absorption": "absorption", "ptk": "pressure", "pressure": "pressure"}


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(prog="python -m idcator", description="IDCator order-flow tools on your own data")
    sub = ap.add_subparsers(dest="cmd", required=True)
    a = sub.add_parser("analyze", help="run the tools and write tables, a summary and a chart")
    src = a.add_mutually_exclusive_group(required=True)
    src.add_argument("--trades", help="CSV of trades: time, price, size, side (buy/sell, B/S, 1/-1)")
    src.add_argument("--binance", help="Binance aggTrades CSV or .zip (data.binance.vision)")
    src.add_argument("--mt5", help="MetaTrader 5 tick export (Symbols -> Ticks -> Export)")
    src.add_argument("--candles", help="OHLCV CSV: footprints estimated from candles (like IDC Order Flow Lite)")
    a.add_argument("--intrabars", help="with --candles: lower-timeframe OHLCV CSV for a finer estimate")
    a.add_argument("--tick", type=float, required=True,
                   help="price tick (e.g. 0.25 NQ, 0.1 BTCUSDT futures, 0.01 XAUUSD)")
    a.add_argument("--bar", type=int, default=60, help="bar length in seconds (default 60)")
    a.add_argument("--row-ticks", type=int, default=0, help="ticks per footprint row (0 = auto: about 20 rows per bar)")
    a.add_argument("--hvn-period", default="auto",
                   help="HVN profile length: auto (by bar length, as in TradingView), 5 (minutes), 15S, 1H, D, W, M")
    a.add_argument("--tools", default="hvn,imb,ha,ptk", help="comma list of hvn, imb, ha, ptk (default all)")
    a.add_argument("--lookahead", type=int, default=30, help="bars after a zone touch for held / broke (default 30)")
    a.add_argument("--out", default="idcator_out", help="output folder")
    a.add_argument("--no-chart", action="store_true")
    d = sub.add_parser("download-binance", help="download one day of Binance aggTrades (free)")
    d.add_argument("symbol")
    d.add_argument("day", help="YYYY-MM-DD")
    d.add_argument("--spot", action="store_true", help="spot market (default: USD-M futures)")
    d.add_argument("--dest", default=".")
    args = ap.parse_args(argv)

    if args.cmd == "download-binance":
        print(download_binance_aggtrades(args.symbol, args.day, "spot" if args.spot else "futures", args.dest))
        return 0

    rt = args.row_ticks or None
    if args.candles:
        intrabars = read_candles(args.intrabars) if args.intrabars else None
        bars = bars_from_candles(read_candles(args.candles), args.bar, args.tick, rt, intrabars)
    else:
        trades = (read_trades(args.trades) if args.trades else read_binance_aggtrades(args.binance) if args.binance
                  else read_mt5_ticks(args.mt5))
        bars = bars_from_trades(trades, args.bar, args.tick, rt)
    tools = [TOOL_NAMES[t.strip().lower()] for t in args.tools.split(",") if t.strip()]
    res = analyze(bars, args.tick, args.bar, tools=tools, lookahead=args.lookahead,
                  hvn=HvnSettings(period=args.hvn_period))
    for p in res.save(args.out, chart=not args.no_chart):
        print(p)
    print()
    print(res.summary_text())
    return 0


if __name__ == "__main__":
    sys.exit(main())
