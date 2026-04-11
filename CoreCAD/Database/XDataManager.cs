using Autodesk.AutoCAD.DatabaseServices;
using CoreCAD.Models;

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
        /// Suntikkan WallData ke XData sebuah entitas.
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

            // Tulis data ke ResultBuffer
            ResultBuffer rb = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, APP_NAME),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, "TYPE:WALL"),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, $"ID:{data.Id}"),
                new TypedValue((int)DxfCode.ExtendedDataReal, data.Thickness)
            );

            ent.UpgradeOpen();
            ent.XData = rb;
            rb.Dispose();
        }

        /// <summary>
        /// Baca WallData dari XData entitas. Kembalikan null jika bukan entitas CoreCAD.
        /// </summary>
        public static WallData? GetWallData(Entity ent)
        {
            ResultBuffer? rb = ent.GetXDataForApplication(APP_NAME);
            if (rb == null) return null;

            string id = "";
            double thickness = 150.0;

            TypedValue[] values = rb.AsArray();
            foreach (TypedValue tv in values)
            {
                if (tv.TypeCode == (int)DxfCode.ExtendedDataAsciiString)
                {
                    string strVal = tv.Value.ToString() ?? "";
                    if (strVal.StartsWith("ID:")) id = strVal.Substring(3);
                }
                else if (tv.TypeCode == (int)DxfCode.ExtendedDataReal)
                {
                    thickness = (double)tv.Value;
                }
            }

            rb.Dispose();

            if (string.IsNullOrEmpty(id)) return null;
            return new WallData(id, thickness);
        }
    }
}
