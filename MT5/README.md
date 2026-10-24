# IDCator untuk MetaTrader 5 (MQL5)

Empat indikator order-flow seperti di video ProfitPro, dibuat untuk MetaTrader 5. Footprint (volume beli/jual agresif per harga) dibangun sendiri dari **tick history MT5** (`CopyTicksRange`). Ini bukan alat sinyal buy/sell.

**Cukup pasang satu indikator: `IDC_OrderFlow`.** Keempat alat ada di dalamnya, masing-masing bisa dinyalakan atau dimatikan di input *Tools (on/off)*, dan semuanya memakai satu pembaca tick. Indikator terpisah tetap disertakan.

| File | Nama di chart | Isi |
|---|---|---|
| **`Indicators/IDCator/IDC_OrderFlow.mq5`** | **IDC_OrderFlow** | **Keempat alat di bawah dalam satu indikator, masing-masing on/off** |
| `Indicators/IDCator/IDC_Imbalance_Levels.mq5` | IDC Imbalance | Zona hijau/merah dari stacked imbalance diagonal, label `12.5x \| 165`. Zona memanjang sampai disentuh harga |
| `Indicators/IDCator/IDC_HVN_Profile.mq5` | IDC HVN | Zona High Volume Node per periode (default M5). Zona terbesar oranye, lainnya biru-abu. Label `124 \| 65%` |
| `Indicators/IDCator/IDC_Heavy_Absorption.mq5` | IDC Heavy Abs | Lingkaran hijau/merah berisi angka volume di harga terberat. Angkanya terus naik di bar yang sedang berjalan |
| `Indicators/IDCator/IDC_Pressure_Toolkit.mq5` | IDC Pressure | Bubble redup/terang, kotak big trade + angka, titik absorption di wick, garis absorption, dan level psikologis |
| `Include/IDCator/IDCatorCore.mqh` | - | Bagian bersama: pembaca tick, footprint, statistik, gambar objek |
| `Include/IDCator/ImbalanceLevels.mqh`, `HvnProfile.mqh`, `HeavyAbsorption.mqh`, `PressureToolkit.mqh` | - | Isi tiap alat (input dan logika), dipakai oleh indikator terpisah dan IDC_OrderFlow. **Seluruh folder `Include/IDCator` wajib ikut disalin** |

Contoh tampilan (data simulasi, dijalankan tick per tick di terminal MT5 simulasi): NQ M1 di `preview.png` dan `preview-ptk.png`, tampilan live (bar/periode berjalan bergaris putus-putus) di `preview-live.png`, Bitcoin M1 di `preview-crypto.png`.

## Penting: data dan akun

- **FundedNext Futures tidak memakai MT5.** Platform resminya Tradovate, NinjaTrader, dan TradingView. Untuk akun futures di Tradovate, pakai versi **TradingView** (folder `TradingView`).
- Di MT5, hasilnya tergantung jenis simbol:
  - **Futures bursa** (broker MT5 yang mengirim transaksi CME dengan flag buy/sell): footprint asli, angka = kontrak, sama seperti di video.
  - **CFD/forex** (misalnya NAS100, US100, XAUUSD di broker atau akun prop CFD): broker tidak mengirim volume transaksi. Indikator memakai setiap perubahan harga quote sebagai 1 "volume", dan arah beli/jual dari naik/turunnya harga tengah. Bentuk dan cara pakainya sama, tetapi **angkanya hitungan tick, bukan kontrak**.
- Setting *Tick source*: `Auto` (default) memilih transaksi bursa kalau ada, kalau tidak quote.

## Cara pasang

1. Di MT5: **File → Open Data Folder**, lalu buka folder `MQL5`.
2. Salin folder `Include/IDCator` (5 file `.mqh`) ke `MQL5/Include/IDCator`.
3. Salin folder `Indicators/IDCator` ke `MQL5/Indicators/IDCator`.
4. Tekan **F4** (MetaEditor), buka `IDC_OrderFlow.mq5`, lalu tekan **F7** (Compile). Harus `0 errors`. File terpisah boleh di-compile juga kalau ingin dipakai.
5. Kembali ke MT5. Di **Navigator → Indicators** klik kanan **Refresh**, lalu drag **IDC_OrderFlow** ke chart. Di tab *Inputs*, grup *Tools (on/off)*, pilih alat yang menyala.

