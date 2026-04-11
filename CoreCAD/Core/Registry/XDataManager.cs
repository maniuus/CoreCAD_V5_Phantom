using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using CoreCAD.Core.Diagnostics;
using System;

namespace CoreCAD.Core.Registry
{
    /// <summary>
    /// Manages the CORECAD_ENGINE XData registry and identity injection.
    /// Hardened for V5 Junction Protocol with Transaction-safe 'Silent Guards'.
    /// </summary>
    public static class XDataManager
    {
        public const string RegAppName = "CORECAD_ENGINE";
        public const string GroupRegAppName = "CORECAD_GROUP";

        public static void EnsureRegApp(Database db, Transaction tr)
        {
            if (db == null) return;
            RegAppTable rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (!rat.Has(RegAppName))
            {
                SafeUpgradeOpen(rat);
                RegAppTableRecord ratr = new RegAppTableRecord { Name = RegAppName };
                rat.Add(ratr);
                tr.AddNewlyCreatedDBObject(ratr, true);
            }
            if (!rat.Has(GroupRegAppName))
            {
                SafeUpgradeOpen(rat);
                RegAppTableRecord ratr = new RegAppTableRecord { Name = GroupRegAppName };
                rat.Add(ratr);
                tr.AddNewlyCreatedDBObject(ratr, true);
            }
        }

