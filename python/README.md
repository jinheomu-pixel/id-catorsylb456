# IDCator untuk Python (analisa)

Keempat alat order flow IDCator (Imbalance Levels, HVN Profile, Heavy Absorption, Pressure Toolkit) dalam Python, untuk menganalisa data Anda sendiri. Aturan dan setting default-nya sama dengan versi TradingView. Pada data uji yang sama, hasilnya **sama persis** dengan script TradingView: zona, label, lingkaran, bubble, titik, dan garis.

Selain sinyalnya, versi ini juga mencatat **apa yang terjadi setelah setiap sinyal**:
- apakah zona disentuh lagi;
- setelah disentuh, zona bertahan atau jebol;
- seberapa jauh harga bergerak setelah lingkaran atau bubble muncul.

Bukan sinyal buy/sell. Angka-angkanya menggambarkan masa lalu data yang Anda berikan.

Contoh `chart.png` ada di `preview.png`: data Bitcoin 1 menit simulasi, sama dengan data tes TradingView.

## Instal

Butuh Python 3.9 ke atas.

```
cd python
pip install -e ".[chart]"        # numpy, pandas, matplotlib (untuk chart.png)
```

Tanpa instal juga bisa: `pip install numpy pandas matplotlib`, lalu jalankan perintah di bawah dari folder `python`.

## Sumber data

| Data | Perintah | Isi |
|---|---|---|
| **Crypto (gratis, order flow asli)** | `--binance file.zip` | Riwayat trade Binance (aggTrades): setiap transaksi beserta sisi agresornya |
| **MetaTrader 5** | `--mt5 file.csv` | Ekspor tick dari MT5. Dari broker futures: transaksi asli. Dari CFD/forex (misalnya XAUUSD): setiap perubahan harga = 1, seperti indikator MT5 |
| **File trade sendiri** | `--trades file.csv` | Kolom `time, price, size, side` (side: buy/sell, B/S, 1/-1) |
| **Candle saja** | `--candles file.csv` | OHLCV, misalnya ekspor chart TradingView. Footprint **diperkirakan** persis seperti IDC Order Flow Lite. Tambahkan `--intrabars` (candle lebih kecil) untuk perkiraan yang lebih halus |

