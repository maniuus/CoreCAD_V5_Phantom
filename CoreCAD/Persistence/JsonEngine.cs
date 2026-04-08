using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
                    if (ent == null || !XDataHelper.HasIdentity(ent)) continue;

                    var data = XDataHelper.GetFullCache(ent);
                    var coreEnt = new CoreEntity
                    {
                        Guid = data.guid,
                        RoleId = data.role,
                        ParentId = data.parent,
                        TemplateId = data.role,
                        SourceFile = GetValidPath(db),
                        Geometry = GeometryEngine.ExtractEntityGeometry(ent, data.role)
                    };
                    coreEnt.Mark = data.mark;
                    coreEnt.VersionId = data.version;

                    coreEnt.Attributes["layer"] = ent.Layer;
                    coreEnt.Attributes["color"] = ent.ColorIndex.ToString();
                    entities.Add(coreEnt);
                }
                tr.Commit();
            }
            return entities;
        }

        public static string GetValidPath(Database db) => CoreJSONEngine.GetValidPath(db);

        public static void SyncWithLibrary(List<CoreEntity> entities) { /* Phase 5 placeholder */ }

        public static void GlobalSync(bool dryRun) 
        {
            // Proxy ke CoreJSONEngine di masa depan
        }

        public static void SerializeProject(List<CoreEntity> entities)
        {
            var master = LoadProject() ?? new ProjectMaster();
            string currentFile = entities.Count > 0 ? entities[0].SourceFile : string.Empty;
            if (!string.IsNullOrEmpty(currentFile))
                master.Entities.RemoveAll(e => e.SourceFile.Equals(currentFile, StringComparison.OrdinalIgnoreCase));

            master.Entities.AddRange(entities);
            File.WriteAllText(ProjectContext.GetMasterJsonPath(), JsonConvert.SerializeObject(master, GetSettings()));
        }

        public static ProjectMaster? LoadProject()
        {
            string path = ProjectContext.GetMasterJsonPath();
            return File.Exists(path) ? JsonConvert.DeserializeObject<ProjectMaster>(File.ReadAllText(path)) : null;
        }

        public static int PullSyncDrawing(Database db)
        {
            var master = LoadProject();
            if (master == null || master.Entities == null) return 0;
            
            int count = 0;
            var currentFile = GetValidPath(db);
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                foreach (ObjectId id in ms)
                {
                    var ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                    if (ent == null || !XDataHelper.HasIdentity(ent)) continue;

                    var cand = XDataHelper.GetGuid(ent);
                    var json = master.Entities.FirstOrDefault(e => e.Guid == cand && e.SourceFile.Equals(currentFile, StringComparison.OrdinalIgnoreCase));
                    if (json != null)
                    {
                        GeometryEngine.ApplyEntityGeometry(ent, json.Geometry, json.RoleId);
                        count++;
                    }
                }
                tr.Commit();
            }
            return count;
        }

        public static void SetViewBounds(Database db, double minX, double minY, double maxX, double maxY, string handle) { }
        public static int ScanVisibleEntities(Database db) => 0;

        public static string ExportBQ() => "Phase 5 BQ is under development.";

        private static JsonSerializerSettings GetSettings() => new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            ContractResolver = new DefaultContractResolver { NamingStrategy = new SnakeCaseNamingStrategy() }
        };
    }
}
