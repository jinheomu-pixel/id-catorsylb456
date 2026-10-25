"""Reading market data: trades (real order flow) or candles (for estimated footprints).

- ``read_trades``: any CSV of trades with time, price, size and aggressor side.
- ``read_binance_aggtrades`` / ``download_binance_aggtrades``: Binance's free trade history (data.binance.vision),
  real order flow for crypto: every trade with its aggressor side.
- ``read_mt5_ticks``: a tick export from MetaTrader 5 (Symbols -> Ticks -> Export). Exchange trades with buy/sell
  flags when the broker sends them, otherwise quote changes (CFD / forex: every mid-price change counts as 1, its
  direction is the side), exactly like the MT5 indicators.
- ``read_candles``: OHLCV candles, e.g. a TradingView chart export (Export chart data...).
"""
from __future__ import annotations

import io
import os
import urllib.request
import zipfile

import numpy as np
import pandas as pd

_BUY = {"buy", "b", "1", "+1", "true", "long"}
_SELL = {"sell", "s", "-1", "false", "short"}


def _side(values) -> np.ndarray:
    """+1 / -1 from buy/sell words, B/S, +1/-1 or booleans (True = buy)."""
    s = pd.Series(values)
    if pd.api.types.is_bool_dtype(s):
        return np.where(s.to_numpy(), 1, -1)
    if pd.api.types.is_numeric_dtype(s):
        return np.sign(s.to_numpy(float)).astype(int)
    low = s.astype(str).str.strip().str.lower()
    out = np.where(low.isin(_BUY), 1, np.where(low.isin(_SELL), -1, 0))
    if (out == 0).any():
        bad = sorted(set(low[out == 0]))[:5]
        raise ValueError(f"unknown side values {bad}: use buy/sell, B/S, 1/-1 or True/False")
    return out


def _time(values) -> pd.Series:
    """UTC timestamps from datetimes, ISO text or epoch numbers (seconds, ms, us or ns, guessed from the size)."""
    s = pd.Series(values)
    if pd.api.types.is_numeric_dtype(s):
        v = s.to_numpy("float64")
        mx = np.nanmax(np.abs(v)) if len(v) else 0
        unit = "s" if mx < 1e11 else "ms" if mx < 1e14 else "us" if mx < 1e17 else "ns"
        return pd.to_datetime(s.astype("int64"), unit=unit, utc=True)
    return pd.to_datetime(s, utc=True)


def read_trades(path_or_df, time: str = "time", price: str = "price", size: str = "size", side: str = "side",
                **read_csv) -> pd.DataFrame:
    """Trades from a CSV (or a DataFrame) with the given column names. Returns columns time, price, size, side
    (+1 aggressive buy, -1 aggressive sell), in time order."""
    df = path_or_df if isinstance(path_or_df, pd.DataFrame) else pd.read_csv(path_or_df, **read_csv)
    out = pd.DataFrame({"time": _time(df[time]), "price": df[price].astype(float), "size": df[size].astype(float),
                        "side": _side(df[side])})
    return out.sort_values("time", kind="stable").reset_index(drop=True)


_AGG_COLS = ["agg_trade_id", "price", "quantity", "first_trade_id", "last_trade_id", "transact_time",
             "is_buyer_maker", "is_best_match"]


def read_binance_aggtrades(path) -> pd.DataFrame:
    """Binance aggTrades CSV (or the .zip from data.binance.vision). A buyer-maker trade was an aggressive sell."""
    if str(path).endswith(".zip"):
        with zipfile.ZipFile(path) as z:
            raw = z.read(z.namelist()[0])
        src = io.BytesIO(raw)
    else:
        src = path
    head = pd.read_csv(src, nrows=1, header=None)
    if hasattr(src, "seek"):
        src.seek(0)
    has_header = not str(head.iloc[0, 0]).strip().lstrip("-").isdigit()
    df = pd.read_csv(src, header=0 if has_header else None)
    if not has_header:
        df.columns = _AGG_COLS[:df.shape[1]]
    df.columns = [c.strip().lower() for c in df.columns]
    maker = df["is_buyer_maker"]
    if not pd.api.types.is_bool_dtype(maker):
        maker = maker.astype(str).str.strip().str.lower().isin(["true", "1"])
    return read_trades(pd.DataFrame({"time": df["transact_time"], "price": df["price"], "size": df["quantity"],
                                     "side": np.where(maker, -1, 1)}))


def download_binance_aggtrades(symbol: str, day: str, market: str = "futures", dest: str = ".") -> str:
    """Downloads one day of Binance aggTrades (free, no account): ``market`` "futures" (USD-M) or "spot",
    ``day`` "YYYY-MM-DD". Returns the path of the .zip (read it with ``read_binance_aggtrades``)."""
    base = "https://data.binance.vision/data/" + ("futures/um" if market == "futures" else "spot")
    name = f"{symbol.upper()}-aggTrades-{day}.zip"
    url = f"{base}/daily/aggTrades/{symbol.upper()}/{name}"
    os.makedirs(dest, exist_ok=True)
    out = os.path.join(dest, name)
    urllib.request.urlretrieve(url, out)
    return out


