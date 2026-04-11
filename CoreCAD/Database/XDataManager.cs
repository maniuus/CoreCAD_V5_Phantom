using Autodesk.AutoCAD.DatabaseServices;
using CoreCAD.Models;
using Newtonsoft.Json;

// CATATAN ARSITEKTUR: Namespace sengaja dibuat CoreCAD.Persistence (bukan CoreCAD.Database)
// karena nama 'Database' bentrok dengan class Autodesk.AutoCAD.DatabaseServices.Database.
// Folder fisiknya tetap bernama 'Database' di disk.
namespace CoreCAD.Persistence
{
    /// <summary>
    /// [V5 DATABASE LAYER] Menyuntikkan dan membaca KTP (WallData) dari entitas AutoCAD via XData.
    /// App Name baru: "CORECAD_MODEL" — terpisah dari "CORECAD_ENGINE" milik sistem lama.
    /// </summary>
    public static class XDataManager
    {
        public const string APP_NAME = "CORECAD_MODEL";

        /// <summary>
        /// Suntikkan WallData ke XData sebuah entitas dalam format Hybrid (Legacy Markers + JSON Chunks).
        /// </summary>
        public static void AttachWallData(Transaction tr, Autodesk.AutoCAD.DatabaseServices.Database db, Entity ent, WallData data)
        {
            // Pastikan RegApp terdaftar
            RegAppTable rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (!rat.Has(APP_NAME))
            {
                rat.UpgradeOpen();
                RegAppTableRecord ratr = new RegAppTableRecord();
                ratr.Name = APP_NAME;
                rat.Add(ratr);
                tr.AddNewlyCreatedDBObject(ratr, true);
            }

            // Serialize data ke JSON
            string json = JsonConvert.SerializeObject(data);

            // [V5 ROBUST HYBRID]
            // Kita simpan marker lama agar scanner versi lama tetap bisa baca ID & Tebal.
            ResultBuffer rb = new ResultBuffer();
            rb.Add(new TypedValue((int)DxfCode.ExtendedDataRegAppName, APP_NAME));
            rb.Add(new TypedValue((int)DxfCode.ExtendedDataAsciiString, "TYPE:WALL"));
            rb.Add(new TypedValue((int)DxfCode.ExtendedDataAsciiString, $"ID:{data.Id}")); // Legacy Marker
            rb.Add(new TypedValue((int)DxfCode.ExtendedDataReal, data.Thickness));       // Legacy Marker

            // Baru kemudian Chunks JSON (untuk Opening, dll)
            int chunkSize = 240; 
            for (int i = 0; i < json.Length; i += chunkSize)
            {
                int length = Math.Min(chunkSize, json.Length - i);
                string chunk = json.Substring(i, length);
                rb.Add(new TypedValue((int)DxfCode.ExtendedDataAsciiString, chunk));
            }

            // [V5 SAFETY] Hanya upgrade jika objek sudah ada di database (bukan objek baru)
            if (ent.Database != null && !ent.IsWriteEnabled)
                ent.UpgradeOpen();
            ent.XData = rb;
            rb.Dispose();
        }

        /// <summary>
        /// Sempurnakan pembacaan JSON: Gabungkan chunk dan laporkan error jika ada.
        /// </summary>
        public static WallData? GetWallData(Entity ent)
        {
            string fullJson = GetRawJSON(ent);
            
            // Fallback: Jika tidak ada JSON, coba cari Legacy ID
            if (string.IsNullOrEmpty(fullJson))
            {
                ResultBuffer? rb = ent.GetXDataForApplication(APP_NAME);
                if (rb != null)
                {
                    string legacyId = "";
                    double legacyThickness = 150.0;
                    bool foundLegacy = false;
                    foreach (TypedValue tv in rb.AsArray())
                    {
                        if (tv.TypeCode == (int)DxfCode.ExtendedDataAsciiString)
                        {
                            string s = tv.Value.ToString() ?? "";
                            if (s.StartsWith("ID:")) { legacyId = s.Substring(3); foundLegacy = true; }
                        }
                        else if (tv.TypeCode == (int)DxfCode.ExtendedDataReal)
                        {
                            legacyThickness = (double)tv.Value;
                        }
                    }
                    rb.Dispose();
                    if (foundLegacy) return new WallData(legacyId, legacyThickness);
                }
                return null;
            }

            try 
            { 
                return JsonConvert.DeserializeObject<WallData>(fullJson); 
            } 
            catch (JsonException ex) 
            { 
#if DEBUG
                var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                doc?.Editor.WriteMessage($"\n[JSON ERROR]: {ex.Message}");
#endif
                return null; 
            }
        }

        /// <summary>
        /// Ambil string JSON mentah dari XData Chunks.
        /// </summary>
        public static string GetRawJSON(Entity ent)
        {
            ResultBuffer? rb = ent.GetXDataForApplication(APP_NAME);
            if (rb == null) return "";

            string fullJson = "";
            bool foundStart = false;

            foreach (TypedValue tv in rb.AsArray())
            {
                if (tv.TypeCode == (int)DxfCode.ExtendedDataAsciiString)
                {
                    string s = tv.Value.ToString() ?? "";
                    
                    // Identifikasi awal JSON
                    if (s.StartsWith("{")) foundStart = true;

                    if (foundStart)
                    {
                        fullJson += s;
                    }
                }
            }
            rb.Dispose();
            return fullJson;
        }
        /// <summary>
        /// Sempurnakan Proxy Line dengan metadata Linkage ke Dinding Induk.
        /// </summary>
        public static void AttachGripData(Transaction tr, Autodesk.AutoCAD.DatabaseServices.Database db, Entity ent, string wallHandle, int opIndex)
        {
            RegAppTable rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (!rat.Has(APP_NAME))
            {
                rat.UpgradeOpen();
                RegAppTableRecord ratr = new RegAppTableRecord { Name = APP_NAME };
                rat.Add(ratr);
                tr.AddNewlyCreatedDBObject(ratr, true);
            }

            ResultBuffer rb = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, APP_NAME),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, "TYPE:GRIP"),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, $"HOST:{wallHandle}"),
                new TypedValue((int)DxfCode.ExtendedDataInteger32, opIndex)
            );

            // [V5 SAFETY] Jangan panggil UpgradeOpen pada objek baru (Grip yang belum di-append)
            if (ent.Database != null && !ent.IsWriteEnabled)
                ent.UpgradeOpen();
            ent.XData = rb;
            rb.Dispose();
        }

        /// <summary>
        /// Ambil data linkage dari sebuah Proxy Line.
        /// </summary>
        public static (string hostHandle, int opIndex)? GetGripData(Entity ent)
        {
            ResultBuffer? rb = ent.GetXDataForApplication(APP_NAME);
            if (rb == null) return null;

            string hostHandle = "";
            int opIndex = -1;
            bool isGrip = false;

            foreach (TypedValue tv in rb.AsArray())
            {
                if (tv.TypeCode == (int)DxfCode.ExtendedDataAsciiString)
                {
                    string s = tv.Value.ToString() ?? "";
                    if (s == "TYPE:GRIP") isGrip = true;
                    else if (s.StartsWith("HOST:")) hostHandle = s.Substring(5);
                }
                else if (tv.TypeCode == (int)DxfCode.ExtendedDataInteger32)
                {
                    opIndex = (int)tv.Value;
                }
            }
            rb.Dispose();

            if (isGrip && !string.IsNullOrEmpty(hostHandle))
                return (hostHandle, opIndex);

            return null;
        }
    }
}
