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
        /// </summary>
        public static void SetIdentity(Entity ent, string guid, string materialId, string levelId)
        {
            using (ResultBuffer rb = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, RegAppName),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, guid),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, materialId),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, levelId)
            ))
            {
                ent.XData = rb;
            }
        }
    }
}
