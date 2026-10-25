"""The Python tools give what the TradingView scripts draw (PineTS fixtures from tools/pine-test/export.mjs), on
simulated NQ (15-second bars, 1-tick rows) and Bitcoin (1-minute bars, $5 rows, fractional sizes) order flow, and in
the estimated mode against IDC Order Flow Lite.

The last bar is left out: in TradingView it is the forming bar (live, dashed drawings)."""
import pytest

import idcator as idc
from parity import (circles_pine, hvn_pine, imbalance_pine, load_csv, load_json, pressure_pine, r)


def _end(e, last):
    return int(e) if e is not None and e == e else last + 1


@pytest.fixture(scope="module", params=["nq15", "btc60"])
def case(request):
    fx = load_json(f"{request.param}.pine.json.gz")
    trades = idc.read_trades(load_csv(f"{request.param}.trades.csv.gz"))
    return request.param, fx, trades


def _bars(fx, trades, script):
    return idc.bars_from_trades(trades, fx["barSec"], fx["tick"], fx["scripts"][script]["ticksPerRow"])


def test_bars(case):
    _, fx, trades = case
    assert len(_bars(fx, trades, "IDC_Imbalance_Levels.pine")) == fx["bars"]


def test_imbalance(case):
    _, fx, trades = case
    d = fx["scripts"]["IDC_Imbalance_Levels.pine"]
    last = fx["bars"] - 1
    z = idc.imbalance_levels(_bars(fx, trades, "IDC_Imbalance_Levels.pine"), fx["tick"])
    py = {(int(a.bar), r(a.bottom), r(a.top), a.label, _end(a.end_bar, last)) for a in z.itertuples() if a.bar < last}
    pine = {t for t in imbalance_pine(d) if t[0] < last}
    assert len(pine) > 20
    assert py == pine


def test_hvn(case):
    _, fx, trades = case
    d = fx["scripts"]["IDC_HVN_Profile.pine"]
    last = fx["bars"] - 1
    bars = _bars(fx, trades, "IDC_HVN_Profile.pine")
    z = idc.hvn_profile(bars, fx["tick"], d["ticksPerRow"], fx["barSec"])
    py = {(int(a.start_bar), r(a.bottom), r(a.top), _end(a.end_bar, last), a.label) for a in z.itertuples()}
    pine = hvn_pine(d)
    assert len(pine) > 10
    assert py == pine


def test_heavy_absorption(case):
    _, fx, trades = case
    d = fx["scripts"]["IDC_Heavy_Absorption.pine"]
    last = fx["bars"] - 1
    z = idc.heavy_absorption(_bars(fx, trades, "IDC_Heavy_Absorption.pine"), fx["tick"])
    py = {(int(a.bar), r(a.price), a.label) for a in z.itertuples() if a.bar < last}
    pine = {t for t in circles_pine(d) if t[0] < last}
    assert len(pine) > 5
    assert py == pine


def test_pressure(case):
    _, fx, trades = case
    d = fx["scripts"]["IDC_Pressure_Toolkit.pine"]
    last = fx["bars"] - 1
    p = idc.pressure_toolkit(_bars(fx, trades, "IDC_Pressure_Toolkit.pine"), fx["tick"])
    pine = {k: [t for t in v if t[0] < last] for k, v in pressure_pine(d).items()}
    b = p["bubbles"][p["bubbles"]["bar"] < last]
    py = {"bubbles": sorted((int(x.bar), r(x.price)) for x in b.itertuples()),
          "big": sorted((int(x.bar), r(x.price)) for x in b.itertuples() if x.big),
          "big_text": sorted((int(x.bar), r(x.price), x.label) for x in b.itertuples() if x.big),
          "dots": sorted((int(x.bar), r(x.price)) for x in p["dots"].itertuples() if x.bar < last),
          "lines": sorted((int(x.bar), r(x.price), _end(x.end_bar, last)) for x in p["lines"].itertuples()
                          if x.bar < last)}
    assert len(pine["bubbles"]) > 10 and len(pine["lines"]) > 10
    assert py == pine


@pytest.mark.parametrize("intrabars", ["Chart bars only", "15 seconds"])
def test_estimated_matches_lite(intrabars):
    """Estimated footprints from candles = IDC Order Flow Lite. Lite runs the four tools in one script with shared
    drawing caps, so it may have dropped its oldest drawings: everything it shows must be in Python's result."""
    fx = load_json("lite60.pine.json.gz")
    d = fx["runs"][intrabars]
    tick, last, rt = fx["tick"], fx["bars"] - 1, d["ticksPerRow"]
    ib = idc.read_candles(load_csv("lite60.intrabars15.csv.gz")) if intrabars == "15 seconds" else None
    bars = idc.bars_from_candles(idc.read_candles(load_csv("lite60.candles.csv.gz")), 60, tick, rt, ib)
    imb = idc.imbalance_levels(bars, tick)
    py_imb = {(int(a.bar), r(a.bottom), r(a.top), a.label, _end(a.end_bar, last)) for a in imb.itertuples()
              if a.bar < last}
    pine_imb = {t for t in imbalance_pine(d) if t[0] < last}
    assert len(pine_imb) > 20 and pine_imb <= py_imb
    hvn = idc.hvn_profile(bars, tick, rt, 60)
    py_hvn = {(int(a.start_bar), r(a.bottom), r(a.top), _end(a.end_bar, last), a.label) for a in hvn.itertuples()}
    pine_hvn = hvn_pine({**d, "labels": [lb for lb in d["labels"] if "label_right" in str(lb.get("style"))]})
    assert len(pine_hvn) > 5 and pine_hvn <= py_hvn
    ha = idc.heavy_absorption(bars, tick)
    py_ha = {(int(a.bar), r(a.price), a.label) for a in ha.itertuples() if a.bar < last}
    pine_ha = {(o["x"], r(o["y"]), o["text"]) for o in d["labels"] if o["x"] < last
               and "label_center" in str(o.get("style")) and o.get("text") not in ("●", "○", "□", "", None)}
    assert pine_ha <= py_ha
    p = idc.pressure_toolkit(bars, tick)
    pp = pressure_pine({**d, "labels": [lb for lb in d["labels"] if "label_right" not in str(lb.get("style"))]})
    py_dots = {(int(x.bar), r(x.price)) for x in p["dots"].itertuples() if x.bar < last}
    py_lines = {(int(x.bar), r(x.price), _end(x.end_bar, last)) for x in p["lines"].itertuples() if x.bar < last}
    assert {t for t in pp["dots"] if t[0] < last} <= py_dots
    assert len(pp["lines"]) > 10 and {t for t in pp["lines"] if t[0] < last} <= py_lines
