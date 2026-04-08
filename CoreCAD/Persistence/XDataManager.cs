using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using CoreCAD.Core;

namespace CoreCAD.Persistence
{
    public static class XDataManager
    {
        public static void RegisterApp(Transaction tr, Autodesk.AutoCAD.DatabaseServices.Database db)
        {
            RegAppTable rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (!rat.Has(ProjectIdentities.RegAppName))
            {
                rat.UpgradeOpen();
                RegAppTableRecord ratr = new RegAppTableRecord();
                ratr.Name = ProjectIdentities.RegAppName;
                rat.Add(ratr);
                tr.AddNewlyCreatedDBObject(ratr, true);
            }
        }

        public static void SetCoreData(Entity ent, string guid, string roleId, string parentId, double localZ)
        {
            Autodesk.AutoCAD.DatabaseServices.Database db = ent.Database;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                RegisterApp(tr, db);
                
                ResultBuffer rb = new ResultBuffer(
                    new TypedValue((int)DxfCode.ExtendedDataRegAppName, ProjectIdentities.RegAppName),
                    new TypedValue((int)DxfCode.ExtendedDataControlString, "{"),
                    new TypedValue((int)DxfCode.ExtendedDataAsciiString, guid),     // Field 1: GUID
                    new TypedValue((int)DxfCode.ExtendedDataAsciiString, roleId),   // Field 2: Role_ID
                    new TypedValue((int)DxfCode.ExtendedDataAsciiString, parentId), // Field 3: Parent_ID
                    new TypedValue((int)DxfCode.ExtendedDataReal, localZ),          // Field 4: Local_Z
                    new TypedValue((int)DxfCode.ExtendedDataControlString, "}")
                );

                ent.UpgradeOpen();
                ent.XData = rb;
                
                tr.Commit();
            }
        }

        public static (string guid, string roleId, string parentId, double localZ) GetCoreData(Entity ent)
        {
            ResultBuffer rb = ent.GetXDataForApplication(ProjectIdentities.RegAppName);
            if (rb == null) return (string.Empty, string.Empty, string.Empty, 0.0);

            TypedValue[] tvs = rb.AsArray();
            // Index 0: RegApp, Index 1: "{", Index 2: GUID, Index 3: RoleID, Index 4: ParentID, Index 5: LocalZ, Index 6: "}"
            if (tvs.Length < 6) return (string.Empty, string.Empty, string.Empty, 0.0);

            string guid = tvs[2].Value.ToString() ?? string.Empty;
            string roleId = tvs[3].Value.ToString() ?? string.Empty;
            string parentId = tvs[4].Value.ToString() ?? string.Empty;
            double localZ = tvs[5].Value is double d ? d : 0.0;

            return (guid, roleId, parentId, localZ);
        }

        public static bool HasCoreCADIdentity(Entity ent)
        {
            using (var rb = ent.GetXDataForApplication(ProjectIdentities.RegAppName))
            {
                return rb != null;
            }
        }

        public static void RefreshGuidOnly(Entity ent)
        {
            var data = GetCoreData(ent);
            if (string.IsNullOrEmpty(data.guid)) return;

            // Generate new GUID but keep Role and Parent
            string newGuid = System.Guid.NewGuid().ToString();
            
            // Re-apply with new GUID
            SetCoreData(ent, newGuid, data.roleId, data.parentId, data.localZ);
        }

        public static void OnObjectAppended(object sender, ObjectEventArgs e)
        {
            if (!(e.DBObject is Entity ent)) return;

            // We use a separate transaction to check and update the entity
            // This ensures the clone gets a unique identity immediately
            var db = (Autodesk.AutoCAD.DatabaseServices.Database)sender;
            
            try
            {
                // Note: ObjectAppended triggers when an object is added to the database.
                // If it already has our RegApp XData, it's a clone (Copy/Mirror/etc).
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    Entity? obj = tr.GetObject(e.DBObject.ObjectId, OpenMode.ForRead) as Entity;
                    if (obj != null && HasCoreCADIdentity(obj))
                    {
                        RefreshGuidOnly(obj);
                    }
                    tr.Commit();
                }
            }
            catch
            {
                // Safety: Avoid crashing the host application during events
            }
        }
    }
}
