# IDCator untuk TradingView (Pine Script v6)

Empat indikator order-flow seperti di video ProfitPro, dibuat untuk TradingView. Ini bukan alat sinyal buy/sell.

> **Mau pakai footprint asli? Harus paket TradingView Premium atau Ultimate.** Footprint (volume beli dan jual agresif di setiap harga, `request.footprint`) hanya dikirim TradingView ke paket itu. Di paket lain muncul error *"This script uses volume footprints, which are available to only Premium and Ultimate plans"*.
>
> **Tanpa Premium:** pakai **`IDC_OrderFlow_Lite.pine`** (nama di chart: **IDC Lite**). Jalan di semua paket, termasuk gratis. Volumenya **perkiraan dari candle**, bukan footprint asli (lihat bagian *IDC Order Flow Lite*).

**Cukup pasang satu file:**
- Premium/Ultimate: `IDC_OrderFlow.pine` (nama di chart: **IDC**).
- Paket lain: `IDC_OrderFlow_Lite.pine`.

Keempat alat ada di dalamnya dan bisa dinyalakan atau dimatikan di setting, grup *Tools (on/off)*. Keempat file terpisah (butuh Premium/Ultimate) tetap disertakan kalau Anda ingin memasang alatnya satu per satu.

| File | Nama di chart | Isi |
|---|---|---|
| **`IDC_OrderFlow.pine`** | **IDC** | **Keempat alat di bawah dalam satu indikator, masing-masing on/off.** Footprint asli: Premium/Ultimate |
| **`IDC_OrderFlow_Lite.pine`** | **IDC Lite** | **Sama, tapi footprint diperkirakan dari candle. Semua paket TradingView** |
| `IDC_Imbalance_Levels.pine` | IDC Imbalance | Zona hijau/merah dari stacked imbalance diagonal, label `12.5x \| 165`. Zona memanjang sampai disentuh harga |
| `IDC_HVN_Profile.pine` | IDC HVN | Zona High Volume Node per periode (otomatis menurut timeframe chart: 15 detik → 5 menit, 1 menit → 15 menit, dan seterusnya). Zona terbesar oranye, lainnya biru-abu. Label `124 \| 65%` |
| `IDC_Heavy_Absorption.pine` | IDC Heavy Abs | Lingkaran hijau/merah berisi angka volume di harga terberat. Angkanya terus naik di bar yang sedang berjalan |
| `IDC_Pressure_Toolkit.pine` | IDC Pressure | Bubble redup/terang, kotak big trade + angka, titik absorption di wick, garis absorption, dan level psikologis |

Contoh tampilan pada data NQ 15 detik simulasi ada di `preview.png` dan `preview-ptk.png`. Tampilan live (bar yang sedang berjalan bergaris putus-putus) ada di `preview-live.png`, contoh Bitcoin 1 menit di `preview-crypto.png`, dan IDC Order Flow Lite di emas 1 menit (tanpa footprint) di `preview-lite.png`.

## Syarat

1. **Paket TradingView Premium atau Ultimate** untuk `IDC_OrderFlow.pine` dan keempat file terpisah. `request.footprint()` hanya jalan di paket ini; di paket lain script langsung error. Chart detik (15s) juga butuh paket ini. **Tanpa Premium/Ultimate: pakai `IDC_OrderFlow_Lite.pine`.**
2. **Data real-time CME** untuk NQ/ES (langganan data CME di TradingView). Tanpa itu datanya delay 10 menit.
3. Simbol yang punya volume, misalnya `CME_MINI:NQ1!`, `CME_MINI:ES1!`, `COMEX:GC1!`, atau crypto dari bursa (lihat bagian Crypto).

FundedNext Futures resmi mendukung TradingView sebagai platform trading. Jadi chart dengan indikator ini bisa dipakai langsung di akun FundedNext (Trading Panel → login akun futures). Pastikan sesuai aturan akun masing-masing.

## Cara pasang

> **Kalau gambar indikator tidak ikut bergeser bersama candle** (ada skala harga kedua di kanan, atau zona berada jauh dari candle): TradingView menyimpan skala dari versi lama script. Hapus indikatornya dari chart, lalu tambahkan lagi dari *Indicators → My scripts*. Bisa juga lewat menu **⋯ → Pin to scale** di nama indikator, lalu pilih skala yang sama dengan candle.

