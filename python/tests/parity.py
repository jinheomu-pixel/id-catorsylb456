"""Helpers of the parity tests: the TradingView drawings (PineTS fixtures) as comparable tuples."""
from __future__ import annotations

import gzip
import json
import os

import pandas as pd

FIX = os.path.join(os.path.dirname(__file__), "fixtures")


def load_json(name: str) -> dict:
    with gzip.open(os.path.join(FIX, name), "rt") as f:
        return json.load(f)


def load_csv(name: str) -> pd.DataFrame:
    return pd.read_csv(os.path.join(FIX, name))


def r(x: float) -> float:
    return round(float(x), 6)


def dashed(o: dict) -> bool:
    return "dash" in str(o.get("border_style", o.get("style", "")))


def imbalance_pine(d: dict) -> set:
    """(left, bottom, top, text, right) of the solid imbalance zones."""
    return {(o["left"], r(min(o["top"], o["bottom"])), r(max(o["top"], o["bottom"])), o.get("text", ""), o["right"])
            for o in d["boxes"] if not dashed(o) and str(o.get("text", "")).strip()}


def hvn_pine(d: dict) -> set:
    """(left, bottom, top, right, label) of the solid HVN zones; the label = number + " | P%" (two Pine labels)."""
    out = set()
    for o in d["boxes"]:
        if dashed(o) or str(o.get("text", "")).strip():
            continue
        lo, hi = min(o["top"], o["bottom"]), max(o["top"], o["bottom"])
        mid = (lo + hi) / 2
        texts = [lb["text"] for lb in d["labels"] if abs(lb["y"] - mid) < 1e-6 and lb["x"] == o["right"]]
        num = next((t.strip() for t in texts if not t.strip().startswith("|")), "")
        pct = next((t for t in texts if t.strip().startswith("|")), "")
        out.add((o["left"], r(lo), r(hi), o["right"], num + pct))
    return out


def circles_pine(d: dict) -> set:
    """(x, y, text) of the heavy absorption numbers."""
    return {(o["x"], r(o["y"]), o["text"]) for o in d["labels"] if o.get("text") not in ("●", "○", "", None)}


def pressure_pine(d: dict) -> dict:
    """Pressure Toolkit drawings: bubbles / big squares (x, y), big texts (x, y, text), dots (x, y), absorption
    lines (x1, y, x2; the psychological levels, extended both ways, are left out)."""
    lab = d["labels"]
    return {
        "bubbles": sorted((o["x"], r(o["y"])) for o in lab if o.get("text") == "●"),
        "big": sorted((o["x"], r(o["y"])) for o in lab if o.get("text") == "□"),
        "big_text": sorted((o["x"], r(o["y"]), o["text"]) for o in lab
                           if o.get("text") not in ("●", "□", "", None)),
        "dots": sorted((o["x"], r(o["y"])) for o in lab
                       if o.get("text") in ("", None) and "circle" in str(o.get("style"))),
        "lines": sorted((o["x1"], r(o["y1"]), o["x2"]) for o in d["lines"] if o.get("extend") in ("none", "n", None)),
    }
