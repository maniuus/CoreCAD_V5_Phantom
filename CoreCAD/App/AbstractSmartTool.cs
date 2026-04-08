using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using CoreCAD.Models;
using CoreCAD.Persistence;
using System;

namespace CoreCAD.App
{
    public abstract class AbstractSmartTool
    {
        public void Execute()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            try
            {
                // 1. Get User Input
                if (!GetUserInput(ed)) return;

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    // 2. Draw Visual
                    Entity ent = DrawVisual(db);
                    if (ent == null) return;

                    ms.AppendEntity(ent);
                    tr.AddNewlyCreatedDBObject(ent, true);

                    // 3. Create Sync Data & Identity
                    string guid = Guid.NewGuid().ToString();
                    SmartObject obj = CreateSyncData(ent, guid);
                    
                    // 4. Set Identity (XData)
                    XDataHelper.SetIdentity(ent, obj);

                    // 5. Sync to JSON Master (Explicit)
                    Persistence.CoreJSONEngine.SyncObject(obj);

                    tr.Commit();
                    ed.WriteMessage($"\n[CoreCAD] Berhasil membuat objek: {obj.RoleId} ({guid})");
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[Error] Gagal menjalankan tool: {ex.Message}");
            }
        }

        protected abstract bool GetUserInput(Editor ed);
        protected abstract Entity DrawVisual(Database db);
        protected abstract SmartObject CreateSyncData(Entity ent, string guid);
    }
}