**Mengambil data:**
- **Binance:** `python -m idcator download-binance BTCUSDT 2026-10-20` (futures USD-M; tambah `--spot` untuk spot). Atau unduh manual dari [data.binance.vision](https://data.binance.vision) → `futures/um/daily/aggTrades`.
- **MT5:** Market Watch → klik kanan simbol → **Symbols** → tab **Ticks** → pilih tanggal → **Request** → **Export Ticks**.
- **TradingView (candle):** menu chart → **Export chart data...** (kolom `time, open, high, low, close, Volume`).

## Cara pakai

```
python -m idcator analyze --binance BTCUSDT-aggTrades-2026-10-20.zip --tick 0.1 --bar 60 --out hasil
python -m idcator analyze --mt5 XAUUSD_ticks.csv --tick 0.01 --bar 60 --out hasil
python -m idcator analyze --trades NQ_trades.csv --tick 0.25 --bar 15 --out hasil
python -m idcator analyze --candles XAUUSD_1m.csv --tick 0.001 --bar 60 --out hasil
```

**Opsi penting:**
- `--tick`: ukuran tick harga. Contoh: NQ 0.25, GC/MGC 0.1, BTCUSDT futures 0.1, XAUUSD OANDA 0.001, XAUUSD di MT5 biasanya 0.01.
- `--bar`: panjang bar dalam detik (15, 60, 300, ...).
- `--row-ticks`: tinggi satu baris footprint dalam tick. Default 0 = otomatis, sekitar 20 baris per bar seperti di MT5. Kalau Imbalance tidak menemukan zona, coba angka lebih kecil.
- `--hvn-period`: panjang profil HVN: `5` (menit), `15S`, `1H`, `D`, `W`.
- `--tools hvn,imb,ha,ptk`: alat yang dijalankan (default semua).
- `--lookahead`: jumlah bar setelah zona disentuh untuk menilai bertahan atau jebol (default 30).

**Keluaran di folder `--out`:**

| File | Isi |
|---|---|
| `summary.txt` | Ringkasan per alat dan sisi (lihat di bawah) |
| `chart.png` | 120 bar terakhir dengan semua zona dan penanda, warna gelap seperti TradingView |
| `bars.csv` | OHLCV per bar, plus buy / sell / delta footprint |
| `imbalance_zones.csv` | Zona Imbalance: bar, sisi (support/resistance), bawah/atas, rasio, volume, label, bar saat disentuh, hasilnya |
| `hvn_zones.csv` | Zona HVN: periode, bawah/atas, POC, zona utama, volume, % pembeli, label, hasilnya |
| `heavy_absorption.csv` | Lingkaran: bar, sisi, harga, volume, ambang (mean + k·σ), pergerakan sesudahnya |
| `pressure_bubbles.csv`, `pressure_dots.csv`, `pressure_lines.csv` | Bubble (kuat/lemah, big trade), titik absorption, dan garis absorption |
| `psych_levels.csv` | Level psikologis di sekitar harga terakhir |

## Arti kolom hasil analisa

**Zona (Imbalance, HVN):**
- `touched`: apakah harga kembali ke zona. `bars_to_touch`: berapa bar sampai itu terjadi.
- `role`: peran zona saat disentuh. *support* kalau harga datang dari atas, *resistance* kalau dari bawah. Zona Imbalance memakai sisinya sendiri.
- `outcome`:
  - **held**: setelah disentuh, harga menjauh sejauh tinggi zona itu sendiri;
  - **broke**: ada close di luar sisi seberang zona lebih dulu;
  - **open**: belum keduanya dalam `--lookahead` bar.
- `mfe_pct` / `mae_pct`: pergerakan terbaik dan terburuk dari tepi zona setelah disentuh, dalam % harga.

**Penanda (lingkaran, bubble, titik):**
- `fwd_1`, `fwd_5`, `fwd_10`, `fwd_20`: perubahan close setelah 1/5/10/20 bar, dalam % harga.
- `up_pct` / `down_pct`: high tertinggi dan low terendah dalam 20 bar sesudahnya, dibanding close bar penanda.

Di `summary.txt`, angka-angka ini dirata-rata per sisi. `fwd_5_up_%` = berapa persen penanda yang 5 bar kemudian harganya lebih tinggi.

## Dari kode (Jupyter / script)

```python
import idcator as idc

trades = idc.read_binance_aggtrades("BTCUSDT-aggTrades-2026-10-20.zip")
bars = idc.bars_from_trades(trades, bar_seconds=60, tick=0.1)          # footprint asli
# bars = idc.bars_from_candles(idc.read_candles("XAUUSD_1m.csv"), 60, 0.001)   # perkiraan (seperti Lite)

res = idc.analyze(bars, tick=0.1, bar_seconds=60, hvn=idc.HvnSettings(period="15"))
res.imbalance          # DataFrame zona Imbalance + hasilnya
res.hvn                # DataFrame zona HVN
res.absorption         # lingkaran Heavy Absorption
res.pressure["bubbles"], res.pressure["dots"], res.pressure["lines"]
print(res.summary_text())
res.save("hasil")
```

Setting tiap alat bisa diubah lewat `idc.ImbalanceSettings`, `idc.HvnSettings`, `idc.AbsorptionSettings`, dan `idc.PressureSettings`. Default-nya sama dengan TradingView, misalnya rasio imbalance minimum 5x, minimal 3 level bertumpuk, dan HVN threshold 70 %.

## Pengujian

```
cd python
python -m pytest
```

Ada 22 tes:
- **Sama dengan TradingView:** data simulasi NQ (bar 15 detik, baris 1 tick) dan Bitcoin (bar 1 menit, baris $5, ukuran pecahan) dijalankan di script TradingView asli (PineTS). Gambar yang dihasilkan disimpan di `tests/fixtures`. Hasil Python harus sama persis untuk keempat alat.
- **Perkiraan dari candle:** hasilnya harus sama dengan IDC Order Flow Lite, dengan candle saja maupun dengan intrabar 15 detik.
- **Lainnya:** model perkiraan, pembaca data (CSV, Binance, MT5), hasil analisa, dan perintah `python -m idcator`.

Fixture dibuat ulang dengan `cd tools/pine-test && node export.mjs`.

Belum diuji: unduhan langsung dari data.binance.vision, karena server pengujian tidak bisa mengaksesnya. Pembaca file Binance-nya sudah diuji.
