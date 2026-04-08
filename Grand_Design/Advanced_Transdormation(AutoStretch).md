============================================================
BRICK 4: ADVANCED TRANSFORMATION (AUTO-STRETCH)
============================================================

1. LOGIKA UTAMA: VERTEX MANIPULATION
Jangan gunakan perintah 'Offset' karena akan membuat objek baru 
dan menghapus GUID. Gunakan manipulasi titik sudut (Vertex):
- Tangkap koordinat Vertex dari Polyline/Line.
- Hitung Vektor Normal (tegak lurus) terhadap Centerline.
- Geser Vertex secara matematis sesuai Delta Width dari JSON.

2. KONSEP ANCHOR POINT (TITIK JANGKAR)
Tentukan arah "melar" objek di dalam kodingan:
- Center Anchor : Melar simetris ke dua arah (Pipa/Dinding As).
- Side Anchor   : Satu sisi diam, sisi lain bergeser (Dinding Kolom).
- Rumus: New_Vertex = Old_Vertex + (Normal_Vector * (Delta_Width / 2))

3. PENANGANAN JUNCTION (TITIK TEMU)
Masalah: Saat dinding menebal, pojokan (L-Join/T-Join) bisa lepas.
Solusi: 
- Gunakan Parent_ID untuk mengidentifikasi objek yang bertamu.
- Jalankan fungsi 'Auto-Extend/Trim' pada vertex yang memiliki 
  koordinat yang sama (shared vertex) setelah stretching.

4. PARAMETER FISIK (DATA-DRIVEN)
- Width     : Diambil dari 'library_standards.json'.
- Thickness : Diambil dari 'library_standards.json'.
- Elevation : Menentukan posisi visual pada view Potongan (Section).

5. PASSING GRADE (STANDAR KELULUSAN)
[ ] Zero Distortion: Sudut objek tidak berubah, hanya lebar/tebal.
[ ] Anchor Stability: Objek tidak berpindah lokasi (tetap di As).
[ ] Hatch Persistence: Hatching tidak 'broken' setelah di-stretch.
[ ] Precision: Akurasi hasil stretch di CAD vs JSON < 0.0001mm.

6. WORKFLOW EKSEKUSI
1. Ganti 'width' dinding di project_master.json (misal 150 -> 200).
2. Ketik PULLSYNC di AutoCAD.
3. Mesin mencari GUID -> Identifikasi Role -> Hitung Normal Vector.
4. Vertex digeser otomatis -> 200 halaman ter-update serentak.
============================================================