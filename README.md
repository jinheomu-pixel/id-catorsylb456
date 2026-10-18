# IDCator: 4 indikator order flow untuk NinjaTrader 8

Replika *clean-room* dari 4 indikator NinjaTrader yang dijual ProfitPro (profitprotrading.com). Isinya Pressure Toolkit, HVN Profile, Imbalance Levels, dan Heavy Absorption. Dibangun ulang dari deskripsi produk publik (ATAS Marketplace dan profitprotrading.com) serta analisis frame per frame video promo mereka di Instagram. Bukan kode asli ProfitPro dan tidak berafiliasi dengan ProfitPro.

> **Bukan sinyal buy/sell otomatis.** Keempat indikator hanya menampilkan data order flow (zona, lingkaran, garis). Keputusan entry/exit tetap di tangan kamu.

| Indikator | Fungsi singkat | Nama di NinjaTrader |
|---|---|---|
| **Imbalance Levels** | Stacked imbalance bid/ask dijadikan zona support/resistance sampai disentuh harga. Label `rasio x \| volume` | `IDC Imbalance Levels` |
| **Heavy Absorption** | Transaksi agresif yang sangat besar (mean + k·σ) dan diserap limit order pasif. Lingkaran berisi angka volume | `IDC Heavy Absorption` |
| **HVN Profile** | High Volume Node per periode (1 menit sampai mingguan), plus POC dan label `volume \| % buyer/seller`. Zona diperpanjang sampai harga kembali | `IDC HVN Profile` |
| **Pressure Toolkit** | Bubble tekanan beli/jual (redup = lemah, terang = kuat), big trade, titik dan garis absorption di wick, level psikologis, alert per sinyal | `IDC Pressure Toolkit` |

Kode ada di [`NinjaTrader8/IDCator/`](NinjaTrader8/IDCator/):

```
IDCatorCore.cs           mesin footprint bersama (WAJIB ikut di-copy)
IDCImbalanceLevels.cs
IDCHeavyAbsorption.cs
IDCHvnProfile.cs
IDCPressureToolkit.cs
```

---

## 1. Kebutuhan

- **NinjaTrader 8 desktop** (Windows), versi **8.1.x** atau lebih baru. Indikator NinjaScript (.cs) **tidak** bisa dipakai di Tradovate web/mobile atau NinjaTrader Web.
- **Data order flow**, pilih salah satu (atau keduanya):
  1. **Volumetric bars (Order Flow+)**: cara yang sama dengan versi asli ProfitPro. Di halaman add-on Tradovate kamu, Order Flow+ tertulis *Active / Included with Tradovate Prop*. Kemungkinan besar Order Flow+ ikut aktif saat login NT8 dengan akun yang sama, tapi ini belum terkonfirmasi khusus untuk FundedNext. Cek sendiri: klik kanan chart, pilih **Data Series**, lalu lihat apakah **Volumetric** ada di pilihan **Type**.
  2. **Tick Replay**: tidak butuh Order Flow+. Indikator membaca setiap transaksi (harga + bid/ask saat transaksi) langsung dari data feed.

### Login NinjaTrader 8 pakai akun FundedNext (Tradovate)
1. Login dulu sekali ke **Tradovate Web** dengan username/password Tradovate dari dashboard FundedNext. Isi data kontak, pilih **Non-professional**, tanda tangani perjanjian market data.
2. Buka **NinjaTrader 8**, masukkan username/password Tradovate yang sama, lalu pilih **Simulation** (bukan Live). Akun prop memang berjalan di mode simulasi.
3. Kalau pakai Multi-Provider: **Control Center → Connections → Configure → NinjaTrader** (account type *Simulation*).

---

## 2. Instalasi

1. Download / clone repo ini.
2. Copy folder **`NinjaTrader8/IDCator`** ke:
   ```
   Documents\NinjaTrader 8\bin\Custom\Indicators\IDCator\
   ```
   Kelima file `.cs` harus ada, termasuk `IDCatorCore.cs`.
3. Di NinjaTrader: **New → NinjaScript Editor**, lalu tekan **F5** (Compile). Tunggu sampai tidak ada error.
   - Kalau error muncul di file **lain** (bukan IDCator), klik kanan error tersebut, pilih *Exclude*, lalu compile ulang. NinjaTrader meng-compile semua script sekaligus.
4. Buka chart, klik kanan **Indicators**, buka folder **IDCator**, lalu tambahkan indikator yang diinginkan.