Kalau compile menunjukkan error, kirim screenshot tab *Errors* di MetaEditor.

## Setup seperti di video

- MT5 tidak punya chart detik. Pakai **M1** (paling dekat dengan 15 detik di video).
- Pasang **IDC_OrderFlow** dengan setting default: HVN Profile (Profile timeframe M5), Imbalance Levels, dan Heavy Absorption menyala; Pressure Toolkit mati.
- Saat pertama dipasang, pojok kiri atas menampilkan *loading tick history...*. MT5 sedang mengunduh tick, jadi tunggu sebentar. *Bars to load* mengatur berapa bar ke belakang yang dihitung (lebih banyak = lebih lama).
- *Row size in points* = 0 (default): ukuran baris dipilih otomatis dari bar chart, sekitar 20 baris per bar biasa. Hasilnya 1 tick di NQ M1, beberapa dolar di Bitcoin, dan pas juga untuk CFD seperti NAS100. Isi angka sendiri kalau ingin ukuran tertentu.

## Crypto

Keempat indikator bisa dipakai di semua simbol crypto di MT5 (BTCUSD, ETHUSD, SOLUSD, dan koin lain).

- **Ukuran baris otomatis** (*Row size in points* = 0; di HVN juga *Footprint row in points* = 0). Ukurannya dihitung dari median rentang 200 bar terakhir, sekitar 20 baris per bar, dibulatkan ke 1/2/2.5/5 dan kelipatan tick. Pada data uji Bitcoin M1 hasilnya baris $5, sama dengan versi TradingView. Di chart asli keduanya biasanya mirip, tetapi tidak selalu sama persis, karena TradingView harus menaksir ukuran baris sebelum ada data.
- **Angka volume dengan desimal** untuk angka kecil (`12.5x | 0.39`, lingkaran `2.24`).
- **Di broker CFD** (paling umum untuk crypto di MT5), angka volume adalah hitungan perubahan harga, bukan jumlah koin (lihat bagian *Penting* di atas). Zona dan bentuknya tetap berfungsi.
- Crypto buka 24 jam. Timer tetap menutup bar saat pasar sepi.

## Alert

Aktifkan di setting tiap indikator (grup *Alerts*). Alert MT5 muncul sebagai pop-up. Untuk notifikasi ke HP, isi MetaQuotes ID di **Tools → Options → Notifications**.

- Imbalance dan HVN: saat harga menyentuh zona (default aktif).
- Heavy Absorption: *Alert* = *Live (when a circle appears)*, yaitu sekali per bar per sisi tepat saat lingkaran muncul, atau *On bar close*.
- Pressure Toolkit: per jenis sinyal.

## Realtime

Indikator membaca tick baru di setiap tick. Yang masih terbentuk ditampilkan dengan **bingkai putus-putus**, lalu menjadi permanen saat bar atau periodenya selesai. Contohnya ada di `preview-live.png`.

- **Imbalance:** stacked imbalance dari bar yang sedang berjalan tampil sebagai kotak dengan bingkai putus-putus dan ikut berubah setiap tick. Saat bar close, kotak itu menjadi zona tetap. Zona lama berhenti tepat di tick yang menyentuhnya.
- **HVN:** node periode yang sedang berjalan tampil dengan bingkai putus-putus beserta labelnya, lalu menjadi zona tetap begitu periodenya selesai. Kalau pasar sepi, timer menutup bar 30 detik setelah waktunya habis.
- **Heavy Absorption:** lingkaran muncul di harga terberat saat bar masih berjalan, lalu angkanya naik setiap ada transaksi di harga itu. Alert bisa *Live*.
- **Pressure Toolkit:** bubble dan titik di bar berjalan ikut berubah. Garis absorption tampil putus-putus selama bar berjalan, lalu menjadi permanen saat close.

