using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
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
            if (db == null) return;
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
        /// Standard Format: [1001] AppName, [1000] GUID, [1000] MaterialID, [1000] LevelID, [1040] PseudoZ, [1000] Role.
        /// </summary>
        public static void SetIdentity(Entity ent, Guid guid, string materialId, string levelId, double pseudoZ, string role = "FOLLOWER")
        {
            // Proteksi: Pastikan entitas OpenForWrite
            if (!ent.IsWriteEnabled)
            {
                ent.UpgradeOpen();
            }

            // Standarisasi GUID format "D" Uppercase
            string guidString = guid.ToString("D").ToUpper();

            using (ResultBuffer rb = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, RegAppName),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, guidString),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, materialId),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, levelId),
                new TypedValue((int)DxfCode.ExtendedDataReal, pseudoZ),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, role)
            ))
            {
                ent.XData = rb;
            }
        }

        /// <summary>
        /// Extracts CoreCAD Identity from an entity's XData.
        /// </summary>
        /// <returns>A tuple containing (Guid, MaterialId, LevelId, PseudoZ, Role). Returns null if no XData found.</returns>
        public static (Guid guid, string materialId, string levelId, double pseudoZ, string role)? GetIdentity(Entity ent)
        {
            using (ResultBuffer rb = ent.GetXDataForApplication(RegAppName))
            {
                if (rb == null) return null;

                TypedValue[] tvs = rb.AsArray();
                if (tvs.Length < 5) return null; // AppName + 4 pockets

                try
                {
                    Guid guid = Guid.Parse(tvs[1].Value.ToString() ?? string.Empty);
                    string matId = tvs[2].Value.ToString() ?? string.Empty;
                    string lvlId = tvs[3].Value.ToString() ?? string.Empty;
                    double pZ = Convert.ToDouble(tvs[4].Value);
                    string role = tvs.Length > 5 ? (tvs[5].Value.ToString() ?? "FOLLOWER") : "FOLLOWER";

                    return (guid, matId, lvlId, pZ, role);
                }
                catch
                {
                    return null;
                }
            }
        }

        /// <summary>
        /// Copies CoreCAD identity from one entity to another.
        /// </summary>
        public static void CopyIdentity(Entity source, Entity target, Transaction tr)
        {
            var identity = GetIdentity(source);
            if (identity != null)
            {
                EnsureRegApp(target.Database, tr);
                SetIdentity(target, identity.Value.guid, identity.Value.materialId, identity.Value.levelId, identity.Value.pseudoZ, identity.Value.role);
            }
        }

        /// <summary>
        /// Stores calculated joint vertices in an XRecord (Level 2 Cache).
        /// Standard Cache: 4 points (Start-Left, End-Left, End-Right, Start-Right) => 12 Double values.
        /// </summary>
        public static void SetJointCache(Entity ent, Point3dCollection points, Transaction tr)
        {
            if (points.Count < 4) return;

            // 1. Get/Create Extension Dictionary
            if (ent.ExtensionDictionary == ObjectId.Null)
            {
                ent.UpgradeOpen();
                ent.CreateExtensionDictionary();
            }

            DBDictionary dict = (DBDictionary)tr.GetObject(ent.ExtensionDictionary, OpenMode.ForWrite);
            const string entryName = "CORECAD_JOINT_CACHE";

            // 2. Prepare Data Buffer
            ResultBuffer rb = new ResultBuffer();
            foreach (Point3d pt in points)
            {
                rb.Add(new TypedValue((int)DxfCode.Real, pt.X));
                rb.Add(new TypedValue((int)DxfCode.Real, pt.Y));
                rb.Add(new TypedValue((int)DxfCode.Real, pt.Z));
            }

            // 3. Save to XRecord
            Xrecord xrec;
            if (dict.Contains(entryName))
            {
                xrec = (Xrecord)tr.GetObject(dict.GetAt(entryName), OpenMode.ForWrite);
            }
            else
            {
                xrec = new Xrecord();
                dict.SetAt(entryName, xrec);
                tr.AddNewlyCreatedDBObject(xrec, true);
            }

            xrec.Data = rb;
        }

        /// <summary>
        /// Retrieves cached joint vertices for high-performance rendering.
        /// </summary>
        public static Point3dCollection? GetJointCache(Entity ent)
        {
            if (ent.ExtensionDictionary == ObjectId.Null) return null;

            // Note: Using OpenCloseTransaction for read-only access in WorldDraw context
            Database db = ent.Database;
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                DBDictionary dict = (DBDictionary)tr.GetObject(ent.ExtensionDictionary, OpenMode.ForRead);
                const string entryName = "CORECAD_JOINT_CACHE";

                if (!dict.Contains(entryName)) return null;

                Xrecord xrec = (Xrecord)tr.GetObject(dict.GetAt(entryName), OpenMode.ForRead);
                using (ResultBuffer? rb = xrec.Data)
                {
                    if (rb == null) return null;
                    TypedValue[] tvs = rb.AsArray();
                    if (tvs.Length < 12) return null;

                    Point3dCollection pts = new Point3dCollection();
                    for (int i = 0; i < 12; i += 3)
                    {
                        pts.Add(new Point3d(
                            Convert.ToDouble(tvs[i].Value),
                            Convert.ToDouble(tvs[i + 1].Value),
                            Convert.ToDouble(tvs[i + 2].Value)
                        ));
                    }
                    return pts;
                }
            }
        }
        private const string ChildrenDictName = "CORECAD_CHILDREN";

        /// <summary>
        /// Links child objects to a parent entity using Handles in an XRecord.
        /// </summary>
        public static void LinkChildren(Entity parent, IEnumerable<ObjectId> children, Transaction tr)
        {
            if (parent.ExtensionDictionary == ObjectId.Null)
            {
                parent.UpgradeOpen();
                parent.CreateExtensionDictionary();
            }

            DBDictionary dict = (DBDictionary)tr.GetObject(parent.ExtensionDictionary, OpenMode.ForWrite);
            Xrecord xrec;

            if (dict.Contains(ChildrenDictName))
            {
                xrec = (Xrecord)tr.GetObject(dict.GetAt(ChildrenDictName), OpenMode.ForWrite);
            }
            else
            {
                xrec = new Xrecord();
                dict.SetAt(ChildrenDictName, xrec);
                tr.AddNewlyCreatedDBObject(xrec, true);
            }

            ResultBuffer rb = new ResultBuffer();
            foreach (ObjectId id in children)
            {
                if (!id.IsNull)
                {
                    rb.Add(new TypedValue((int)DxfCode.Handle, id.Handle));
                }
            }
            xrec.Data = rb;
        }

        /// <summary>
        /// Retrieves ObjectIds of linked children.
        /// </summary>
        public static ObjectIdCollection GetChildren(Entity parent, Transaction tr)
        {
            ObjectIdCollection ids = new ObjectIdCollection();
            if (parent.ExtensionDictionary == ObjectId.Null) return ids;

            DBDictionary dict = (DBDictionary)tr.GetObject(parent.ExtensionDictionary, OpenMode.ForRead);
            if (!dict.Contains(ChildrenDictName)) return ids;

            Xrecord xrec = (Xrecord)tr.GetObject(dict.GetAt(ChildrenDictName), OpenMode.ForRead);
            using (ResultBuffer? rb = xrec.Data)
            {
                if (rb == null) return ids;
                foreach (TypedValue tv in rb)
                {
                    if (tv.TypeCode == (int)DxfCode.Handle && tv.Value != null)
                    {
                        string? hStr = tv.Value.ToString();
                        if (!string.IsNullOrEmpty(hStr))
                        {
                            Handle h = new Handle(Convert.ToInt64(hStr, 16));
                            if (parent.Database.TryGetObjectId(h, out ObjectId id))
                            {
                                ids.Add(id);
                            }
                        }
                    }
                }
            }
            return ids;
        }

        /// <summary>
        /// Removes all children links and optionally erases child objects.
        /// </summary>
        public static void UnlinkChildren(Entity parent, Transaction tr, bool eraseChildren = false)
        {
            if (parent.ExtensionDictionary == ObjectId.Null) return;

            DBDictionary dict = (DBDictionary)tr.GetObject(parent.ExtensionDictionary, OpenMode.ForRead);
            if (!dict.Contains(ChildrenDictName)) return;

            if (eraseChildren)
            {
                ObjectIdCollection children = GetChildren(parent, tr);
                foreach (ObjectId id in children)
                {
                    if (!id.IsErased && id.IsValid)
                    {
                        Entity ent = (Entity)tr.GetObject(id, OpenMode.ForWrite);
                        ent.Erase();
                    }
                }
            }

            dict.UpgradeOpen();
            dict.Remove(ChildrenDictName);
        }

        /// <summary>
        /// Lightweight check to see if an entity has CoreCAD identity XData.
        /// </summary>
        public static bool HasIdentity(Entity ent)
        {
            using (ResultBuffer rb = ent.GetXDataForApplication(RegAppName))
            {
                return rb != null;
            }
        }

        /// <summary>
        /// Scans the database for all entities sharing a specific CoreCAD GUID.
        /// Useful for re-binding children after copy operations or finding orphans.
        /// </summary>
        public static ObjectIdCollection FindEntitiesByGuid(Database db, Guid guid, Transaction tr)
        {
            ObjectIdCollection ids = new ObjectIdCollection();
            string searchGuid = guid.ToString("D").ToUpper();

            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            foreach (ObjectId btrId in bt)
            {
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
                if (!btr.IsLayout) continue; // Only scan ModelSpace and PaperSpace layouts

                foreach (ObjectId entId in btr)
                {
                    if (entId.IsErased) continue;
                    
                    Entity ent = (Entity)tr.GetObject(entId, OpenMode.ForRead);
                    var identity = GetIdentity(ent);
                    if (identity != null && identity.Value.guid == guid)
                    {
                        ids.Add(entId);
                    }
                }
            }
            return ids;
        }
    }
}
