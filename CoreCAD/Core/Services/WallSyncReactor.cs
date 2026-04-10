using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using CoreCAD.Core.Geometry;
using CoreCAD.Core.Registry;
using CoreCAD.Modules.Architecture;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace CoreCAD.Core.Services
{
    public class WallSyncReactor
    {
        public static WallSyncReactor Instance = new WallSyncReactor();
        
        private static ConcurrentQueue<ObjectId> _pendingUpdates = new ConcurrentQueue<ObjectId>();
        private static ConcurrentQueue<ObjectId> _pendingReIDs = new ConcurrentQueue<ObjectId>();
        private static HashSet<ObjectId> _uniqueUpdates = new HashSet<ObjectId>();
        private static bool _isIdleSubscribed = false;
        private static bool _isCommandActive = false;

        public void Register(Database db)
        {
            db.ObjectAppended += Database_ObjectAppended;
            db.ObjectModified += Database_ObjectModified;
            db.ObjectErased += Database_ObjectErased;
        }

        public void Unregister(Database db)
        {
            db.ObjectAppended -= Database_ObjectAppended;
            db.ObjectModified -= Database_ObjectModified;
            db.ObjectErased -= Database_ObjectErased;
        }

        public void RegisterDocEvents(Document doc)
        {
            doc.CommandWillStart += (s, e) => _isCommandActive = true;
            doc.CommandEnded += (s, e) => { _isCommandActive = false; TriggerIdleUpdate(); };
            doc.CommandCancelled += (s, e) => { _isCommandActive = false; _pendingUpdates = new ConcurrentQueue<ObjectId>(); _uniqueUpdates.Clear(); };
        }

        private void Database_ObjectAppended(object sender, ObjectEventArgs e)
        {
            // COPY/CLONE DETECTION: Defer to Idle to avoid eInvalidContext/Transaction leaks
            if (e.DBObject is Entity ent && XDataManager.HasIdentity(ent))
            {
                _pendingReIDs.Enqueue(ent.ObjectId);
                TriggerIdleUpdate();
            }
        }

        private void Database_ObjectModified(object sender, ObjectEventArgs e)
        {
            if (e.DBObject is Line line)
            {
                var identity = XDataManager.GetIdentity(line);
                if (identity != null && identity.Value.role == "MASTER")
                {
                    QueueUpdate(line.ObjectId);
                }
            }
        }

        private void Database_ObjectErased(object sender, ObjectErasedEventArgs e)
        {
            if (e.DBObject is Line line && e.Erased)
            {
                var identity = XDataManager.GetIdentity(line);
                if (identity != null && identity.Value.role == "MASTER")
                {
                    CleanupChildren(line);
                }
            }
        }

        private void QueueUpdate(ObjectId id)
        {
            if (!_uniqueUpdates.Contains(id))
            {
                _uniqueUpdates.Add(id);
                _pendingUpdates.Enqueue(id);
            }
            if (!_isCommandActive) TriggerIdleUpdate();
        }

        private void TriggerIdleUpdate()
        {
            if (!_isIdleSubscribed && (!_pendingUpdates.IsEmpty || !_pendingReIDs.IsEmpty))
            {
                Autodesk.AutoCAD.ApplicationServices.Application.Idle += OnIdle;
                _isIdleSubscribed = true;
            }
        }

        private void OnIdle(object? sender, EventArgs e)
        {
            Autodesk.AutoCAD.ApplicationServices.Application.Idle -= OnIdle;
            _isIdleSubscribed = false;
            if (_isCommandActive) return;

            ProcessPendingReIDs();
            ProcessPendingUpdates();
        }

        private void ProcessPendingReIDs()
        {
            if (_pendingReIDs.IsEmpty) return;

            var databaseGroups = new Dictionary<Database, List<ObjectId>>();
            while (_pendingReIDs.TryDequeue(out ObjectId id))
            {
                if (id.IsValid && !id.IsErased)
                {
                    Database db = id.Database;
                    if (!databaseGroups.ContainsKey(db)) databaseGroups[db] = new List<ObjectId>();
                    databaseGroups[db].Add(id);
                }
            }

            foreach (var kvp in databaseGroups)
            {
                Database db = kvp.Key;
                try
                {
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        foreach (ObjectId id in kvp.Value)
                        {
                            Entity ent = (Entity)tr.GetObject(id, OpenMode.ForWrite);
                            var identity = XDataManager.GetIdentity(ent);
                            if (identity != null)
                            {
                                Guid newGuid = Guid.NewGuid();
                                XDataManager.SetIdentity(ent, newGuid, identity.Value.materialId, identity.Value.levelId, identity.Value.pseudoZ, identity.Value.role);
                            }
                        }
                        tr.Commit();
                    }
                }
                catch { }
            }
        }

        private void ProcessPendingUpdates()
        {
            if (_pendingUpdates.IsEmpty) return;

            var databaseGroups = new Dictionary<Database, List<ObjectId>>();
            while (_pendingUpdates.TryDequeue(out ObjectId id))
            {
                _uniqueUpdates.Remove(id);
                if (id.IsValid && !id.IsErased)
                {
                    Database db = id.Database;
                    if (!databaseGroups.ContainsKey(db)) databaseGroups[db] = new List<ObjectId>();
                    databaseGroups[db].Add(id);
                }
            }

            foreach (var kvp in databaseGroups)
            {
                Database db = kvp.Key;
                try
                {
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        foreach (ObjectId id in kvp.Value)
                        {
                            if (id.IsErased) continue;
                            Line line = (Line)tr.GetObject(id, OpenMode.ForRead);
                            UpdateWallGeometry(line, tr);
                        }
                        tr.Commit();
                    }
                }
                catch { }
            }
        }

        private void UpdateWallGeometry(Line line, Transaction tr)
        {
            if (line == null) return;
            var identity = XDataManager.GetIdentity(line);
            if (identity == null) return;

            ObjectIdCollection childrenIds = XDataManager.FindEntitiesByGuid(line.Database, identity.Value.guid, tr);
            List<Entity> children = new List<Entity>();
            foreach (ObjectId id in childrenIds)
            {
                if (id == line.ObjectId) continue;
                children.Add((Entity)tr.GetObject(id, OpenMode.ForWrite));
            }
            
            SmartWall wall = new SmartWall();
            wall.LoadFromXData(line);
            wall.StartPoint = line.StartPoint;
            wall.EndPoint = line.EndPoint;

            Point3dCollection pts = wall.GetVertices(true, line);
            if (pts.Count >= 4)
            {
                foreach (var ent in children.OfType<Polyline>())
                {
                    for (int i = 0; i < pts.Count; i++)
                    {
                        ent.SetPointAt(i, new Point2d(pts[i].X, pts[i].Y));
                    }
                    ent.Elevation = pts[0].Z;
                    ent.RecordGraphicsModified(true);
                }

                foreach (var ent in children.OfType<Hatch>())
                {
                    ent.EvaluateHatch(true);
                }
            }
        }

        private void CleanupChildren(Line line)
        {
            if (line == null || line.Database == null) return;
            try
            {
                using (var tr = line.Database.TransactionManager.StartTransaction())
                {
                    var identity = XDataManager.GetIdentity(line);
                    if (identity != null)
                    {
                        ObjectIdCollection children = XDataManager.FindEntitiesByGuid(line.Database, identity.Value.guid, tr);
                        foreach (ObjectId id in children)
                        {
                            if (id != line.ObjectId)
                            {
                                Entity ent = (Entity)tr.GetObject(id, OpenMode.ForWrite);
                                if (!ent.IsErased) ent.Erase();
                            }
                        }
                    }
                    tr.Commit();
                }
            }
            catch { }
        }
    }
}
