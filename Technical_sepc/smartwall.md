================================================================================
          SPESIFIKASI TEKNIS: SMARTWALL V5 (ARCHITECTURE MODULE)
================================================================================
Versi          : 2026.04.10
Module         : CoreCAD.Modules.Architecture
Base Class     : CoreCADEntity
Logic Engine   : DrawJig (Phantom Preview)
--------------------------------------------------------------------------------

1. DATA MODEL STRUCTURE (SmartWall.cs)
--------------------------------------
Setiap objek SmartWall wajib menyimpan properti fisik berikut:
- Thickness (double) : Diambil dari JSON standar (misal: 150mm).
- Height    (double) : Tinggi dinding (misal: 3500mm).
- StartPoint (Point3d) & EndPoint (Point3d).

Fungsi Kalkulasi (Physics-Based):
- NetVolume: $$V = \frac{Length \times Thickness \times Height}{10^9}$$ (Output: m3).
- WallArea : $$A = \frac{Length \times Height}{10^6}$$ (Output: m2).

2. PHANTOM ENGINE (WallDrawJig.cs)
----------------------------------
Gunakan 'DrawJig' (bukan EntityJig) agar kita bisa menggambar Polygon (kotak) 
secara real-time, bukan sekadar garis as.

Logika Vektor (Physics Tip):
- Hitung 'Direction Vector' dari Start ke End.
- Hitung 'Normal Vector' (90 derajat dari arah dinding).
- Offset titik Start dan End ke kiri dan kanan sebesar (Thickness / 2).
- Draw : worldGeom.Polygon(offsetPoints) untuk memunculkan bayangan dinding.

3. ALUR EKSEKUSI (CC_WALL Command)
----------------------------------
Wajib menggunakan 'TransactionHelper.ExecuteAtomic' dengan urutan:

A. PRE-PROCESS:
   - Ambil 'DefaultThickness' dan 'MaterialId' dari JsonService.
   - Minta user klik StartPoint.

B. JIG PROCESS:
   - Jalankan 'WallDrawJig'.
   - Update 'Length' secara dinamis berdasarkan pergerakan kursor.
   - Tunggu user klik EndPoint (Accept).

C. COMMIT PROCESS (Inside ExecuteAtomic):
   - Buat objek 'Line' atau 'Polyline' sebagai representasi database.
   - Tambahkan ke BlockTableRecord.
   - Panggil 'XDataManager.SetIdentity' untuk menyuntikkan:
     * GUID (Identitas Unik)
     * MaterialID (Link ke JSON)
     * LevelID (Identitas Lantai)
     * PseudoZ (Elevasi Logis)

4. VALIDASI & ERROR HANDLING
----------------------------
- Minimum Length: Dinding tidak boleh dibuat jika Length < 10mm.
- Z-Constraint: Pastikan EndPoint.Z dipaksa mengikuti StartPoint.Z 
  (atau mengikuti PseudoZ) agar dinding tetap horizontal secara data.
- Atomic Rollback: Jika 'SetIdentity' gagal, transaksi wajib batal otomatis 
  agar tidak ada "Dinding Tanpa KTP" di dalam gambar.

5. INTEGRASI BOQ (BILL OF QUANTITY)
-----------------------------------
- Tool ini harus siap dibaca oleh perintah 'CC_REPORT'.
- Setiap kali 'Length' berubah (melalui Grip edit), fungsi 'GetVolume' 
  harus bisa dipanggil ulang secara instan.

--------------------------------------------------------------------------------
"Build your code like a structural beam; rigid in logic, flexible in function."
================================================================================