using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using CoreCAD.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace CoreCAD.Persistence
{
    public static class CoreJSONEngine
    {
        private static JsonSerializerSettings GetSettings()
        {
            return new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore,
                ContractResolver = new DefaultContractResolver { NamingStrategy = new SnakeCaseNamingStrategy() }
            };
        }

        public static List<SmartObject> LoadMaster()
        {
            string path = ProjectContext.GetMasterJsonPath();
            if (!File.Exists(path)) return new List<SmartObject>();

            try
            {
                string json = File.ReadAllText(path);
                var project = JsonConvert.DeserializeObject<ProjectMaster_V5>(json, GetSettings());
                return project?.Entities ?? new List<SmartObject>();
            }
            catch { return new List<SmartObject>(); }
        }

        public static void SyncObject(SmartObject incoming)
        {
            SyncBatch(new List<SmartObject> { incoming });
        }

        public static void SyncBatch(List<SmartObject> incomingList)
        {
            if (incomingList == null || incomingList.Count == 0) return;

            var masterEntities = LoadMaster();
            
            // Konversi ke Dictionary dengan Case-Insensitive (Mencegah Duplikasi A vs a)
            var masterMap = new Dictionary<string, SmartObject>(StringComparer.OrdinalIgnoreCase);
            foreach(var ent in masterEntities)
            {
                if (!string.IsNullOrEmpty(ent.Guid))
                    masterMap[ent.Guid] = ent; // Last one wins
            }
            
            foreach (var incoming in incomingList)
            {
                if (string.IsNullOrEmpty(incoming.Guid)) continue;

                if (masterMap.TryGetValue(incoming.Guid, out var existing))
                {
                    // 1. UPDATE PHYSICAL DNA (GLOBAL)
                    existing.Dna = incoming.Dna;
                    existing.RoleId = incoming.RoleId;
                    existing.ParentId = incoming.ParentId;
                    existing.IsDeleted = incoming.IsDeleted;

                    // 2. UPDATE/ADD INSTANCE (LOCAL)
                    foreach (var incomingInst in incoming.Instances)
                    {
                        var existingInst = existing.Instances.FirstOrDefault(i => 
                            i.SourceFile.Equals(incomingInst.SourceFile, StringComparison.OrdinalIgnoreCase));
                        
                        if (existingInst != null)
                        {
                            existingInst.Geometry = incomingInst.Geometry;
                            existingInst.ViewType = incomingInst.ViewType;
                            existingInst.VersionId++;
                        }
                        else
                        {
                            existing.Instances.Add(incomingInst);
                        }
                    }
                }
                else
                {
                    masterMap[incoming.Guid] = incoming;
                }
            }

            SaveMaster(masterMap.Values.ToList());
        }

        private static void SaveMaster(List<SmartObject> entities)
        {
            string path = ProjectContext.GetMasterJsonPath();
            var project = new ProjectMaster_V5 { Entities = entities };
            string json = JsonConvert.SerializeObject(project, GetSettings());

            int retryCount = 0;
            while (retryCount < 5)
            {
                try
                {
                    File.WriteAllText(path, json);
                    break; // Sukses
                }
                catch (IOException)
                {
                    retryCount++;
                    System.Threading.Thread.Sleep(100); // Tunggu 100ms
                }
            }
        }

        /// <summary>
        /// Mendapatkan nama file yang valid untuk kunci JSON (Lowered)
        /// Digunakan sebagai SourceFile pada InstanceData.
        /// </summary>
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
                foreach (Autodesk.AutoCAD.ApplicationServices.Document d in Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager)
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
        public static void CleanOrphanedData()
        {
            var master = LoadMaster();
            int removedCount = 0;
            string root = ProjectContext.ProjectRoot;

            if (string.IsNullOrEmpty(root)) return;

            foreach (var entity in master)
            {
                // 1. Hapus Instance yang file-nya sudah tidak ada di disk
                var validInstances = entity.Instances.Where(inst =>
                {
                    // Coba cari file di dalam folder Project Root
                    string fullPath = Path.Combine(root, inst.SourceFile);
                    bool exists = File.Exists(fullPath);
                    
                    if (!exists) removedCount++;
                    return exists;
                }).ToList();

                entity.Instances = validInstances;
            }

            // 2. Hapus DNA yang sudah tidak punya raga (Zero Instances)
            int beforeCount = master.Count;
            master.RemoveAll(e => e.Instances.Count == 0);
            int dnaRemoved = beforeCount - master.Count;

            if (removedCount > 0 || dnaRemoved > 0)
            {
                SaveMaster(master);
                
                var ed = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument?.Editor;
                ed?.WriteMessage($"\n[CoreCAD] Clean-up: {removedCount} Instansi Hantu dan {dnaRemoved} DNA Yatim-Piatu dihapus.");
            }
        }
    }

    public class ProjectMaster_V5
    {
        public List<SmartObject> Entities { get; set; } = new();
    }
}
