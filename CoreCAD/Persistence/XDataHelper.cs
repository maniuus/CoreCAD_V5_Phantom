using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using CoreCAD.Models;
using System;
using System.Linq;

namespace CoreCAD.Persistence
{
    public static class XDataHelper
    {
        public static string RegAppName => CoreCAD.Core.ProjectIdentities.RegAppName;

        private static void RegisterApp(Database db)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
                if (!rat.Has(RegAppName))
                {
                    rat.UpgradeOpen();
                    var ratr = new RegAppTableRecord { Name = RegAppName };
                    rat.Add(ratr);
                    tr.AddNewlyCreatedDBObject(ratr, true);
                }
                tr.Commit();
            }
        }

        public static void SetIdentity(Entity ent, SmartObject obj, string parentGuid = "")
        {
            Database db = ent.Database;
            RegisterApp(db);

            // XData Cache: GUID (1000), Role (1000), Mark (1000), Version (1071), Parent (1000)
            using (var rb = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, RegAppName),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, obj.Guid),         // 0: GUID
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, obj.RoleId),       // 1: Role
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, obj.Dna.Mark ?? ""),     // 2: Mark
                new TypedValue((int)DxfCode.ExtendedDataInteger32, obj.Instances.FirstOrDefault()?.VersionId ?? 1), // 3: Version
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, parentGuid)       // 4: Parent GUID
            ))
            {
                ent.XData = rb;
            }
        }

        public static void ResetIdentityForClone(Entity ent)
        {
            var cache = GetQuickCache(ent);
            if (string.IsNullOrEmpty(cache.guid)) return;

            // Kita hapus GUID lama, tapi biarkan RoleID & Mark agar PUSH tahu tipenya
            SetIdentity(ent, new SmartObject { Guid = string.Empty, RoleId = cache.role, Dna = new PhysicalDNA { Mark = cache.mark } });
        }

        public static string GetGuid(Entity ent)
        {
            ResultBuffer rb = ent.GetXDataForApplication(RegAppName);
            if (rb == null) return string.Empty;

            var values = rb.AsArray();
            if (values.Length > 1)
                return values[1].Value?.ToString() ?? string.Empty;

            return string.Empty;
        }

        public static (string guid, string role, string mark, int version, string parent) GetFullCache(Entity ent)
        {
            ResultBuffer rb = ent.GetXDataForApplication(RegAppName);
            if (rb == null) return (string.Empty, string.Empty, string.Empty, 0, string.Empty);

            var v = rb.AsArray();
            // Index 1: GUID, Index 2: Role, Index 3: Mark, Index 4: Version, Index 5: Parent
            string g = v.Length > 1 ? v[1].Value?.ToString() ?? "" : "";
            string r = v.Length > 2 ? v[2].Value?.ToString() ?? "" : "";
            string m = v.Length > 3 ? v[3].Value?.ToString() ?? "" : "";
            int vId  = v.Length > 4 ? Convert.ToInt32(v[4].Value) : 0;
            string p = v.Length > 5 ? v[5].Value?.ToString() ?? "" : "";

            return (g, r, m, vId, p);
        }

        public static (string guid, string role, string mark) GetQuickCache(Entity ent)
        {
            ResultBuffer rb = ent.GetXDataForApplication(RegAppName);
            if (rb == null) return (string.Empty, string.Empty, string.Empty);
            
            var v = rb.AsArray();
            // Index 1 = GUID, Index 2 = Role, Index 3 = Mark
            return (
                v.Length > 1 ? v[1].Value?.ToString() ?? "" : "", 
                v.Length > 2 ? v[2].Value?.ToString() ?? "" : "", 
                v.Length > 3 ? v[3].Value?.ToString() ?? "" : ""
            );
        }

        public static bool HasIdentity(Entity ent)
        {
            return !string.IsNullOrEmpty(GetGuid(ent));
        }
    }
}
