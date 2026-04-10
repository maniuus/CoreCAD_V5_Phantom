using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using System;

namespace CoreCAD.Core.Registry
{
    /// <summary>
    /// Manages the CORECAD_ENGINE XData registry and identity injection.
    /// </summary>
    public static class XDataManager
    {
        public const string RegAppName = "CORECAD_ENGINE";

        /// <summary>
        /// Registers the CoreCAD AppID in the drawing if not already present.
        /// </summary>
        public static void EnsureRegApp(Database db, Transaction tr)
        {
            RegAppTable rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (!rat.Has(RegAppName))
            {
                rat.UpgradeOpen();
                RegAppTableRecord ratr = new RegAppTableRecord { Name = RegAppName };
                rat.Add(ratr);
                tr.AddNewlyCreatedDBObject(ratr, true);
            }
        }

        /// <summary>
        /// Injects standard identity XData into an entity.
        /// Standard Format: [1001] AppName, [1000] GUID (D format), [1000] MaterialID, [1000] LevelID, [1040] PseudoZ.
        /// </summary>
        public static void SetIdentity(Entity ent, Guid guid, string materialId, string levelId, double pseudoZ)
        {
            // Proteksi: Pastikan entitas OpenForWrite
            if (!ent.IsWriteEnabled)
            {
                ent.UpgradeOpen();
            }

            // Standarisasi GUID format "D" Uppercase
            string guidString = guid.ToString("D").ToUpper();

            using (ResultBuffer rb = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, RegAppName),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, guidString),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, materialId),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, levelId),
                new TypedValue((int)DxfCode.ExtendedDataReal, pseudoZ)
            ))
            {
                ent.XData = rb;
            }
        }
    }
}
