================================================================================
          STANDARD CODING & BEST PRACTICES: coreCAD ENGINE
================================================================================
Versi          : 2026.04
Bahasa         : C# (.NET Framework / .NET Core)
Prinsip Utama  : Clean Code, Modular, & Error-Proof
--------------------------------------------------------------------------------

1. NAMING CONVENTIONS (ATURAN PENAMAAN)
---------------------------------------
- PascalCase: Digunakan untuk Class, Method, dan Public Property.
  Contoh: 'class SmartWall', 'void CalculateVolume()', 'string MaterialId'.
- camelCase: Digunakan untuk variabel lokal dan parameter fungsi.
  Contoh: 'double wallWidth', 'string jsonPath'.
- Underscore Prefix (_): Digunakan untuk private field/variabel internal class.
  Contoh: 'private string _guid;'.
- UpperCASE: Digunakan untuk konstanta (Constant).
  Contoh: 'public const string APP_NAME = "CORECAD_ENGINE";'.

2. PROJECT STRUCTURE (FOLDER & NAMESPACE)
-----------------------------------------
Pemisahan folder dilakukan berdasarkan tanggung jawab logika (Separation of Concerns):
- CoreCAD.Core      : Logika dasar (Database, Transactions, Logger, JSON Services).
- CoreCAD.Modules   : Fitur spesifik (Architecture, Structural, MEP).
- CoreCAD.UI        : Antarmuka pengguna (Ribbon, Palettes, WPF Dialogs).
- CoreCAD.Overrule  : Implementasi kelas-kelas Overrule API.

3. THE "SACRED TRANSACTION" TEMPLATE
------------------------------------
Setiap fungsi yang memodifikasi database AutoCAD WAJIB menggunakan pola ini:

[CommandMethod("CC_YOURCOMMAND")]
public void CommandTemplate()
{
    Document doc = Application.DocumentManager.MdiActiveDocument;
    Database db = doc.Database;

    using (Transaction tr = db.TransactionManager.StartTransaction())
    {
        try 
        {
            // 1. Validasi Input/Data JSON
            // 2. Operasi Database (Create/Update/Delete)
            // 3. Injeksi XData/XRecord coreCAD
            
            tr.Commit(); // Eksekusi hanya jika semua lancar
            doc.Editor.WriteMessage("\n[coreCAD] Perintah berhasil dieksekusi.");
        }
        catch (System.Exception ex)
        {
            tr.Abort(); // Batalkan semua perubahan jika terjadi error
            CoreCAD.Logger.Write(ex); // Catat log error secara otomatis
            doc.Editor.WriteMessage("\n[coreCAD ERROR] Proses dibatalkan: " + ex.Message);
        }
    }
}

4. ENTITY MODELING (INHERITANCE)
--------------------------------
Semua objek cerdas coreCAD harus diturunkan dari Base Class yang sama:

public abstract class CoreCADEntity 
{
    public Guid CoreCAD_ID { get; set; } = Guid.NewGuid(); // KTP Objek
    public string LevelId { get; set; }                   // Anchor Lantai
    public double PseudoZ { get; set; }                   // Elevasi Logis

    // Fungsi wajib untuk semua modul
    public abstract double GetVolume(); 
    public abstract void SyncFromJSON();
}

5. KOMENTAR & DOKUMENTASI (XML DOCS)
------------------------------------
Wajib menyertakan ringkasan fungsi menggunakan XML Comments (///) agar muncul 
sebagai panduan saat coding (Intellisense).

/// <summary>
/// Menghitung volume bersih dengan pengurangan void/lubang.
/// </summary>
/// <returns>Volume dalam meter kubik (m3)</returns>
public double GetNetVolume() { ... }

6. MEMORY MANAGEMENT (LAZY LOADING)
-----------------------------------
- Jangan biarkan objek JSON terbuka terus-menerus.
- Gunakan 'using' statement saat membaca file eksternal agar stream memori 
  langsung ditutup setelah digunakan.
- Hindari loop di dalam loop saat memproses ribuan GUID; gunakan Dictionary 
  untuk pencarian data yang lebih cepat.

--------------------------------------------------------------------------------
"Write code as if the person who ends up maintaining it is a violent 
psychopath who knows where you live." - Software Engineering Motto
================================================================================