# IDCator untuk MetaTrader 5 (MQL5)

Empat indikator order-flow seperti di video ProfitPro, dibuat untuk MetaTrader 5. Footprint (volume beli/jual agresif per harga) dibangun sendiri dari **tick history MT5** (`CopyTicksRange`). Ini bukan alat sinyal buy/sell.

| File | Nama di chart | Isi |
|---|---|---|
| `Indicators/IDCator/IDC_Imbalance_Levels.mq5` | IDC Imbalance | Zona hijau/merah dari stacked imbalance diagonal, label `12.5x \| 165`. Zona memanjang sampai disentuh harga |
| `Indicators/IDCator/IDC_HVN_Profile.mq5` | IDC HVN | Zona High Volume Node per periode (default M5). Zona terbesar oranye, lainnya biru-abu. Label `124 \| 65%` |
| `Indicators/IDCator/IDC_Heavy_Absorption.mq5` | IDC Heavy Abs | Lingkaran hijau/merah berisi angka volume di harga terberat. Angkanya terus naik di bar yang sedang berjalan |
| `Indicators/IDCator/IDC_Pressure_Toolkit.mq5` | IDC Pressure | Bubble redup/terang, kotak big trade + angka, titik absorption di wick, garis absorption, dan level psikologis |
| `Include/IDCator/IDCatorCore.mqh` | - | Bagian bersama: pembaca tick, footprint, statistik, gambar objek. **Wajib ikut disalin** |

Contoh tampilan (data NQ M1 simulasi, dijalankan tick per tick di terminal MT5 simulasi) ada di `preview.png` dan `preview-ptk.png`.

## Penting: data dan akun

- **FundedNext Futures tidak memakai MT5.** Platform resminya Tradovate, NinjaTrader, dan TradingView. Untuk akun futures di Tradovate, pakai versi **TradingView** (folder `TradingView`).
- Di MT5, hasilnya tergantung jenis simbol:
  - **Futures bursa** (broker MT5 yang mengirim transaksi CME dengan flag buy/sell): footprint asli, angka = kontrak, sama seperti di video.
  - **CFD/forex** (misalnya NAS100, US100, XAUUSD di broker atau akun prop CFD): broker tidak mengirim volume transaksi. Indikator memakai setiap perubahan harga quote sebagai 1 "volume", dan arah beli/jual dari naik/turunnya harga tengah. Bentuk dan cara pakainya sama, tetapi **angkanya hitungan tick, bukan kontrak**.
- Setting *Tick source*: `Auto` (default) memilih transaksi bursa kalau ada, kalau tidak quote.

## Cara pasang

1. Di MT5: **File → Open Data Folder**, lalu buka folder `MQL5`.
2. Salin folder `Include/IDCator` ke `MQL5/Include/IDCator`.
3. Salin folder `Indicators/IDCator` ke `MQL5/Indicators/IDCator`.
4. Tekan **F4** (MetaEditor), buka keempat file `.mq5`, lalu tekan **F7** (Compile) di masing-masing. Harus `0 errors`.
5. Kembali ke MT5. Di **Navigator → Indicators** klik kanan **Refresh**, lalu drag indikator ke chart.

Kalau compile menunjukkan error, kirim screenshot tab *Errors* di MetaEditor.

## Setup seperti di video

- MT5 tidak punya chart detik. Pakai **M1** (paling dekat dengan 15 detik di video).
- Pasang **IDC HVN** (Profile timeframe M5), **IDC Imbalance**, dan **IDC Heavy Abs** bersamaan.
- Saat pertama dipasang, pojok kiri atas menampilkan *loading tick history...*. MT5 sedang mengunduh tick, jadi tunggu sebentar. *Bars to load* mengatur berapa bar ke belakang yang dihitung (lebih banyak = lebih lama).
- Kalau baris terlalu rapat di CFD, naikkan *Row size in points* (misalnya 5–10 pada NAS100).

## Alert

Aktifkan di setting tiap indikator (grup *Alerts*). Alert MT5 muncul sebagai pop-up. Untuk notifikasi ke HP, isi MetaQuotes ID di **Tools → Options → Notifications**.

- Imbalance dan HVN: saat harga menyentuh zona (default aktif).
- Heavy Absorption: saat bar close.
- Pressure Toolkit: per jenis sinyal.

## Realtime

Indikator membaca tick baru di setiap tick:

- **Imbalance:** zona berhenti di tick yang menyentuhnya. Zona baru muncul saat bar close.
- **Heavy Absorption:** lingkaran muncul di harga terberat, lalu angkanya naik setiap ada transaksi di harga itu.
- **HVN:** zona sebuah periode muncul begitu periode itu selesai. Kalau pasar sepi, timer menutup bar 30 detik setelah waktunya habis.
- **Pressure Toolkit:** bubble dan titik di bar berjalan ikut berubah. Garis absorption dibuat saat bar close.

Bisa juga dicoba di Strategy Tester (mode *Visual*, model *Every tick based on real ticks*).

## Perbedaan dengan versi TradingView

- Objek MT5 tidak transparan. Warna zona dicampur dengan warna latar chart supaya terlihat seperti transparan; kalau latar chart diganti, pasang ulang indikator.
- Lingkaran dan kotak memakai font Wingdings, jadi ukurannya mengikuti volume.
- Footprint dihitung dari tick, bukan dari server TradingView, jadi di CFD angkanya berbeda dengan TradingView/CME.

## Pengujian

MetaEditor tidak tersedia di lingkungan pembuatan, jadi file ini **belum di-compile dengan MetaEditor asli**. Sebagai gantinya, `tools/mql-check` menerjemahkan setiap indikator ke C++ dan menjalankannya di **terminal MT5 simulasi**. Terminal ini meniru tick history (`CopyTicksRange`), bar M1, `OnCalculate` di setiap tick, `OnTimer` tiap detik, dan objek chart. Index array di luar batas dan pembagian dengan nol menghentikan program, sama seperti critical error di MT5.

Data uji: order flow NQ simulasi (3 jam, sekitar 89 ribu tick bursa), dan versi CFD-nya (hanya perubahan bid/ask). Ada 8 skenario:

1. Lolos compile check (nama, jumlah parameter, tipe).
2. **Realtime = history:** 27 ribu tick terakhir masuk satu per satu, hasil akhirnya sama persis dengan saat semua tick sudah jadi history.
3. Sama juga kalau MT5 melewatkan panggilan `OnCalculate` (37 tick per panggilan).
4. Feed CFD (tanpa transaksi): semua indikator tetap menggambar, realtime = history.
5. **Sama dengan versi TradingView** pada data yang sama: 95 zona imbalance, 51 zona HVN, 13 lingkaran, 39 bubble, 2 big trade, 71 titik, 127 garis, dan 12 level psikologis. Posisi, label, dan titik sentuhnya identik.
6. Alert sentuhan zona keluar sekali, tepat di detik transaksi pertama yang masuk zona.
7. Angka di lingkaran Heavy Absorption muncul saat bar masih berjalan dan terus naik (misalnya 265 → 271 → ... → 298).
8. Pasar sepi: bar terakhir ditutup timer 30 detik setelah waktunya habis, lalu zona HVN periode itu muncul.

```
cd tools/pine-test && npm install && cd ../..      # sekali (dipakai bersama tes TradingView)
python3 tools/mql-check/check.py                   # compile check
node tools/mql-check/run.mjs --verbose             # 8 skenario
node tools/mql-check/run.mjs --render              # + gambar di tools/mql-check/out/
```

Simulasi ini bukan MT5 asli. Tetap compile sekali di MetaEditor (langkah 4 di atas) dan cek tampilannya di chart.