---

## 3. Setting chart yang disarankan

Video ProfitPro memakai **NQ** dengan bar **15 detik / 30 detik**, profil HVN **5 menit**, dan ketiga indikator dipasang bersamaan.

**Opsi A: Volumetric (kalau Order Flow+ aktif)**
- Data Series → Type: **Volumetric**, base period misalnya *Second 15* atau *Minute 1*, **Ticks per level = 1**, Delta type *BidAsk*.
- Heavy Absorption dan *big trade* di Pressure Toolkit tetap butuh data per transaksi. Aktifkan juga Tick Replay (opsi B) kalau mau riwayat historisnya.

**Opsi B: Tick Replay (tanpa Order Flow+)**
1. **Tools → Options → Market Data**, centang **Show Tick Replay**.
2. Klik kanan chart, pilih **Data Series**, lalu centang **Tick Replay**.
3. **Days to load: 2–5 hari** saja. Tick Replay NQ/ES itu berat.

**Wajib untuk semua:** di setting indikator, **Calculate = On each tick** (default). Kalau diubah, indikator memunculkan peringatan di pojok kiri atas chart.

Tanpa Tick Replay dan tanpa Volumetric, indikator tetap jalan untuk data **realtime** (mulai saat chart dibuka). HVN Profile juga tetap bisa menggambar riwayat dengan perkiraan dari volume bar.

**Apa yang bergerak live (bar belum close)**

| Indikator | Saat bar masih berjalan | Saat bar close |
|---|---|---|
| Imbalance Levels | Zona yang sudah ada berhenti tepat di tick yang menyentuhnya, dan alert bunyi saat itu juga | Zona baru dari bar itu muncul (sama seperti aslinya, imbalance dihitung dari bar yang sudah jadi) |
| Heavy Absorption | Lingkaran muncul tepat di transaksi besarnya, lalu angkanya bertambah setiap ada transaksi sisi yang sama di harga itu | Nilai jadi final. Alert *on bar close* (kalau dipilih) |
| HVN Profile | Histogram profil periode berjalan ikut berubah setiap transaksi (paling cepat tiap 300 ms). Zonanya belum digambar, sama seperti aslinya | Bar masuk ke profil periode. Saat periode selesai, zona final dan labelnya muncul |
| Pressure Toolkit | Bubble, big trade, dan titik absorption bar berjalan diperbarui (paling cepat tiap 250 ms) | Sinyal final dan alert |

Chart langsung digambar ulang saat ada perubahan, jadi tidak perlu menunggu tick berikutnya. Kalau pasar mendadak sepi (tidak ada transaksi lagi), transaksi terakhir tetap muncul paling lambat sekitar 0,6 detik kemudian.

Saat market tutup, misalnya jeda harian 16:00–17:00 CT, akhir pekan, atau Playback di-pause, bar terakhir tetap ditutup otomatis **30 detik setelah waktunya habis**. Zona Imbalance dari bar itu dan zona HVN periode itu langsung muncul, tanpa menunggu transaksi pertama sesi berikutnya. Hasilnya sama persis dengan chart yang baru dibuka belakangan. Alert *on bar close* untuk bar yang baru ketahuan selesai saat sesi berikutnya dibuka tidak dibunyikan, karena sudah basi.

---

## 4. Penjelasan tiap indikator

### Imbalance Levels
- Setiap bar yang sudah close dipindai secara **diagonal**:
  - imbalance beli di harga P: `Ask(P) ≥ rasio × Bid(P − 1 level)`
  - imbalance jual di harga P: `Bid(P) ≥ rasio × Ask(P + 1 level)`