Tampilan live bisa dimatikan per alat (*Show the forming bar's stacks*, *Show the forming period's nodes*, *Show the forming bar's lines*).

Bisa juga dicoba di Strategy Tester (mode *Visual*, model *Every tick based on real ticks*).

## Perbedaan dengan versi TradingView

- Objek MT5 tidak transparan. Warna zona dicampur dengan warna latar chart supaya terlihat seperti transparan; kalau latar chart diganti, pasang ulang indikator.
- Lingkaran dan kotak memakai font Wingdings, jadi ukurannya mengikuti volume.
- Footprint dihitung dari tick, bukan dari server TradingView, jadi di CFD angkanya berbeda dengan TradingView/CME.

## Pengujian

MetaEditor tidak tersedia di lingkungan pembuatan, jadi file ini **belum di-compile dengan MetaEditor asli**. Sebagai gantinya, `tools/mql-check` menerjemahkan setiap indikator ke C++ dan menjalankannya di **terminal MT5 simulasi**. Terminal ini meniru tick history (`CopyTicksRange`), bar M1, `OnCalculate` di setiap tick, `OnTimer` tiap detik, dan objek chart. Index array di luar batas dan pembagian dengan nol menghentikan program, sama seperti critical error di MT5.

Data uji: order flow NQ simulasi (3 jam, sekitar 89 ribu tick bursa), versi CFD-nya (hanya perubahan bid/ask), dan order flow yang sama sebagai Bitcoin (harga 100.000, tick 0.01, volume pecahan). Ada 14 skenario:

1. Lolos compile check (nama, jumlah parameter, tipe).
2. **Realtime = history:** 27 ribu tick terakhir masuk satu per satu, hasil akhirnya sama persis dengan saat semua tick sudah jadi history.
3. Sama juga kalau MT5 melewatkan panggilan `OnCalculate` (37 tick per panggilan).
4. Feed CFD (tanpa transaksi): semua indikator tetap menggambar, realtime = history.
5. **Sama dengan versi TradingView** pada data yang sama: 95 zona imbalance, 51 zona HVN, 13 lingkaran, 39 bubble, 2 big trade, 71 titik, 127 garis, dan 12 level psikologis. Posisi, label, dan titik sentuhnya identik.
6. Alert sentuhan zona keluar sekali, tepat di detik transaksi pertama yang masuk zona.
7. Angka di lingkaran Heavy Absorption muncul saat bar masih berjalan dan terus naik (misalnya 265 → 271 → ... → 298).
8. Pasar sepi: bar terakhir ditutup timer 30 detik setelah waktunya habis, lalu zona HVN periode itu muncul.
9. **Bitcoin** (feed bursa dan CFD) dengan ukuran baris otomatis: baris $5, label berdesimal, realtime = history.
10. **Bitcoin sama dengan versi TradingView** pada baris $5: semua zona, label, lingkaran, dan garis identik.
11. **IDC_OrderFlow** dengan keempat alat menyala menggambar objek yang persis sama dengan keempat indikator terpisah (NQ dan Bitcoin).
12. IDC_OrderFlow: realtime = history.
13. **Live, tick per tick:** kotak imbalance, node HVN, dan garis absorption putus-putus muncul selama bar/periode berjalan, lalu menjadi permanen saat close.
14. Alert live Heavy Absorption keluar di dalam bar saat lingkaran muncul, paling banyak sekali per sisi.

```
cd tools/pine-test && npm install && cd ../..      # sekali (dipakai bersama tes TradingView)
python3 tools/mql-check/check.py                   # compile check
node tools/mql-check/run.mjs --verbose             # 14 skenario
node tools/mql-check/run.mjs --render              # + gambar di tools/mql-check/out/
```

Simulasi ini bukan MT5 asli. Tetap compile sekali di MetaEditor (langkah 4 di atas) dan cek tampilannya di chart.