1. Buka chart di TradingView, klik **Pine Editor** di bawah.
2. Buka menu nama script di kiri atas Pine Editor, pilih **Create new → Indicator**, lalu hapus semua isinya.
3. Buka `IDC_OrderFlow.pine` (Premium/Ultimate) atau `IDC_OrderFlow_Lite.pine` (paket lain), salin **semua** isinya, lalu tempel di Pine Editor.
4. Klik **Save** (beri nama, misalnya "IDC Order Flow"), lalu **Add to chart**. Setelah tersimpan, indikator bisa dicari di **Indicators → My scripts**.
5. Buka setting indikator, lalu di grup **Tools (on/off)** centang alat yang ingin dipakai. Default: HVN Profile, Imbalance Levels, dan Heavy Absorption menyala; Pressure Toolkit mati.

File terpisah dipasang dengan cara yang sama, satu file per indikator.

## Setup seperti di video

- Chart **NQ1!**, timeframe **15 detik** (boleh juga 30 detik atau 1 menit).
- Pasang **IDC Order Flow** dengan setting default: HVN Profile (profil otomatis 5 menit di chart 15 detik), Imbalance Levels, dan Heavy Absorption menyala.
- Pressure Toolkit di video ditampilkan di chart terpisah. Kalau mau sama, tambahkan IDC Order Flow sekali lagi dengan hanya Pressure Toolkit yang menyala.
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

  Nilai yang sedang dipakai terlihat saat kursor diarahkan ke kotak status di pojok kanan atas chart (*Ticks per row: N*).
- **Kalau barisnya kurang pas**, pojok kanan atas chart menampilkan saran, misalnya `rows look too fine for this chart - try 'Ticks per row' = 500`. Isi angka itu di setting. Ini bisa terjadi pada koin yang jarang atau koin yang harganya sudah jauh berubah.
- TradingView tidak mengirim footprint untuk bar dengan lebih dari 2000 baris. Indikator mendeteksinya dan menampilkan *too many price rows ... set 'Ticks per row' to N*.
- **Angka volume dalam satuan koin**, dengan desimal untuk angka kecil: `12.5x | 0.39`, lingkaran `2.24`. Filter seperti *Min volume per level* dan *Min stack volume* juga dalam satuan koin.
- Futures crypto CME (`CME:BTC1!`, `CME:MBT1!`, `CME:ETH1!`) adalah futures, jadi barisnya 1 tick (tick-nya sudah besar, misalnya $5).
- Kalau muncul *no footprint data* di pair yang punya volume, cek dulu paket TradingView (Premium/Ultimate). Lalu coba simbol bursa lain.

## Emas dan logam spot (XAUUSD)

- Di simbol seperti `OANDA:XAUUSD` tick-nya 0.001, jauh lebih kecil dari harganya. Jadi ukuran baris otomatis juga dipakai di sini: di 1 menit barisnya $0.10 (100 tick). Perak, platinum, dan paladium juga dikenali, dari base currency (`XAU`, `XAG`, `XPT`, `XPD`) atau dari tickernya (`XAUUSD`, `GOLD`, `XAGUSD`, `SILVER`, ...).
- Volume di simbol CFD/broker (OANDA, FXCM, dan sejenisnya) adalah volume broker itu sendiri, bukan volume bursa. Untuk order flow emas yang sebenarnya, pakai futures `COMEX:GC1!` atau `COMEX:MGC1!` (barisnya 1 tick = $0.10).

## IDC Order Flow Lite (tanpa Premium)

`IDC_OrderFlow_Lite.pine` berisi keempat alat yang sama dengan setting yang sama. Bedanya hanya sumber data: footprint **diperkirakan dari candle**, bukan dari `request.footprint()`. Karena itu Lite jalan di semua paket TradingView.

**Cara memperkirakan** (sama persis dengan versi Python, mode `--candles`):
- Setiap candle dianggap bergerak open → low → high → close kalau close di atas open (open → high → low → close kalau turun).
- 90 % volumenya disebar di sepanjang jalur itu menurut jarak. Kaki naik dihitung beli, kaki turun dihitung jual. Setiap kaki menyisakan 20 % bagiannya di ujungnya, tempat harga berbalik.
- 10 % sisanya diperdagangkan dua arah di sekitar close.
- Hasilnya diolah keempat alat persis seperti footprint asli.

