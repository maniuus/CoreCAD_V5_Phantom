============================================================
FRAMEWORK: OBJECT-ORIENTED DRAFTING (OOD) - CORE-CAD V5.0
============================================================

1. FILOSOFI DATA: "SATU JIWA, BANYAK RAGA"
- Jiwa (Physical Data): Disimpan satu kali di JSON (Global).
- Raga (Instance Data): Muncul berkali-kali di berbagai DWG (Lokal).

2. STRUKTUR PROPERTI WAJIB (JSON SCHEMA)
------------------------------------------------------------
A. IDENTITY SYSTEM (Kunci Utama)
   - GUID        : String (Unique ID global, tidak boleh berubah).
   - Role_ID     : String (Kategori: 'DOOR', 'WALL', 'COLUMN').
   - Version_ID  : Integer (Tracking revisi otomatis).

B. PHYSICAL DNA (Data Engineering - Sifat Global)
   - Mark        : String (Label schedule, misal: 'D1', 'K1').
   - Width       : Double (Dimensi fisik lebar).
   - Height      : Double (Dimensi fisik tinggi).
   - Material    : String (Spesifikasi teknis bahan).
   - Physics_Note: String (Catatan teknis beban/kekuatan).

C. INSTANCE REPRESENTATION (Data Visual - Sifat Per View)
   - Source_File : String (Nama file .dwg tempat objek berada).
   - View_Type   : String ('PLAN', 'SECTION', 'DETAIL').
   - Local_Pos   : Vector3D (Koordinat X, Y, Z di file tersebut).
   - Rotation    : Double (Sudut putar objek).
   - Flip_State  : Boolean (Mirror status untuk arah bukaan).

3. LOGIKA OPERASIONAL (C# RULES)
------------------------------------------------------------
- CREATE  : Generate GUID baru + Gambar visual + PUSH ke JSON.
- UPDATE  : Jika Data Physical berubah, cari SEMUA GUID yang sama 
            di seluruh file dan update dimensinya.
- DELETE  : 'Soft Delete' di JSON. Jika dihapus di Denah Utama, 
            berikan flag 'Removed' agar Phantom Engine menghapus 
            visualnya di 199 file lainnya.
- SYNC    : Selalu bandingkan data Lokal CAD vs Data Global JSON. 
            Data Global di JSON selalu menang (SSOT).

4. IMPLEMENTASI TOOLS (Essential List)
- Smart Door  : Menghasilkan gambar denah & data lebar otomatis.
- Smart Wall  : Menghasilkan garis ganda & data volume material.
- Smart Column: Menghasilkan profil kolom & data elevasi struktur.

5. PENAMAAN (CONVENTION)
- Role ID selalu UPPERCASE (Contoh: MEP_PIPE_CW).
- Unit menggunakan Milimeter (mm) untuk konsistensi BQ.
============================================================