# MT5 tick flags
_FLAG_LAST, _FLAG_BUY, _FLAG_SELL = 8, 32, 64


def read_mt5_ticks(path, source: str = "auto") -> pd.DataFrame:
    """A MetaTrader 5 tick export (tab-separated <DATE> <TIME> <BID> <ASK> <LAST> <VOLUME> <FLAGS>).

    ``source``: "trades" (exchange trades: price = last, side from the buy/sell flag, else from bid/ask, else from
    the last price's direction), "quotes" (every mid-price change counts as 1 at the ask when it rose, at the bid when
    it fell) or "auto" (trades when the file has any, else quotes), as in the MT5 indicators.
    """
    df = pd.read_csv(path, sep="\t")
    df.columns = [c.strip("<> ").lower() for c in df.columns]
    t = pd.to_datetime(df["date"].astype(str) + " " + df["time"].astype(str), format="%Y.%m.%d %H:%M:%S.%f",
                       errors="coerce")
    t = t.fillna(pd.to_datetime(df["date"].astype(str) + " " + df["time"].astype(str), format="%Y.%m.%d %H:%M:%S",
                                errors="coerce")).dt.tz_localize("UTC")
    bid = df.get("bid", pd.Series(np.nan, index=df.index)).astype(float).to_numpy()
    ask = df.get("ask", pd.Series(np.nan, index=df.index)).astype(float).to_numpy()
    last = df.get("last", pd.Series(np.nan, index=df.index)).astype(float).to_numpy()
    vol = df.get("volume", pd.Series(0, index=df.index)).fillna(0).astype(float).to_numpy()
    flags = df.get("flags", pd.Series(0, index=df.index)).fillna(0).astype(int).to_numpy()
    is_trade = ((flags & (_FLAG_LAST | _FLAG_BUY | _FLAG_SELL)) != 0) & (np.nan_to_num(last) > 0)
    trades = source == "trades" or (source == "auto" and is_trade.any())
    rows = []
    prev_side, prev_last, prev_mid = 0, 0.0, 0.0
    for i in range(len(df)):
        if trades:
            if not is_trade[i]:
                continue
            price = last[i]
            buy, sell = bool(flags[i] & _FLAG_BUY), bool(flags[i] & _FLAG_SELL)
            if buy and not sell:
                side = 1
            elif sell and not buy:
                side = -1
            elif ask[i] > 0 and price >= ask[i]:
                side = 1
            elif bid[i] > 0 and price <= bid[i]:
                side = -1
            else:
                side = 1 if price > prev_last else -1 if price < prev_last else prev_side
            prev_last = price
            v = vol[i]
        else:
            if not (bid[i] > 0 and ask[i] > 0):
                continue
            mid = (bid[i] + ask[i]) / 2
            if prev_mid <= 0:
                prev_mid = mid
                continue
            side = 1 if mid > prev_mid else -1 if mid < prev_mid else prev_side
            price = ask[i] if side > 0 else bid[i]
            prev_mid = mid
            v = vol[i]
        if side == 0:
            continue
        prev_side = side
        rows.append((t.iloc[i], price, v if v > 0 else 1.0, side))
    return pd.DataFrame(rows, columns=["time", "price", "size", "side"])


def read_candles(path_or_df, **read_csv) -> pd.DataFrame:
    """OHLCV candles (a TradingView chart export works: time, open, high, low, close, Volume). Returns columns
    time, open, high, low, close, volume."""
    df = path_or_df if isinstance(path_or_df, pd.DataFrame) else pd.read_csv(path_or_df, **read_csv)
    cols = {c.lower().strip(): c for c in df.columns}
    need = ["time", "open", "high", "low", "close", "volume"]
    missing = [c for c in need if c not in cols]
    if missing:
        raise ValueError(f"missing columns {missing} (found {list(df.columns)})")
    out = pd.DataFrame({c: df[cols[c]] for c in need})
    out["time"] = _time(out["time"])
    for c in need[1:]:
        out[c] = out[c].astype(float)
    return out.sort_values("time", kind="stable").reset_index(drop=True)


def candles_from_trades(trades: pd.DataFrame, bar_seconds: int) -> pd.DataFrame:
    """OHLCV candles of trades (to compare real and estimated footprints on the same data)."""
    t = trades.copy()
    t["bar"] = t["time"].dt.floor(f"{bar_seconds}s")
    g = t.groupby("bar", sort=True)
    return pd.DataFrame({"time": g["time"].first().index, "open": g["price"].first().to_numpy(),
                         "high": g["price"].max().to_numpy(), "low": g["price"].min().to_numpy(),
                         "close": g["price"].last().to_numpy(), "volume": g["size"].sum().to_numpy()})