**Chart tetap bersih di semua timeframe** (default Lite):
- Zona Imbalance dan HVN yang sudah disentuh harga **dihapus**. Yang tersisa hanya zona yang masih berlaku. Untuk menampilkannya lagi, nyalakan *Keep touched zones*.
- Hanya zona terbaru yang tampil: paling banyak 20 zona Imbalance dan 12 zona HVN, dengan satu node (yang terbesar) per profil HVN. Semuanya bisa dinaikkan di setting (*Max zones on chart*, *Max clusters per period*).
- **Panjang profil HVN otomatis** mengikuti timeframe chart, sekitar 15–25 bar per profil:

  | Chart | Profil |
  |---|---|
  | 15 detik | 5 menit (seperti di video) |
  | 1 menit | 15 menit |
  | 5 menit | 2 jam |
  | 15 menit | 4 jam |
  | 30 menit dan 1 jam | 1 hari |
  | 4 jam | 1 minggu |
  | 1 hari | 1 bulan |

  Matikan *Auto profile timeframe* untuk memilih sendiri.
- **Ukuran baris otomatis dari candle chart itu sendiri**, sekitar 1/20 dari range rata-rata 100 candle terakhir. Ukuran ini diperbarui sekali sehari, jadi sama sepanjang hari. Ini berlaku untuk semua simbol (emas, indeks, forex, crypto) dan semua timeframe. Sebelum ada 100 candle, dipakai ukuran bawaan.

**Setting *Intrabars*** (grup *Data (Lite)*): candle yang lebih kecil di dalam setiap bar memberi perkiraan yang lebih halus.
- *Auto*: 1 menit untuk chart sampai 1 jam, 5 menit sampai 4 jam, dan seterusnya. Di chart 1 menit ke bawah dipakai candle chart itu sendiri.
- *1/5/15 seconds*: butuh paket TradingView yang punya data detik.

**Yang perlu diketahui:**
- Angka volume di label adalah **perkiraan**, bukan transaksi asli.
- **HVN Profile** paling dekat dengan aslinya. Di tes (profil 5 menit), zona HVN Lite menyentuh 86 % zona footprint asli dengan candle saja, dan 96 % dengan intrabar 15 detik.
- **Imbalance Levels** tanpa intrabar menandai badan candle yang bergerak kuat, dengan label rasio minimum (`5x | ...`). Dengan intrabar, zonanya lebih mirip aslinya.
- **Heavy Absorption** dan **Pressure Toolkit**: lingkaran dan titik muncul di bar dengan volume padat; posisinya perkiraan (biasanya di ujung wick).
- Untuk order flow yang sebenarnya: Premium/Ultimate dengan `IDC_OrderFlow.pine`, versi MT5 (membaca tick sendiri), atau versi Python dengan data trade.

## Alert

Klik ikon jam alarm, pilih **Condition** = nama indikator, lalu **Any alert() function call**. Atur sumber alert di setting tiap indikator:

- Imbalance dan HVN: saat harga menyentuh zona (setiap zona baru yang tersentuh, juga kalau beberapa zona tersentuh di bar yang sama).
- Heavy Absorption: pilih *Live (when a circle appears)*, yaitu sekali per bar per sisi tepat saat lingkaran muncul, atau *On bar close*.
- Pressure Toolkit: per jenis sinyal.

## Realtime

TradingView menjalankan ulang script di **setiap tick**, jadi semua alat ikut update live. Yang masih terbentuk ditampilkan **bergaris putus-putus**. Begitu bar atau periodenya selesai, garisnya jadi permanen (solid). Contohnya ada di `preview-live.png`.

- **Imbalance:** stacked imbalance dari bar yang sedang berjalan sudah tampil sebagai kotak putus-putus dan ikut berubah setiap tick. Saat bar close, kotak itu menjadi zona tetap. Zona yang sudah ada berhenti tepat di tick yang menyentuhnya.
- **HVN:** node dari periode yang sedang berjalan tampil putus-putus beserta labelnya, lalu menjadi zona tetap begitu periodenya selesai.
- **Heavy Absorption:** lingkaran muncul di harga terberat saat bar masih berjalan, lalu angkanya naik setiap ada transaksi di harga itu. Alert bisa dipasang *Live*.
- **Pressure Toolkit:** bubble dan titik di bar berjalan ikut berubah. Garis absorption tampil putus-putus selama bar berjalan, lalu menjadi permanen saat close.

