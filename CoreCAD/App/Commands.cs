using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using CoreCAD.Core;
using CoreCAD.Persistence;
using System;
using System.Collections.Generic;
using System.Linq;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CoreCAD.App
{
    public class Commands
    {
        [CommandMethod("COREINIT")]
        public void CoreInit()
        {
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;

            ed.WriteMessage("\n============================================================");
            ed.WriteMessage("\nCoreCAD Solo-Mach Engine V5.0 Initialized.");
            ed.WriteMessage($"\nProject Root: {ProjectContext.ProjectRoot}");
            ed.WriteMessage($"\nMaster Path: {ProjectContext.GetMasterJsonPath()}");
            ed.WriteMessage("\n============================================================");
        }

        [CommandMethod("XDATA")]
        public void XDataInspector()
        {
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            PromptEntityOptions peo = new PromptEntityOptions("\nSelect entity to inspect CoreCAD XData: ");
            PromptEntityResult per = ed.GetEntity(peo);

            if (per.Status != PromptStatus.OK) return;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Entity ent = (Entity)tr.GetObject(per.ObjectId, OpenMode.ForRead);
                var data = XDataHelper.GetFullCache(ent);
                
                if (string.IsNullOrEmpty(data.guid))
                {
                    ed.WriteMessage("\n[Result] Entity has no persistent CoreCAD identity.");
                }
                else
                {
                    ed.WriteMessage("\n--- CORE-CAD ENTITY METADATA ---");
                    ed.WriteMessage($"\nLogical Identity (GUID): {data.guid}");
                    ed.WriteMessage($"\nFunctional Role (RoleID): {data.role}");
                    ed.WriteMessage($"\nMark/Label: {data.mark}");
                    ed.WriteMessage($"\nGrouping Identity (ParentID): {data.parent}");
                    ed.WriteMessage("\n--------------------------------");
                }
                tr.Commit();
            }
        }

        [CommandMethod("COREGUID")]
        public void CoreGuid()
        {
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            PromptEntityOptions peo = new PromptEntityOptions("\nSelect entity to tag with CoreCAD ID: ");
            PromptEntityResult per = ed.GetEntity(peo);

            if (per.Status != PromptStatus.OK) return;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Entity ent = (Entity)tr.GetObject(per.ObjectId, OpenMode.ForWrite);
                string newGuid = Guid.NewGuid().ToString();
                
                // SetIdentity uses SmartObject as transport
                var dummy = new CoreCAD.Models.SmartObject { 
                    Guid = newGuid, 
                    RoleId = ArchitectureRoles.WallExt 
                };
                dummy.Dna.Mark = "W1";

                XDataHelper.SetIdentity(ent, dummy);
                
                ed.WriteMessage($"\n[Success] Entity tagged with Logical Identity: {newGuid}");
                tr.Commit();
            }
        }

        [CommandMethod("CORESYNC")]
        public void CoreSync()
        {
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Editor ed = doc.Editor;
            Database db = doc.Database;

            ed.WriteMessage("\n[Bridge] Memulai sinkronisasi data ke JSON...");

            try
            {
                var entities = JsonEngine.ScanDrawing(db);
                ed.WriteMessage($"\n[Bridge] {entities.Count} objek terdeteksi.");

                JsonEngine.SyncWithLibrary(entities);
                JsonEngine.SerializeProject(entities);
                
                ed.WriteMessage("\n[Bridge] Database Master berhasil diperbarui.");
                ed.WriteMessage($"\n[Bridge] Lokasi: {ProjectContext.GetMasterJsonPath()}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[Error] Gagal melakukan sinkronisasi: {ex.Message}");
            }
        }

        [CommandMethod("PULLSYNC")]
        public void PullSync()
        {
            // Redirect ke Engine V5.0 yang baru
            new CommandController().ExecutePull();
        }

        [CommandMethod("CHECK_INTEGRITY")]
        public void CheckIntegrity()
        {
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            var master = CoreJSONEngine.LoadMaster();
            var jsonGuids = new HashSet<string>(master.Select(m => m.Guid));

            int orphanCount = 0;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                foreach (ObjectId id in ms)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null || !XDataHelper.HasIdentity(ent)) continue;

                    string guid = XDataHelper.GetGuid(ent);
                    if (!jsonGuids.Contains(guid))
                    {
                        ent.UpgradeOpen();
                        ent.ColorIndex = 1; // RED
                        orphanCount++;
                    }
                }
                tr.Commit();
            }

            if (orphanCount > 0)
                ed.WriteMessage($"\n[Integrity] Found {orphanCount} Orphan objects (marked in RED).");
            else
                ed.WriteMessage("\n[Integrity] All synced objects are healthy!");
        }
    }
}