        public static void SetIdentity(Entity ent, Guid guid, string materialId, string levelId, double pseudoZ, string role = "FOLLOWER", double thickness = 150.0)
        {
            SafeUpgradeOpen(ent);

            string guidString = guid.ToString("D").ToUpper();

            using (ResultBuffer rb = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, RegAppName),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, guidString),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, materialId),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, levelId),
                new TypedValue((int)DxfCode.ExtendedDataReal, pseudoZ),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, role),
                new TypedValue((int)DxfCode.ExtendedDataReal, thickness)
            ))
            {
                ent.XData = rb;
            }
        }

        public static (Guid guid, string materialId, string levelId, double pseudoZ, string role, double thickness)? GetIdentity(Entity ent)
        {
            using (ResultBuffer rb = ent.GetXDataForApplication(RegAppName))
            {
                if (rb == null) return null;

                TypedValue[] tvs = rb.AsArray();
                if (tvs.Length < 5) return null;

                try
                {
                    Guid guid = Guid.Parse(tvs[1].Value.ToString() ?? string.Empty);
                    string matId = tvs[2].Value.ToString() ?? string.Empty;
                    string lvlId = tvs[3].Value.ToString() ?? string.Empty;
                    double pZ = Convert.ToDouble(tvs[4].Value);
                    string role = tvs.Length > 5 ? (tvs[5].Value.ToString() ?? "FOLLOWER") : "FOLLOWER";
                    double thickness = tvs.Length > 6 ? Convert.ToDouble(tvs[6].Value) : 150.0;

                    return (guid, matId, lvlId, pZ, role, thickness);
                }
                catch
                {
                    return null;
                }
            }
        }

        public static bool HasIdentity(Entity ent)
        {
            using (ResultBuffer rb = ent.GetXDataForApplication(RegAppName)) { return rb != null; }
        }

        public static void CopyIdentity(Entity source, Entity target, Transaction tr)
        {
            var identity = GetIdentity(source);
            if (identity != null)
            {
                EnsureRegApp(target.Database, tr);
                SetIdentity(target, identity.Value.guid, identity.Value.materialId,
                            identity.Value.levelId, identity.Value.pseudoZ,
                            identity.Value.role, identity.Value.thickness);
            }
        }

        public static ObjectIdCollection FindEntitiesByGuid(Database db, Guid guid, Transaction tr)
        {
            ObjectIdCollection ids = new ObjectIdCollection();
            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
            foreach (ObjectId entId in btr)
            {
                if (entId.IsErased) continue;
                Entity ent = (Entity)tr.GetObject(entId, OpenMode.ForRead);
                var identity = GetIdentity(ent);
                if (identity != null && identity.Value.guid == guid) ids.Add(entId);
            }
            return ids;
        }

        public static void LinkChildren(Entity parent, System.Collections.Generic.IEnumerable<ObjectId> children, Transaction tr)
        {
            if (parent.ExtensionDictionary == ObjectId.Null) 
            { 
                SafeUpgradeOpen(parent); 
                parent.CreateExtensionDictionary(); 
            }
            DBDictionary dict = (DBDictionary)tr.GetObject(parent.ExtensionDictionary, OpenMode.ForWrite);
            Xrecord xrec;
            if (dict.Contains("CORECAD_CHILDREN")) xrec = (Xrecord)tr.GetObject(dict.GetAt("CORECAD_CHILDREN"), OpenMode.ForWrite);
            else { xrec = new Xrecord(); dict.SetAt("CORECAD_CHILDREN", xrec); tr.AddNewlyCreatedDBObject(xrec, true); }
            
            ResultBuffer rb = new ResultBuffer();
            foreach (ObjectId id in children) if (!id.IsNull) rb.Add(new TypedValue((int)DxfCode.Handle, id.Handle));
            xrec.Data = rb;
        }

        public static ObjectIdCollection GetChildren(Entity parent, Transaction tr)
        {
            ObjectIdCollection ids = new ObjectIdCollection();
            if (parent.ExtensionDictionary == ObjectId.Null) return ids;
            DBDictionary dict = (DBDictionary)tr.GetObject(parent.ExtensionDictionary, OpenMode.ForRead);
            if (!dict.Contains("CORECAD_CHILDREN")) return ids;
            Xrecord xrec = (Xrecord)tr.GetObject(dict.GetAt("CORECAD_CHILDREN"), OpenMode.ForRead);
            using (ResultBuffer? rb = xrec.Data)
            {
                if (rb == null) return ids;
                foreach (TypedValue tv in rb) if (tv.TypeCode == (int)DxfCode.Handle && tv.Value != null)
                {
                    Handle h = new Handle(Convert.ToInt64(tv.Value.ToString(), 16));
                    if (parent.Database.TryGetObjectId(h, out ObjectId id)) ids.Add(id);
                }
            }
            return ids;
        }

        public static void SetGroupId(Entity ent, string groupId)
        {
            SafeUpgradeOpen(ent);
            using (ResultBuffer rb = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, GroupRegAppName),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, groupId)
            ))
            {
                ent.XData = rb;
            }
        }

        public static string GetGroupId(Entity ent)
        {
            using (ResultBuffer rb = ent.GetXDataForApplication(GroupRegAppName))
            {
                if (rb == null) return string.Empty;
                TypedValue[] tvs = rb.AsArray();
                return tvs.Length > 1 ? (tvs[1].Value.ToString() ?? string.Empty) : string.Empty;
            }
        }

        public static void SetParentGuids(Entity ent, string[] guids)
        {
            SafeUpgradeOpen(ent);
            string joined = string.Join(";", guids);
            using (ResultBuffer rb = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, RegAppName),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, "PARENTS:" + joined)
            ))
            {
                ent.XData = rb;
            }
        }

        public static string[] GetParentGuids(Entity ent)
        {
            using (ResultBuffer rb = ent.GetXDataForApplication(RegAppName))
            {
                if (rb == null) return Array.Empty<string>();
                TypedValue[] tvs = rb.AsArray();
                foreach (var tv in tvs)
                {
                    string val = tv.Value.ToString() ?? "";
                    if (val.StartsWith("PARENTS:"))
                    {
                        return val.Substring(8).Split(';', StringSplitOptions.RemoveEmptyEntries);
                    }
                }
            }
            return Array.Empty<string>();
        }

        /// <summary>
        /// THE SILENT GUARD: Safely upgrades an object to WRITE state or fails silently if already locked.
        /// Prevents eInvalidOpenState crashes in high-speed reactor cycles.
        /// </summary>
        public static void SafeUpgradeOpen(DBObject obj)
        {
            if (obj == null || obj.IsErased || obj.IsWriteEnabled) return;

            try
            {
                obj.UpgradeOpen();
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                // Capture eInvalidOpenState silently as it usually means a parent transaction is already active
                if (ex.ErrorStatus == ErrorStatus.InvalidOpenState)
                {
                    DebugLogger.Log($"[SILENT GUARD] Suppressed eInvalidOpenState for {obj.Handle}");
                }
                else
                {
                    // For other critical errors, we still want to know
                    DebugLogger.Error("Critical UpgradeOpen Failure", ex);
                }
            }
        }
    }
}
