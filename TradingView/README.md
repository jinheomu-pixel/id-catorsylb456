# IDCator untuk TradingView (Pine Script v6)

Empat indikator order-flow seperti di video ProfitPro, dibuat untuk TradingView. Datanya dari **footprint asli TradingView**, yaitu volume beli dan jual agresif di setiap harga (`request.footprint`). Ini bukan alat sinyal buy/sell.

| File | Nama di chart | Isi |
|---|---|---|
| `IDC_Imbalance_Levels.pine` | IDC Imbalance | Zona hijau/merah dari stacked imbalance diagonal, label `12.5x \| 165`. Zona memanjang sampai disentuh harga |
| `IDC_HVN_Profile.pine` | IDC HVN | Zona High Volume Node per periode (default 5 menit). Zona terbesar oranye, lainnya biru-abu. Label `124 \| 65%` |
| `IDC_Heavy_Absorption.pine` | IDC Heavy Abs | Lingkaran hijau/merah berisi angka volume di harga terberat. Angkanya terus naik di bar yang sedang berjalan |
| `IDC_Pressure_Toolkit.pine` | IDC Pressure | Bubble redup/terang, kotak big trade + angka, titik absorption di wick, garis absorption, dan level psikologis |

Contoh tampilan pada data NQ 15 detik simulasi ada di `preview.png` dan `preview-ptk.png`.

## Syarat

1. **Paket TradingView Premium atau Ultimate.** `request.footprint()` hanya jalan di paket ini; di paket lain script langsung error. Chart detik (15s) juga butuh paket ini.
2. **Data real-time CME** untuk NQ/ES (langganan data CME di TradingView). Tanpa itu datanya delay 10 menit.
3. Simbol yang punya volume, misalnya `CME_MINI:NQ1!`, `CME_MINI:ES1!`, `COMEX:GC1!`, atau crypto.

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
- update tick-per-tick (zona berhenti di tick yang menyentuh, angka lingkaran naik live).

Jalankan:

```
cd tools/pine-test
npm install
node run.mjs --verbose          # 14 skenario
node run.mjs --render           # + gambar di tools/pine-test/out/
node preview.mjs out/preview.png
```

PineTS bukan TradingView. Hasil akhirnya tetap perlu dicek sekali di TradingView setelah dipasang.