- Minimal **3 level berturut-turut** (default) membentuk zona. Zona beli hanya dihitung kalau berada **di bawah close** (support), zona jual hanya kalau **di atas close** (resistance).
- Zona diperpanjang ke kanan sampai harga masuk lagi ke zona, lalu berhenti di bar yang menyentuhnya.
- Label `6.7x | 350` = rasio terkuat dalam stack | volume stack. Makin besar rasio, makin pekat warnanya (sekitar 7% di 5x sampai 40% di ≥15x).
- Kalau sisi lawan diagonal bervolume **nol**, level itu dihitung imbalance tepat di rasio minimum (*Zero-value handling = CountAsMinRatio*). Ini sesuai label di video asli yang banyak berbunyi `5x | 8`. Level paling bawah/atas bar tidak punya pasangan diagonal, jadi tidak dihitung.
- Label rata kanan 5 px sebelum ujung zona. Kalau zona sedikit lebih sempit dari teksnya (misalnya zona 2 bar yang langsung tersentuh bar berikutnya), label mulai dari tepi kiri zona dan boleh melewati ujung kanan, sama seperti di video. Label disembunyikan hanya kalau zona jauh lebih sempit dari teks (< 0,8×) atau terlalu pendek.
- Zona yang sudah tersentuh **tidak** dibuat lebih pudar, cuma berhenti memanjang (sama seperti aslinya). Kalau mau dibedakan, turunkan *Touched zone opacity %*.
- Setting penting: *Min imbalance ratio* (5), *Min stacked levels* (3), *Zero-value handling*, *Discard if next bar touches* (off), *Alert on zone touch*.

### Heavy Absorption
- Threshold statistik: **rata-rata + k × standar deviasi** ukuran transaksi dalam *Lookback bars* (default 100 bar, k = 2.0). Threshold ini otomatis naik saat pasar ramai dan turun saat sepi.
- Satu lingkaran hijau (beli) dan/atau merah (jual) per bar, di harga transaksi terbesar. Kalau muncul transaksi yang lebih besar, lingkarannya pindah.
- Angka di dalam lingkaran = volume agresif di harga itu, dan terus bertambah secara live. Ribuan ditulis dengan K besar (`9.1K`, `1K`) seperti di video. Bisa diubah ke *jumlah transaksi yang lolos threshold*.
- Ukuran lingkaran = diameter minimum + faktor × √volume (*Circle sizing = Absolute*, mengikuti video asli). Volume diukur relatif terhadap ukuran transaksi rata-rata instrumen, jadi tetap proporsional di NQ, ES, saham, maupun crypto. Opsi *RelativeToVisible* membandingkan dengan lingkaran lain di layar.
- *Min volume filter* menyembunyikan lingkaran yang angkanya di bawah batas.
- Opsi *Print grouping: SameTimestamp* menjumlahkan transaksi dengan timestamp dan sisi yang sama, mendekati "satu order besar yang menyapu beberapa harga".
- **Butuh Tick Replay** untuk riwayat. Tanpa itu, lingkaran baru muncul sejak realtime.

### HVN Profile
- *Profile timeframe*: 1m, 5m (default, sama seperti di video: chart NQ 15 detik dengan node per 5 menit), 15m, 30m, 1h, 2h, 4h, Daily, Weekly.
- Profil volume per periode dihaluskan (*Smoothing passes*). Baris dengan volume ≥ *HVN threshold %* dari baris tertinggi digabung menjadi cluster (*Gap tolerance*, *Min cluster size*), lalu tepinya dirapikan (*Cluster sensitivity*: 0% = cluster utuh, 100% = hanya baris puncak).
- Cluster terbesar di periode itu berwarna **oranye**, cluster lain **biru**. Garis **POC** (harga dengan volume terbesar di cluster) bisa dinyalakan lewat *Show POC line*. Default-nya mati, karena setup default di video asli hanya menampilkan zona polos.
- Zona yang sudah tersentuh tetap dengan warna yang sama, hanya berhenti memanjang.
- Label `221 | 51%`: angka = volume di POC cluster (bisa diganti ke total volume cluster). Persentase = porsi sisi dominan, **hijau** kalau buyer dominan, **merah** kalau seller.
- Zona baru bisa "tersentuh" setelah periodenya selesai (+ *Touch grace bars*), lalu berhenti di bar yang menyentuh. Ada alert sentuhan.
- Histogram profil per periode bisa ditampilkan atau disembunyikan. Periode yang sedang berjalan digambar lebih transparan (*Live profile opacity*).
- Di realtime, histogram periode berjalan ikut bergerak setiap transaksi, termasuk bar yang belum close (diperbarui paling cepat tiap 300 ms). Zona periode berjalan tidak digambar, sama seperti aslinya: zona dan labelnya muncul begitu periode selesai. Kalau mau melihat zona sementara (lebih pudar, tanpa label), nyalakan *Show developing period zones*.
- Di chart tick/volume/range, bar yang melewati batas periode dimasukkan ke periode tempat bar itu selesai, jadi hasil live sama dengan setelah chart di-reload.

