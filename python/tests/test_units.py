"""The estimate, the data readers, the outcomes and the command line."""
import gzip
import io
import os
import zipfile

import numpy as np
import pandas as pd
import pytest

import idcator as idc
from idcator.__main__ import main
from idcator.footprint import estimate_candle
from parity import FIX, load_csv


def test_estimate_keeps_the_volume():
    buy, sell = [0.0] * 30, [0.0] * 30
    estimate_candle(buy, sell, 99990, 1, 25000, 25005, 24999.5, 25004.5, 460, 0.25)
    assert sum(buy) + sum(sell) == pytest.approx(460)


def test_estimate_of_a_strong_up_bar():
    """Open 25000, low 24999.50, high 25005, close 25004.50: one buy stack from just above the open to just below
    the close (as LITE-02 checks in TradingView); the heaviest buying at the high, where the up leg turned."""
    c = pd.DataFrame({"time": [0], "open": [25000.0], "high": [25005.0], "low": [24999.5], "close": [25004.5],
                      "volume": [460.0]})
    bar = idc.bars_from_candles(c, 15, 0.25, 1)[0]
    z = idc.imbalance.stacks_of(bar.rows, bar.close, 0.25, idc.ImbalanceSettings())
    assert [(s["support"], s["bottom"], s["top"]) for s in z] == [(True, 25000.25, 25004.5)]
    assert max(bar.rows, key=lambda r: r.buy).down == 25005.0


def test_flat_candle_is_two_way():
    c = pd.DataFrame({"time": [0], "open": [10.0], "high": [10.0], "low": [10.0], "close": [10.0], "volume": [8.0]})
    bar = idc.bars_from_candles(c, 60, 0.5, 1)[0]
    assert len(bar.rows) == 1 and bar.rows[0].buy == bar.rows[0].sell == 4.0


def test_read_trades_sides():
    t = idc.read_trades(pd.DataFrame({"time": ["2026-10-20T10:00:00Z", "2026-10-20T10:00:01Z"], "price": [1, 2],
                                      "size": [1, 2], "side": ["Buy", "S"]}))
    assert list(t["side"]) == [1, -1]
    with pytest.raises(ValueError):
        idc.read_trades(pd.DataFrame({"time": [0], "price": [1], "size": [1], "side": ["?"]}))


@pytest.mark.parametrize("header", [True, False])
def test_read_binance_aggtrades(tmp_path, header):
    rows = "1,100000.5,0.010,1,1,1760954400000,true,true\n2,100001.0,0.200,2,3,1760954400100,false,true\n"
    text = ("agg_trade_id,price,quantity,first_trade_id,last_trade_id,transact_time,is_buyer_maker,is_best_match\n"
            if header else "") + rows
    z = tmp_path / "BTCUSDT-aggTrades-2025-10-20.zip"
    with zipfile.ZipFile(z, "w") as f:
        f.writestr("BTCUSDT-aggTrades-2025-10-20.csv", text)
    t = idc.read_binance_aggtrades(str(z))
    assert list(t["side"]) == [-1, 1] and list(t["size"]) == [0.01, 0.2]
    assert t["time"].iloc[0] == pd.Timestamp("2025-10-20 10:00:00", tz="UTC")


def test_read_mt5_ticks(tmp_path):
    head = "<DATE>\t<TIME>\t<BID>\t<ASK>\t<LAST>\t<VOLUME>\t<FLAGS>\n"
    quotes = head + ("2026.10.20\t10:00:00.000\t4000.10\t4000.30\t\t\t6\n"
                     "2026.10.20\t10:00:00.500\t4000.20\t4000.40\t\t\t6\n"
                     "2026.10.20\t10:00:01.000\t4000.00\t4000.20\t\t\t6\n")
    p = tmp_path / "q.csv"
    p.write_text(quotes)
    t = idc.read_mt5_ticks(str(p))
    assert list(t["side"]) == [1, -1] and list(t["price"]) == [4000.40, 4000.00] and list(t["size"]) == [1, 1]
    trades = head + ("2026.10.20\t10:00:00.000\t25000.00\t25000.25\t25000.25\t3\t56\n"
                     "2026.10.20\t10:00:00.200\t25000.00\t25000.25\t25000.00\t2\t88\n")
    p2 = tmp_path / "t.csv"
    p2.write_text(trades)
    t2 = idc.read_mt5_ticks(str(p2))
    assert list(t2["side"]) == [1, -1] and list(t2["size"]) == [3, 2]


def test_outcomes_and_summary():
    trades = idc.read_trades(load_csv("nq15.trades.csv.gz"))
    bars = idc.bars_from_trades(trades, 15, 0.25, 1)
    res = idc.analyze(bars, 0.25, 15)
    z = res.imbalance
    assert set(z["outcome"]) <= {"held", "broke", "open", "untouched"}
    assert (z.loc[z["touched"], "bars_to_touch"] > 0).all()
    assert {"fwd_1", "fwd_20", "up_pct", "down_pct"} <= set(res.absorption.columns)
    s = res.summaries()
    assert {"imbalance", "hvn", "absorption", "pressure_bubbles"} <= set(s)
    assert "imbalance zones" in res.summary_text()


def test_cli(tmp_path):
    src = tmp_path / "trades.csv"
    with gzip.open(os.path.join(FIX, "nq15.trades.csv.gz"), "rt") as f:
        src.write_text(f.read())
    out = tmp_path / "out"
    assert main(["analyze", "--trades", str(src), "--tick", "0.25", "--bar", "15", "--row-ticks", "1",
                 "--out", str(out)]) == 0
    for name in ("bars.csv", "imbalance_zones.csv", "hvn_zones.csv", "heavy_absorption.csv", "pressure_bubbles.csv",
                 "pressure_dots.csv", "pressure_lines.csv", "summary.txt", "chart.png"):
        assert (out / name).exists(), name
    cand = tmp_path / "candles.csv"
    with gzip.open(os.path.join(FIX, "lite60.candles.csv.gz"), "rt") as f:
        cand.write_text(f.read())
    assert main(["analyze", "--candles", str(cand), "--tick", "0.25", "--bar", "60", "--out", str(tmp_path / "o2"),
                 "--no-chart"]) == 0
    assert (tmp_path / "o2" / "imbalance_zones.csv").exists()


def test_auto_rows():
    trades = idc.read_trades(load_csv("btc60.trades.csv.gz"))
    bars = idc.bars_from_trades(trades, 60, 0.01)
    rt = round((bars[0].rows[0].up - bars[0].rows[0].down) / 0.01)
    assert 100 <= rt <= 2000
    assert np.median([len(b.rows) for b in bars]) == pytest.approx(20, abs=12)