Tampilan live bisa dimatikan per alat di setting (*Show the forming bar's stacks*, *Show the forming period's nodes*, *Show the forming bar's lines*).

## Perbedaan dengan versi ATAS/NinjaTrader

- **Big print:** TradingView tidak memberi script daftar transaksi satu per satu, hanya volume beli/jual per harga. Heavy Absorption dan big trade di Pressure Toolkit memakai harga dengan volume agresif terbesar di bar itu. Hasilnya dibandingkan dengan harga terberat bar-bar sebelumnya (rata-rata + k × standar deviasi). Angka di lingkaran tetap sama dengan aslinya: volume agresif di harga itu.
- **Batas objek:** setiap script maksimal 500 kotak/label/garis. Objek paling lama (yang sudah tersentuh lebih dulu) dihapus otomatis.
  - Di IDC Order Flow, batas itu dibagi ke keempat alat:
    - Imbalance: 250 zona;
    - HVN: 100 zona;
    - Heavy Absorption: 50 lingkaran;
    - Pressure Toolkit: 100 penanda dan 250 garis.
  - Kalau butuh lebih banyak riwayat untuk satu alat, pakai file terpisahnya.
- **Font:** bentuk lingkaran dan font memakai font TradingView. Ukurannya bisa diatur di setting (*Min/Max circle size*, *Label size*).

## Pengujian

Script diuji dengan [PineTS](https://github.com/LuxAlgo/PineTS) (runtime Pine Script open-source dari LuxAlgo) memakai data order-flow buatan. Tesnya mencakup:
- hasil per indikator (label, warna, posisi zona);
- update tick-per-tick (zona berhenti di tick yang menyentuh, angka lingkaran naik live);
- profil HVN otomatis menurut timeframe chart;
- crypto dan emas: ukuran baris otomatis per koin dan timeframe (juga `OANDA:XAUUSD`, dikenali dari tickernya), sesi Bitcoin dengan label desimal, dan saran di chart saat baris terlalu rapat;
- tipe data: TradingView hanya menerima ukuran baris yang sudah pasti saat script dimuat (*simple int*). PineTS tidak memeriksa ini, jadi ada tes khusus yang memastikan ukuran baris di kelima file dihitung di level atas script hanya dengan fungsi bawaan. Fungsi buatan sendiri tidak dipakai di situ, karena Pine menganggap parameternya *series*. Bagian itu juga tidak memakai `switch`, `if`, loop, `:=`, `var`, atau data bar;
- live: kotak/node/garis putus-putus selama bar berjalan yang menjadi permanen saat close, dan alert live Heavy Absorption;
- IDC Order Flow: dengan satu alat menyala hasilnya sama persis dengan file terpisahnya. Dengan semua menyala, hasilnya sama dengan gabungan keempatnya (NQ dan Bitcoin). Skenario realtime juga dijalankan pada IDC Order Flow (`--suite`).
- semua script tanpa plot dan tanpa pengaturan skala sendiri, supaya gambarnya memakai skala harga candle dan ikut bergeser bersamanya;
- IDC Order Flow Lite:
  - tidak memakai `request.footprint()` dan menggambar keempat alat tanpa data footprint;
  - ukuran baris dari candle chart (1/20 range rata-rata), diperbarui sekali sehari, juga di simbol tanpa base currency;
  - default bersih: hanya zona yang belum tersentuh, paling banyak 20 zona Imbalance dan 12 zona HVN, satu node per profil;
  - perkiraan satu candle naik kuat menghasilkan zona beli tepat di badannya;
  - intrabar membuat zona HVN lebih dekat ke footprint asli;
  - realtime sama dengan history.
- Versi Python (`python/`) memberi hasil yang sama persis dengan script TradingView di data uji yang sama, termasuk perkiraan Lite.

`IDC_OrderFlow.pine` dan `IDC_OrderFlow_Lite.pine` dibuat otomatis dari keempat file terpisah dengan `python3 tools/build_suite.py`. Perubahan dilakukan di file terpisah, lalu build dijalankan ulang.

Jalankan:

```
cd tools/pine-test
npm install
node run.mjs --verbose          # 31 skenario (crypto, emas, live, IDC Order Flow, Lite)
node run.mjs --suite IMB HVN HA PTK   # skenario alat dijalankan pada IDC Order Flow
node run.mjs --render           # + gambar di tools/pine-test/out/
node preview.mjs out/preview.png
```

PineTS bukan TradingView. Hasil akhirnya tetap perlu dicek sekali di TradingView setelah dipasang.
