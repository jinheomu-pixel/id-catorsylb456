# IDCator untuk TradingView (Pine Script v6)

Empat indikator order-flow seperti di video ProfitPro, dibuat untuk TradingView. Datanya dari **footprint asli TradingView**, yaitu volume beli dan jual agresif di setiap harga (`request.footprint`). Ini bukan alat sinyal buy/sell.

| File | Nama di chart | Isi |
|---|---|---|
| `IDC_Imbalance_Levels.pine` | IDC Imbalance | Zona hijau/merah dari stacked imbalance diagonal, label `12.5x \| 165`. Zona memanjang sampai disentuh harga |
| `IDC_HVN_Profile.pine` | IDC HVN | Zona High Volume Node per periode (default 5 menit). Zona terbesar oranye, lainnya biru-abu. Label `124 \| 65%` |
| `IDC_Heavy_Absorption.pine` | IDC Heavy Abs | Lingkaran hijau/merah berisi angka volume di harga terberat. Angkanya terus naik di bar yang sedang berjalan |
| `IDC_Pressure_Toolkit.pine` | IDC Pressure | Bubble redup/terang, kotak big trade + angka, titik absorption di wick, garis absorption, dan level psikologis |

Contoh tampilan pada data NQ 15 detik simulasi ada di `preview.png` dan `preview-ptk.png`. Contoh untuk Bitcoin 1 menit ada di `preview-crypto.png`.

## Syarat

1. **Paket TradingView Premium atau Ultimate.** `request.footprint()` hanya jalan di paket ini; di paket lain script langsung error. Chart detik (15s) juga butuh paket ini.
2. **Data real-time CME** untuk NQ/ES (langganan data CME di TradingView). Tanpa itu datanya delay 10 menit.
3. Simbol yang punya volume, misalnya `CME_MINI:NQ1!`, `CME_MINI:ES1!`, `COMEX:GC1!`, atau crypto dari bursa (lihat bagian Crypto).

FundedNext Futures resmi mendukung TradingView sebagai platform trading. Jadi chart dengan indikator ini bisa dipakai langsung di akun FundedNext (Trading Panel → login akun futures). Pastikan sesuai aturan akun masing-masing.

## Cara pasang (untuk setiap file)

1. Buka chart di TradingView, klik **Pine Editor** di bawah.
2. Buka menu nama script di kiri atas Pine Editor, pilih **Create new → Indicator**, lalu hapus semua isinya.
3. Buka file `.pine`, salin **semua** isinya, lalu tempel di Pine Editor.
4. Klik **Save** (beri nama, misalnya "IDC Imbalance"), lalu **Add to chart**.
5. Ulangi untuk keempat file. Setelah tersimpan, indikator bisa dicari di **Indicators → My scripts**.

## Setup seperti di video

