using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using CoreCAD.Engine;

namespace CoreCAD.Commands
{
    /// <summary>
    /// [V5 COMMAND] CC_REBUILD — Tombol Refresh utama arsitektur data-driven.
    /// Satu perintah, satu transaksi: Purge view lama → Bake view baru.
    /// </summary>
    public class CmdRebuild
    {
        [CommandMethod("CC_REBUILD")]
        public void RebuildAll()
        {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            var db  = doc.Database;
            Editor ed = doc.Editor;

            using (Transaction tr = doc.TransactionManager.StartTransaction())
            {
                try
                {
                    ed.WriteMessage("\n[CoreCAD] Purging view lama...");
                    ViewGenerator.PurgeOldViews(db, tr);

                    ed.WriteMessage("\n[CoreCAD] Baking view baru...");
                    ViewGenerator.BakeAllWalls(db, tr);

                    tr.Commit();
                    ed.WriteMessage("\n[CoreCAD] ✓ Rebuild Selesai! Semua dinding tercetak sempurna.");
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\n[Error CC_REBUILD]: {ex.Message}");
                    tr.Abort();
                }
            }
        }
    }
}
