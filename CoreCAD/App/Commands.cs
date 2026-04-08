using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using CoreCAD.Core;
using CoreCAD.Persistence;
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
            ed.WriteMessage($"\nData Folder: {ProjectContext.DataFolder}");
            ed.WriteMessage("\n============================================================");
        }

        [CommandMethod("XDATA")]
        public void XDataInspector()
        {
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            Autodesk.AutoCAD.DatabaseServices.Database db = doc.Database;

            PromptEntityOptions peo = new PromptEntityOptions("\nSelect entity to inspect CoreCAD XData: ");
            PromptEntityResult per = ed.GetEntity(peo);

            if (per.Status != PromptStatus.OK) return;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Entity ent = (Entity)tr.GetObject(per.ObjectId, OpenMode.ForRead);
                var (guid, roleId, parentId, localZ) = XDataManager.GetCoreData(ent);
                
                if (string.IsNullOrEmpty(guid))
                {
                    ed.WriteMessage("\n[Result] Entity has no persistent CoreCAD identity.");
                }
                else
                {
                    ed.WriteMessage("\n--- CORE-CAD ENTITY METADATA ---");
                    ed.WriteMessage($"\nLogical Identity (GUID): {guid}");
                    ed.WriteMessage($"\nFunctional Role (RoleID): {roleId}");
                    ed.WriteMessage($"\nGrouping Identity (ParentID): {parentId}");
                    ed.WriteMessage($"\nPhysical Elevation (LocalZ): {localZ}");
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
            Autodesk.AutoCAD.DatabaseServices.Database db = doc.Database;

            PromptEntityOptions peo = new PromptEntityOptions("\nSelect entity to tag with CoreCAD ID: ");
            PromptEntityResult per = ed.GetEntity(peo);

            if (per.Status != PromptStatus.OK) return;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Entity ent = (Entity)tr.GetObject(per.ObjectId, OpenMode.ForWrite);
                string newGuid = Guid.NewGuid().ToString();
                
                // Using the specific Role ID defined in Constants
                XDataManager.SetCoreData(ent, newGuid, ArchitectureRoles.WallExt, "none", 0.0);
                
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
            Autodesk.AutoCAD.DatabaseServices.Database db = doc.Database;

            ed.WriteMessage("\n[Bridge] Memulai sinkronisasi data ke JSON...");

            try
            {
                // 1. Scanning
                var entities = JsonEngine.ScanDrawing(db);
                ed.WriteMessage($"\n[Bridge] {entities.Count} objek terdeteksi.");

                // 2. Mapping
                JsonEngine.SyncWithLibrary(entities);
                ed.WriteMessage("\n[Bridge] Sinkronisasi dengan library standards selesai.");

                // 3. Serializing
                JsonEngine.SerializeProject(entities);
                
                ed.WriteMessage("\n[Bridge] Database 'project_master.json' berhasil diperbarui.");
                ed.WriteMessage($"\n[Bridge] Lokasi: {ProjectContext.GetMasterJsonPath()}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[Error] Gagal melakukan sinkronisasi: {ex.Message}");
            }
        }
        [CommandMethod("VALIDATE_GEO")]
        public void ValidateGeo()
        {
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            Autodesk.AutoCAD.DatabaseServices.Database db = doc.Database;

            PromptEntityOptions peo = new PromptEntityOptions("\nSelect entity to validate CoreCAD Geometry: ");
            PromptEntityResult per = ed.GetEntity(peo);

            if (per.Status != PromptStatus.OK) return;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Entity ent = (Entity)tr.GetObject(per.ObjectId, OpenMode.ForRead);
                var data = XDataManager.GetCoreData(ent);
                
                if (string.IsNullOrEmpty(data.guid))
                {
                    ed.WriteMessage("\n[Result] Entity has no identity! Use COREGUID first.");
                }
                else
                {
                    var geo = GeometryEngine.ExtractEntityGeometry(ent, data.roleId);
                    
                    ed.WriteMessage("\n--- GEOMETRY VALIDATION ---");
                    ed.WriteMessage($"\nRole: {data.roleId}");
                    ed.WriteMessage($"\nLocal X: {geo.LocalX}");
                    ed.WriteMessage($"\nLocal Y: {geo.LocalY}");
                    ed.WriteMessage($"\nRotation: {geo.Rotation}");
                    ed.WriteMessage($"\nWidth/Length: {geo.Width}");
                    ed.WriteMessage($"\nHeight: {geo.Height}");
                    ed.WriteMessage("\n---------------------------");
                }
                tr.Commit();
            }
        }
        [CommandMethod("PULLSYNC")]
        public void PullSync()
        {
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Editor ed = doc.Editor;
            Autodesk.AutoCAD.DatabaseServices.Database db = doc.Database;

            ed.WriteMessage("\n[Bridge] Memulai penarikan data dari JSON ke CAD...");

            try
            {
                int updated = JsonEngine.PullSyncDrawing(db);
                
                if (updated > 0)
                {
                    ed.WriteMessage($"\n[Bridge] Sukses! {updated} objek telah diperbarui dari SSOT.");
                    doc.Editor.Regen(); // Refresh display
                }
                else
                {
                    ed.WriteMessage("\n[Bridge] Tidak ada perubahan yang ditemukan atau file JSON tidak ditemukan.");
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[Error] Gagal melakukan Pull Sync: {ex.Message}");
            }
        }
        [CommandMethod("SLOPE_SYNC")]
        public void SlopeSync()
        {
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Editor ed = doc.Editor;
            
            // 1. Ask for Anchor Point (Starting GUID)
            PromptStringOptions pso = new PromptStringOptions("\nMasukkan GUID Anchor (Upstream/Hulu): ");
            PromptResult pr = ed.GetString(pso);
            if (pr.Status != PromptStatus.OK) return;
            string startGuid = pr.StringResult;

            ed.WriteMessage("\n[Engine] Memulai kalkulasi 2.5D Slope Chain...");

            try
            {
                // 2. Load and Propagate in JSON
                var entities = JsonEngine.ScanDrawing(doc.Database); // Get latest from Drawing to merge if needed, but the user wants to calculate in JSON
                // Actually, let's load from JSON as SSOT
                var master = JsonEngine.LoadProject();
                if (master == null || master.Entities == null)
                {
                    ed.WriteMessage("\n[Error] File project_master.json tidak ditemukan.");
                    return;
                }

                // 3. Run Calculation Logic
                SlopeSolver.PropagateChain(master.Entities, startGuid);
                
                // 4. Save Back to JSON
                JsonEngine.SerializeProject(master.Entities);
                ed.WriteMessage("\n[Engine] Kalkulasi selesai. Data hulu ke hilir telah diperbarui di JSON.");

                // 5. Trigger PullSync to update CAD visuals
                ed.WriteMessage("\n[Bridge] Melakukan update visual di AutoCAD...");
                JsonEngine.PullSyncDrawing(doc.Database);
                doc.Editor.Regen();
                
                ed.WriteMessage("\n[Success] Seluruh sistem telah sinkron.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[Error] Gagal melakukan Slope Sync: {ex.Message}");
            }
        }

        [CommandMethod("GLOBAL_SYNC")]
        public void GlobalSync()
        {
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Editor ed = doc.Editor;
            ed.WriteMessage("\n[Phantom] Memulai BATCH PROCESS (Global Sync)...");

            try
            {
                // Safety confirm
                PromptKeywordOptions pko = new PromptKeywordOptions("\nJalankan Batch Update (Headless) pada seluruh file proyek? [Yes/No/Dryrun]: ", "Yes No Dryrun");
                pko.AllowNone = false;
                PromptResult pr = ed.GetKeywords(pko);

                if (pr.Status != PromptStatus.OK || pr.StringResult == "No") return;

                bool dryRun = (pr.StringResult == "Dryrun");
                
                // Execute
                JsonEngine.GlobalSync(dryRun);

                ed.WriteMessage(dryRun 
                    ? "\n[Phantom] DRY-RUN selesai. Cek log untuk melihat simulasi perubahan." 
                    : "\n[Phantom] BATCH PROCESS selesai. Semua file telah diproses dan di-backup.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[Error] Gagal melakukan Global Sync: {ex.Message}");
            }
        }

        [CommandMethod("EXPORT_BQ")]
        public void ExportBQ()
        {
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Editor ed = doc.Editor;
            ed.WriteMessage("\n[BQ Engine] Mengekstrak Bill of Quantity ke CSV...");

            try
            {
                string csvPath = JsonEngine.ExportBQ();
                ed.WriteMessage($"\n[BQ Engine] Berhasil! Laporan disimpan di: {csvPath}");
                
                // Open the folder (Optional)
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{csvPath}\"");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[Error] Gagal ekspor BQ: {ex.Message}");
            }
        }

        [CommandMethod("REANNOTATE")]
        public void Reannotate()
        {
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Editor ed = doc.Editor;
            ed.WriteMessage("\n[Smart Label] Sinkronisasi label aktif...");

            try
            {
                int count = JsonEngine.PullSyncDrawing(doc.Database);
                ed.WriteMessage($"\n[Smart Label] {count} objek & label telah disinkronkan.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[Error] Gagal sinkronisasi label: {ex.Message}");
            }
        }

        [CommandMethod("SET_VIEW_BOUNDS")]
        public void SetViewBounds()
        {
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            PromptKeywordOptions pko = new PromptKeywordOptions("\nPilih metode boundary [Select-entity/Points]: ", "Select Points");
            pko.AllowNone = false;
            PromptResult pr = ed.GetKeywords(pko);
            if (pr.Status != PromptStatus.OK) return;

            double minX = 0, minY = 0, maxX = 0, maxY = 0;
            string handleStr = "";

            if (pr.StringResult == "Select")
            {
                PromptEntityOptions peo = new PromptEntityOptions("\nPilih objek pembatas (Polyline/Rectangle): ");
                PromptEntityResult per = ed.GetEntity(peo);
                if (per.Status != PromptStatus.OK) return;

                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    var ent = (Entity)tr.GetObject(per.ObjectId, OpenMode.ForRead);
                    var ext = ent.GeometricExtents;
                    minX = ext.MinPoint.X;
                    minY = ext.MinPoint.Y;
                    maxX = ext.MaxPoint.X;
                    maxY = ext.MaxPoint.Y;
                    handleStr = ent.Handle.ToString();
                    tr.Commit();
                }
            }
            else
            {
                PromptPointOptions ppo1 = new PromptPointOptions("\nKlik sudut pertama area clipping: ");
                PromptPointResult ppr1 = ed.GetPoint(ppo1);
                if (ppr1.Status != PromptStatus.OK) return;

                PromptCornerOptions pco = new PromptCornerOptions("\nKlik sudut kedua area clipping: ", ppr1.Value);
                PromptPointResult ppr2 = ed.GetCorner(pco);
                if (ppr2.Status != PromptStatus.OK) return;

                minX = Math.Min(ppr1.Value.X, ppr2.Value.X);
                minY = Math.Min(ppr1.Value.Y, ppr2.Value.Y);
                maxX = Math.Max(ppr1.Value.X, ppr2.Value.X);
                maxY = Math.Max(ppr1.Value.Y, ppr2.Value.Y);
            }

            JsonEngine.SetViewBounds(doc.Database, minX, minY, maxX, maxY, handleStr);
            ed.WriteMessage($"\n[View] Area clipping berhasil diset: ({minX:N0},{minY:N0}) ke ({maxX:N0},{maxY:N0})");
            if (!string.IsNullOrEmpty(handleStr)) ed.WriteMessage($"\n[View] Terhubung ke Entity Handle: {handleStr}");
            ed.WriteMessage("\n[View] Jalankan PULLSYNC untuk sinkronisasi area.");
        }

        [CommandMethod("SCANVISIBLE")]
        public void ScanVisible()
        {
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            ed.WriteMessage("\n[Scanner] Mencari objek global yang masuk ke area view ini...");

            try
            {
                int count = JsonEngine.ScanVisibleEntities(doc.Database);
                
                if (count == -1)
                {
                    ed.WriteMessage("\n[Error] Batas area (View Bounds) belum diset. Gunakan SET_VIEW_BOUNDS dulu.");
                }
                else
                {
                    ed.WriteMessage($"\n[Scanner] Selesai! Menemukan {count} objek baru untuk view ini.");
                    ed.WriteMessage("\n[Scanner] Jalankan PULLSYNC untuk menampilkan objek tersebut.");
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[Error] Gagal melakukan scan: {ex.Message}");
            }
        }
    }
}