### Pressure Toolkit
- **Pressure bubble**: level harga dengan volume agresif besar (≥ mean + k·σ volume per level) dan dominan dibanding sisi lawan (*Dominance ratio*). Bubble redup = lemah, terang = kuat.
- **Big trade**: transaksi tunggal yang sangat besar dan jatuh di/dekat bubble dengan sisi yang sama. Ditampilkan sebagai kotak kecil plus angka volume. Butuh Tick Replay untuk riwayat.
- **Absorption dot**: wick (bagian bar di luar body) yang menyimpan level bervolume tinggi. Titik **merah di atas** bar = absorption di high, titik **hijau di bawah** = absorption di low. Titik yang lebih besar menandai kejadian yang signifikan secara statistik.
- **Absorption line**: satu garis per level bervolume tinggi di wick, diperpanjang sampai harga kembali menguji level itu. Warna mengikuti sisi agresor yang diserap: hijau = buyer, merah = seller.
- **Level psikologis**: angka bulat yang otomatis menyesuaikan harga (NQ 100, ES 25, GC 10, CL 0.25, BTC 250). Bisa diatur manual lewat *Level step*.
- Alert terpisah untuk bubble, big trade, absorption dot, retest garis absorption, dan cross level psikologis.

---

## 5. Cara pakai (seperti di video ProfitPro)

Pola yang paling sering muncul di video adalah **konfluensi**:

> Level yang **belum tersentuh** (zona Imbalance atau HVN) + **lingkaran Heavy Absorption** yang muncul tepat di level itu.

- Harga naik ke zona **merah** (sell imbalance / HVN di atas), lalu muncul lingkaran **hijau** besar di high. Artinya buyer agresif terserap, potensi reversal turun.
- Harga turun ke zona **hijau** / HVN di bawah, lalu muncul lingkaran **merah** besar di low. Artinya seller agresif terserap, potensi reversal naik.
- Pakai level berikutnya sebagai target dan sisi luar zona sebagai invalidasi.

Tetap kombinasikan dengan analisa sendiri dan manajemen risiko, apalagi di akun prop yang punya aturan drawdown.

---

## 6. Troubleshooting

| Gejala | Solusi |
|---|---|
| Tulisan kuning/putih di kiri atas: *no footprint history…* | Aktifkan Tick Replay atau pakai chart Volumetric |
| *Volumetric requested but chart is not Volumetric* | Ganti *Footprint source* ke **Auto**, atau ubah Data Series ke Volumetric |
| *historical ticks carry no bid/ask* | Data feed tidak menyimpan bid/ask historis. Indikator otomatis memakai *tick rule* (naik = beli, turun = jual) |
| Chart lambat | Kurangi *Days to load*, kurangi *Max bars back* |
| Alert tidak bunyi | Alert hanya aktif di realtime. File suara diambil dari folder `sounds` NinjaTrader (Alert1–4.wav), atau tulis path lengkap. Path yang salah tidak membuat indikator error, alert hanya jadi tanpa suara |
| Buka chart saat market tutup (malam/weekend) | Normal. Bar terakhir yang sudah selesai tetap dianalisa saat chart dibuka |

---

## 7. Untuk developer: compile-check tanpa NinjaTrader

```bash
# memakai stub API (signature dari dokumentasi resmi NinjaScript)
dotnet build tools/compile-check

# memakai DLL asli NinjaTrader 8 (folder bin instalasi NinjaTrader)
dotnet build tools/compile-check -p:NT8BinDir="C:\Program Files\NinjaTrader 8\bin"
```

Kode ditulis kompatibel dengan **C# 5**, karena NinjaTrader sebelum 8.1.2 hanya mendukung C# 5.

Ada juga **simulator perilaku**, yaitu runtime NinjaTrader palsu yang diberi data tick sintetis. Isinya 87 skenario: Tick Replay, Volumetric, realtime (update di bar yang belum close, repaint chart, zona berhenti tepat di tick yang menyentuh, pasar sepi, bar terakhir sebelum market tutup), market tutup, quote sintetis, stress 20.000 bar, render di thread lain, dan detail visual yang diukur dari video (label, warna zona tersentuh, huruf K).

```bash
dotnet run --project tools/sim-test -c Release            # semua skenario
dotnet run --project tools/sim-test -c Release -- HVN     # filter nama
```

---

*Trading futures berisiko tinggi. Indikator ini alat bantu analisa, bukan jaminan profit.*
