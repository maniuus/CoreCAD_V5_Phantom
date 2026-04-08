using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using CoreCAD.Core;
using CoreCAD.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace CoreCAD.Persistence
{
    public static class JsonEngine
    {
        /// <summary>
        /// 1. SCANNING: Mengumpulkan semua GUID dan Role dari layar AutoCAD.
        /// </summary>
        public static List<CoreEntity> ScanDrawing(Database db)
        {
            var entities = new List<CoreEntity>();

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                foreach (ObjectId id in ms)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null) continue;

                    if (XDataManager.HasCoreCADIdentity(ent))
                    {
                        var data = XDataManager.GetCoreData(ent);
                        
                        var coreEnt = new CoreEntity
                        {
                            Guid = data.guid,
                            RoleId = data.roleId,
                            ParentId = data.parentId,
                            TemplateId = data.roleId, // Default TemplateID as RoleID
                            SourceFile = GetValidPath(db), // Track which file this came from
                            Geometry = GeometryEngine.ExtractEntityGeometry(ent, data.roleId)
                        };
                        coreEnt.Geometry.LocalZ = data.localZ; // Maintain true vertical data from XData

                        // Add basic attributes
                        coreEnt.Attributes["layer"] = ent.Layer;
                        coreEnt.Attributes["color"] = ent.ColorIndex.ToString();

                        entities.Add(coreEnt);
                    }
                }
                tr.Commit();
            }

            return entities;
        }

        public static string GetValidPath(Database db)
        {
            // Coba urutan prioritas:
            // 1. Properti Filename langsung
            string fullPath = db.Filename;
            
            // 2. Properti OriginalFileName (jika ReadDwgFile atau side-loading)
            if (string.IsNullOrEmpty(fullPath)) fullPath = db.OriginalFileName;

            // 3. DocumentManager (Kasus Active Document)
            if (string.IsNullOrEmpty(fullPath))
            {
                foreach (Document d in Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager)
                {
                    if (d.Database == db)
                    {
                        fullPath = d.Name;
                        break;
                    }
                }
            }
            
            // 4. Fallback Active Doc (Kasus Darurat)
            if (string.IsNullOrEmpty(fullPath))
            {
                var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                if (doc != null) fullPath = doc.Name;
            }

            if (string.IsNullOrEmpty(fullPath)) return "unsaved_document.dwg";
            
            // Sanitasi: Pastikan kita ambil hanya nama file-nya saja
            try
            {
                return Path.GetFileName(fullPath).ToLower();
            }
            catch
            {
                return "invalid_path_format.dwg";
            }
        }

        private static JsonSerializerSettings GetSettings()
        {
            return new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                ContractResolver = new DefaultContractResolver
                {
                    NamingStrategy = new SnakeCaseNamingStrategy()
                }
            };
        }

        /// <summary>
        /// 2. SERIALIZING: Mengubah daftar objek tersebut menjadi file project_master.json.
        /// Menggunakan logika Composite Key (GUID + SourceFile) untuk mencegah data overwrite antar view.
        /// </summary>
        public static void SerializeProject(List<CoreEntity> entities)
        {
            // 1. Load data lama
            var master = LoadProject() ?? new ProjectMaster();
            
            // 2. Identifikasi file sumber aktif
            string currentFile = entities.Count > 0 ? entities[0].SourceFile : string.Empty;

            // 3. LOGIKA MERGE (Composite Key):
            // Hapus entri lama yang berasal dari FILE yang sama, tapi PERTAHANKAN yang dari file lain.
            if (!string.IsNullOrEmpty(currentFile))
            {
                master.Entities.RemoveAll(e => e.SourceFile.Equals(currentFile, StringComparison.OrdinalIgnoreCase));
            }

            // 4. LOGIKA GLOBAL PROP SYNC (Propagation):
            // Jika ada perubahan pada properti fisik (Width/Height), sebarkan ke view lain dengan GUID sama.
            foreach (var newEnt in entities)
            {
                // Cari "saudara" objek ini di file/view lain
                var relatives = master.Entities.FindAll(e => e.Guid == newEnt.Guid);
                foreach (var relative in relatives)
                {
                    // Sinkronisasi Properti Fisik (Shared)
                    relative.Geometry.Width = newEnt.Geometry.Width;
                    relative.Geometry.Height = newEnt.Geometry.Height;
                    relative.Geometry.LocalZ = newEnt.Geometry.LocalZ;
                    relative.Geometry.SlopePercentage = newEnt.Geometry.SlopePercentage;
                    relative.RoleId = newEnt.RoleId;
                    relative.ParentId = newEnt.ParentId;

                    // Physical DNA Broad Sync (OOD Framework)
                    relative.Mark = newEnt.Mark;
                    relative.Material = newEnt.Material;
                    relative.PhysicsNote = newEnt.PhysicsNote;
                    relative.VersionId = newEnt.VersionId;
                    
                    // Sinkronisasi Atribut (Material, Spesifikasi, dll)
                    foreach (var attr in newEnt.Attributes)
                    {
                        if (attr.Key == "layer" || attr.Key == "handle") continue; 
                        relative.Attributes[attr.Key] = attr.Value;
                    }
                    
                    // JANGAN sinkronisasi LocalX, LocalY, Rotation karena tiap view berbeda
                }
            }

            // 5. Masukkan entitas baru ke Master
            master.Entities.AddRange(entities);

            // 6. Update Meta
            master.ProjectInfo.ProjectName = Path.GetFileNameWithoutExtension(ProjectContext.ProjectRoot);
            master.ProjectInfo.LastSyncTimestamp = DateTime.Now;

            // 7. Save
            string jsonPath = ProjectContext.GetMasterJsonPath();
            string jsonContent = JsonConvert.SerializeObject(master, GetSettings());
            File.WriteAllText(jsonPath, jsonContent);
        }

        /// <summary>
        /// 3. MAPPING: Menghubungkan GUID tersebut dengan parameter standar dari library_standards.json.
        /// </summary>
        public static void SyncWithLibrary(List<CoreEntity> entities)
        {
            string libPath = Path.Combine(ProjectContext.DataFolder, ProjectIdentities.LibStandards);
            if (!File.Exists(libPath)) return;

            try
            {
                string libJson = File.ReadAllText(libPath);
                var templates = JsonConvert.DeserializeObject<List<ObjectTemplate>>(libJson);
                if (templates == null) return;

                foreach (var entity in entities)
                {
                    var template = templates.Find(t => t.TemplateId == entity.TemplateId);
                    if (template != null)
                    {
                        // Enforce template name or update specific attributes from library
                        foreach (var param in template.DefaultParameters)
                        {
                            if (!entity.Attributes.ContainsKey(param.Key))
                            {
                                entity.Attributes[param.Key] = param.Value.ToString();
                            }
                        }
                    }
                }
            }
            catch
            {
                // Silently skip if library is corrupted or missing
            }
        }
        /// <summary>
        /// 4. DESERIALIZING: Membaca file project_master.json kembali ke objek.
        /// </summary>
        public static ProjectMaster? LoadProject()
        {
            string jsonPath = ProjectContext.GetMasterJsonPath();
            if (!File.Exists(jsonPath)) return null;

            string jsonContent = File.ReadAllText(jsonPath);
            return JsonConvert.DeserializeObject<ProjectMaster>(jsonContent);
        }

        /// <summary>
        /// 5. PULL SYNC: Mengupdate geometri CAD berdasarkan data dari JSON.
        /// </summary>
        /// <summary>
        /// 5. PULL SYNC (Active Document): Mengupdate geometri CAD dokument aktif.
        /// </summary>
        public static int PullSyncDrawing(Database db)
        {
            var master = LoadProject();
            if (master == null) return 0;
            return ProcessDatabase(db, master, true); // Active doc allowed to generate
        }

        /// <summary>
        /// BRICK 6: THE PHANTOM ENGINE (BATCH PROCESSOR)
        /// Memproses banyak file tanpa membukanya di UI AutoCAD.
        /// </summary>
        public static void GlobalSync(bool dryRun)
        {
            var master = LoadProject();
            if (master == null || master.ProjectFiles == null || master.ProjectFiles.Count == 0) return;

            foreach (string path in master.ProjectFiles)
            {
                if (!File.Exists(path)) continue;

                // 1. Buat Database "Hantu" (Tanpa UI)
                using (Database sideDb = new Database(false, true))
                {
                    try
                    {
                        // 2. Baca file ke RAM
                        sideDb.ReadDwgFile(path, FileOpenMode.OpenForReadAndWriteNoShare, true, "");
                        sideDb.CloseInput(true);

                        // 3. Jalankan Logic Sinkronisasi (Hanya update objek eksis)
                        int updated = ProcessDatabase(sideDb, master, false); 

                        if (updated > 0 && !dryRun)
                        {
                            // 4. Backup Safety
                            string backupFolder = Path.Combine(Path.GetDirectoryName(path) ?? "", "backup");
                            if (!Directory.Exists(backupFolder)) Directory.CreateDirectory(backupFolder);
                            string backupPath = Path.Combine(backupFolder, Path.GetFileName(path) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".bak");
                            File.Copy(path, backupPath, true);

                            // 5. Save Secara Diam-diam
                            sideDb.SaveAs(path, DwgVersion.Current);
                        }
                    }
                    catch (System.Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error processing {path}: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Logic inti sinkronisasi data dari CoreCAD Master ke Database AutoCAD manapun.
        /// </summary>
        private static int ProcessDatabase(Database db, ProjectMaster master, bool allowGeneration)
        {
            if (master.Entities == null) return 0;

            var currentFileName = GetValidPath(db);
            
            // Ambil data clipping area untuk file ini jika ada
            BoundData? clippingArea = null;
            if (master.ProjectInfo.ViewBounds.TryGetValue(currentFileName, out var bounds))
            {
                clippingArea = bounds;
                
                // REFRESH DINAMIS: Jika ada Handle, ambil extents terbaru dari AutoCAD
                if (!string.IsNullOrEmpty(bounds.BoundHandle))
                {
                    try
                    {
                        using (var tr = db.TransactionManager.StartTransaction())
                        {
                            long handleVal = Convert.ToInt64(bounds.BoundHandle, 16);
                            Handle h = new Handle(handleVal);
                            ObjectId id = db.GetObjectId(false, h, 0);
                            
                            if (!id.IsNull && id.IsValid)
                            {
                                var ent = (Entity)tr.GetObject(id, OpenMode.ForRead);
                                var ext = ent.GeometricExtents;
                                clippingArea.MinX = ext.MinPoint.X;
                                clippingArea.MinY = ext.MinPoint.Y;
                                clippingArea.MaxX = ext.MaxPoint.X;
                                clippingArea.MaxY = ext.MaxPoint.Y;
                            }
                            tr.Commit();
                        }
                    }
                    catch { /* Jika handle tidak valid, gunakan Min/Max lama */ }
                }
            }

            // OPTIMASI: Dictionary untuk lookup fisik (Global) untuk Smart Labels
            var globalLookup = new Dictionary<string, CoreEntity>();
            foreach (var e in master.Entities)
            {
                if (!globalLookup.ContainsKey(e.Guid) || e.ViewType == "PLAN")
                    globalLookup[e.Guid] = e;
            }

            // Lookup spesifik file aktif (Composite Key)
            var jsonLookup = new Dictionary<string, CoreEntity>();
            foreach (var e in master.Entities)
            {
                if (!string.IsNullOrEmpty(e.Guid) && e.SourceFile.Equals(currentFileName, StringComparison.OrdinalIgnoreCase))
                {
                    jsonLookup[e.Guid] = e;
                }
            }

            var updatedGuids = new HashSet<string>();
            int resultCount = 0;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                // LANGKAH 1: UPDATE OBJEK EKSISTING
                foreach (ObjectId id in ms)
                {
                    var ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                    if (ent == null || !XDataManager.HasCoreCADIdentity(ent)) continue;

                    var cadData = XDataManager.GetCoreData(ent);
                    if (jsonLookup.TryGetValue(cadData.guid, out var jsonData))
                    {
                        // 1. LOGIKA PENGHAPUSAN (ERASE)
                        bool isOrphanLabel = !string.IsNullOrEmpty(jsonData.TargetGuid) && 
                                             globalLookup.TryGetValue(jsonData.TargetGuid, out var target) && 
                                             target.IsDeleted;

                        // 2. LOGIKA CLIPPING (Kalo objek di luar area, kita anggap sembunyi)
                        bool isOutside = clippingArea != null && (
                            jsonData.Geometry.LocalX < clippingArea.MinX || 
                            jsonData.Geometry.LocalX > clippingArea.MaxX ||
                            jsonData.Geometry.LocalY < clippingArea.MinY ||
                            jsonData.Geometry.LocalY > clippingArea.MaxY);

                        if (jsonData.IsDeleted || isOrphanLabel || isOutside)
                        {
                            ent.Erase();
                            updatedGuids.Add(jsonData.Guid);
                            resultCount++;
                            continue;
                        }

                        // SMART LABEL RESOLUTION
                        if (!string.IsNullOrEmpty(jsonData.TargetGuid) && !string.IsNullOrEmpty(jsonData.LabelFormat))
                        {
                            if (globalLookup.TryGetValue(jsonData.TargetGuid, out var targetEnt))
                            {
                                string resolved = ResolveLabelContent(jsonData.LabelFormat, targetEnt);
                                GeometryEngine.ApplyEntityGeometry(ent, jsonData.Geometry, jsonData.RoleId, resolved);
                            }
                        }
                        else
                        {
                            GeometryEngine.ApplyEntityGeometry(ent, jsonData.Geometry, jsonData.RoleId);
                        }

                        XDataManager.SetCoreData(ent, jsonData.Guid, jsonData.RoleId, jsonData.ParentId, jsonData.Geometry.LocalZ);
                        updatedGuids.Add(jsonData.Guid);
                        resultCount++;
                    }
                }

                // LANGKAH 2: GENERATE BARU
                if (allowGeneration)
                {
                    foreach (var jsonEnt in master.Entities)
                    {
                        if (jsonEnt.IsDeleted) continue;

                        if (jsonEnt.SourceFile.Equals(currentFileName, StringComparison.OrdinalIgnoreCase) && !updatedGuids.Contains(jsonEnt.Guid))
                        {
                            // SPATIAL CLIPPING CHECK
                            if (clippingArea != null && (
                                jsonEnt.Geometry.LocalX < clippingArea.MinX || 
                                jsonEnt.Geometry.LocalX > clippingArea.MaxX ||
                                jsonEnt.Geometry.LocalY < clippingArea.MinY ||
                                jsonEnt.Geometry.LocalY > clippingArea.MaxY))
                            {
                                continue; // Di luar area, abaikan
                            }

                            Entity? newEnt = GeometryEngine.CreateNewEntityFromRole(jsonEnt.RoleId);
                            if (newEnt != null)
                            {
                                ms.AppendEntity(newEnt);
                                tr.AddNewlyCreatedDBObject(newEnt, true);
                                XDataManager.SetCoreData(newEnt, jsonEnt.Guid, jsonEnt.RoleId, jsonEnt.ParentId, jsonEnt.Geometry.LocalZ);

                                string? resolved = null;
                                if (!string.IsNullOrEmpty(jsonEnt.TargetGuid) && globalLookup.TryGetValue(jsonEnt.TargetGuid, out var target))
                                {
                                    if (target.IsDeleted) continue; 
                                    resolved = ResolveLabelContent(jsonEnt.LabelFormat, target);
                                }
                                
                                GeometryEngine.ApplyEntityGeometry(newEnt, jsonEnt.Geometry, jsonEnt.RoleId, resolved);
                                resultCount++;
                            }
                        }
                    }
                }
                tr.Commit();
            }
            return resultCount;
        }

        public static void SetViewBounds(Database db, double minX, double minY, double maxX, double maxY, string boundHandle = "")
        {
            var master = LoadProject() ?? new ProjectMaster();
            string fileName = GetValidPath(db);
            
            master.ProjectInfo.ViewBounds[fileName] = new BoundData 
            { 
                MinX = minX, MinY = minY, MaxX = maxX, MaxY = maxY,
                BoundHandle = boundHandle
            };
            
            SerializeProject(master.Entities); 
        }

        public static int ScanVisibleEntities(Database db)
        {
            var master = LoadProject();
            if (master == null || master.Entities == null) return 0;

            string currentFileName = GetValidPath(db);
            if (!master.ProjectInfo.ViewBounds.TryGetValue(currentFileName, out var bounds))
                return -1; // No boundary set

            int discoveredCount = 0;
            var currentFileEntities = master.Entities
                .Where(e => e.SourceFile.Equals(currentFileName, StringComparison.OrdinalIgnoreCase))
                .Select(e => e.Guid)
                .ToHashSet();

            // Saring SEMUA entitas proyek
            var candidateEntities = master.Entities
                .Where(e => !e.IsDeleted && !currentFileEntities.Contains(e.Guid))
                .ToList();

            var newEntries = new List<CoreEntity>();

            foreach (var candidate in candidateEntities)
            {
                // FILTER SPASIAL (X, Y, Z)
                bool isInside = (candidate.Geometry.LocalX >= bounds.MinX && candidate.Geometry.LocalX <= bounds.MaxX) &&
                                (candidate.Geometry.LocalY >= bounds.MinY && candidate.Geometry.LocalY <= bounds.MaxY) &&
                                (candidate.Geometry.LocalZ >= bounds.MinZ && candidate.Geometry.LocalZ <= bounds.MaxZ);

                if (isInside)
                {
                    // Discovery: Clone entitas untuk view ini (Linked by GUID)
                    var clone = new CoreEntity
                    {
                        Guid = candidate.Guid,
                        RoleId = candidate.RoleId,
                        ParentId = candidate.ParentId,
                        TemplateId = candidate.TemplateId,
                        SourceFile = currentFileName,
                        ViewType = "AUTO_DISCOVERED",
                        Geometry = new GeometryData
                        {
                            LocalX = candidate.Geometry.LocalX,
                            LocalY = candidate.Geometry.LocalY,
                            LocalZ = candidate.Geometry.LocalZ,
                            Width = candidate.Geometry.Width,
                            Height = candidate.Geometry.Height,
                            Length = candidate.Geometry.Length,
                            Rotation = candidate.Geometry.Rotation,
                            SlopePercentage = candidate.Geometry.SlopePercentage
                        },
                        Attributes = new Dictionary<string, string>(candidate.Attributes)
                    };
                    
                    newEntries.Add(clone);
                    discoveredCount++;
                }
            }

            if (newEntries.Count > 0)
            {
                master.Entities.AddRange(newEntries);
                SerializeProject(newEntries); // Simpan hanya entry baru (smart merge akan handle sisanya)
            }

            return discoveredCount;
        }

        private static string ResolveLabelContent(string format, CoreEntity target)
        {
            if (string.IsNullOrEmpty(format)) return string.Empty;

            string resolved = format;
            resolved = resolved.Replace("{z}", target.Geometry.LocalZ.ToString("N2"));
            resolved = resolved.Replace("{w}", target.Geometry.Width.ToString("N0"));
            resolved = resolved.Replace("{h}", target.Geometry.Height.ToString("N0"));
            resolved = resolved.Replace("{slope}", target.Geometry.SlopePercentage.ToString("N1") + "%");
            resolved = resolved.Replace("{guid}", target.Guid);
            
            return resolved;
        }

        public static string ExportBQ()
        {
            var master = LoadProject();
            if (master == null || master.Entities == null) return "Error: Project not found.";

            // ANTI-DOUBLE COUNT: Group by GUID
            var bqItems = master.Entities
                .GroupBy(e => e.Guid)
                .Select(g => new
                {
                    Guid = g.Key,
                    Role = g.First().RoleId,
                    Width = g.Max(e => e.Geometry.Width),
                    Height = g.Max(e => e.Geometry.Height),
                    LocalZ = g.First(e => e.ViewType == "PLAN" || true).Geometry.LocalZ,
                    SourceFiles = string.Join("|", g.Select(e => e.SourceFile).Distinct())
                })
                .ToList();

            string csvPath = Path.Combine(ProjectContext.DataFolder, "BQ_Report_" + DateTime.Now.ToString("yyyyMMdd") + ".csv");
            
            using (var writer = new StreamWriter(csvPath))
            {
                writer.WriteLine("GUID,Role,Width,Height,Elevation,AppearsInFiles");
                foreach (var item in bqItems)
                {
                    writer.WriteLine($"{item.Guid},{item.Role},{item.Width},{item.Height},{item.LocalZ},{item.SourceFiles}");
                }
            }

            return csvPath;
        }
    }
}