- Chart **NQ1!**, timeframe **15 detik** (boleh juga 30 detik atau 1 menit).
- Pasang **IDC HVN** (Profile timeframe 5), **IDC Imbalance**, dan **IDC Heavy Abs** bersamaan.
- Klik kanan di skala harga, pilih **Settings → Symbol**, lalu matikan *Wick*/*Border* kalau ingin tampilan candle setipis di video (opsional).

## Crypto

Keempat indikator bisa dipakai di semua pair crypto yang punya data volume di TradingView. Pakai simbol dari bursa, misalnya `BINANCE:BTCUSDT`, `BINANCE:ETHUSDT.P`, `BYBIT:SOLUSDT`, `OKX:DOGEUSDT`, atau `COINBASE:BTCUSD`.

- **Ukuran baris otomatis.** Setting *Ticks per row* = 0 (default) memilih sendiri ukuran baris footprint. Di futures dan saham ukurannya 1 tick. Di crypto, tick-nya sangat kecil dibanding harganya (BTC bergerak per 0.01 di harga sekitar 100.000), jadi indikator memakai sekitar 20 baris per bar:

  | Pair | Timeframe | Baris |
  |---|---|---|
  | BTC/USDT | 15 detik | $2 |
  | BTC/USDT | 1 menit | $5 |
  | ETH/USDT | 1 menit | $0.10 |
  | SOL, XRP, DOGE, dan sejenisnya | 1 menit | 1 tick |

  Nilai yang sedang dipakai terlihat di **Data Window** (*Ticks per row*). Di HVN Profile namanya *Footprint ticks per row*.
- **Kalau barisnya kurang pas**, pojok kanan atas chart menampilkan saran, misalnya `rows look too fine for this chart - try 'Ticks per row' = 500`. Isi angka itu di setting. Ini bisa terjadi pada koin yang jarang atau koin yang harganya sudah jauh berubah.
- TradingView tidak mengirim footprint untuk bar dengan lebih dari 2000 baris. Indikator mendeteksinya dan menampilkan *too many price rows ... set 'Ticks per row' to N*.
- **Angka volume dalam satuan koin**, dengan desimal untuk angka kecil: `12.5x | 0.39`, lingkaran `2.24`. Filter seperti *Min volume per level* dan *Min stack volume* juga dalam satuan koin.
- Futures crypto CME (`CME:BTC1!`, `CME:MBT1!`, `CME:ETH1!`) adalah futures, jadi barisnya 1 tick (tick-nya sudah besar, misalnya $5).
- Kalau muncul *no footprint data* di pair yang punya volume, cek dulu paket TradingView (Premium/Ultimate). Lalu coba simbol bursa lain.

## Alert

Klik ikon jam alarm, pilih **Condition** = nama indikator, lalu **Any alert() function call**. Atur sumber alert di setting tiap indikator:

- Imbalance dan HVN: saat harga menyentuh zona (setiap zona baru yang tersentuh, juga kalau beberapa zona tersentuh di bar yang sama).
- Heavy Absorption: saat bar close.
- Pressure Toolkit: per jenis sinyal.

## Realtime

TradingView menjalankan ulang script di **setiap tick**, jadi semua indikator ini ikut update live:

- **Imbalance:** zona berhenti di tick yang menyentuhnya. Zona baru muncul saat bar close, seperti aslinya.
- **Heavy Absorption:** lingkaran muncul di harga terberat, lalu angkanya naik setiap ada transaksi di harga itu.
- **HVN:** zona sebuah periode muncul begitu bar terakhir periode itu close, tidak menunggu bar berikutnya.
- **Pressure Toolkit:** bubble dan titik di bar yang sedang berjalan ikut berubah. Garis absorption dibuat saat bar close.

## Perbedaan dengan versi ATAS/NinjaTrader

- **Big print:** TradingView tidak memberi script daftar transaksi satu per satu, hanya volume beli/jual per harga. Heavy Absorption dan big trade di Pressure Toolkit memakai harga dengan volume agresif terbesar di bar itu. Hasilnya dibandingkan dengan harga terberat bar-bar sebelumnya (rata-rata + k × standar deviasi). Angka di lingkaran tetap sama dengan aslinya: volume agresif di harga itu.
- **Batas objek:** setiap script maksimal 500 kotak/label/garis. Objek paling lama (yang sudah tersentuh lebih dulu) dihapus otomatis.
- **Font:** bentuk lingkaran dan font memakai font TradingView. Ukurannya bisa diatur di setting (*Min/Max circle size*, *Label size*).

## Pengujian

Script diuji dengan [PineTS](https://github.com/LuxAlgo/PineTS) (runtime Pine Script open-source dari LuxAlgo) memakai data order-flow buatan. Tesnya mencakup:
- hasil per indikator (label, warna, posisi zona);
- update tick-per-tick (zona berhenti di tick yang menyentuh, angka lingkaran naik live);
- crypto: ukuran baris otomatis per koin dan timeframe, sesi Bitcoin dengan label desimal, dan saran di chart saat baris terlalu rapat.

Jalankan:

```
cd tools/pine-test
npm install
node run.mjs --verbose          # 17 skenario (termasuk 3 skenario crypto)
node run.mjs --render           # + gambar di tools/pine-test/out/
node preview.mjs out/preview.png
```

PineTS bukan TradingView. Hasil akhirnya tetap perlu dicek sekali di TradingView setelah dipasang.
