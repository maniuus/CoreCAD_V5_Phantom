using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using CoreCAD.Core.Geometry;
using CoreCAD.Core.Registry;
using CoreCAD.Modules.Architecture;
using Autodesk.AutoCAD.EditorInput;
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
            doc.CommandCancelled += (s, e) => { _isCommandActive = false; TriggerIdleUpdate(); };
        }

        private void Database_ObjectAppended(object sender, ObjectEventArgs e) { }

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
            TriggerIdleUpdate();
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

            ProcessPendingUpdates();
        }

        private void ProcessPendingUpdates()
        {
            if (_pendingUpdates.IsEmpty) return;

            // Group by Document instead of just Database to manage Locking correctly
            var docGroups = new Dictionary<Document, List<ObjectId>>();
            while (_pendingUpdates.TryDequeue(out ObjectId id))
            {
                _uniqueUpdates.Remove(id);
                if (id.IsValid && !id.IsErased)
                {
                    Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.GetDocument(id.Database);
                    if (doc == null) continue;

                    if (!docGroups.ContainsKey(doc)) docGroups[doc] = new List<ObjectId>();
                    docGroups[doc].Add(id);
                }
            }

            foreach (var kvp in docGroups)
            {
                Document doc = kvp.Key;
                Editor ed = doc.Editor;
                Database db = doc.Database;
                
                try
                {
                    // CRITICAL FIX: Lock the document before modifying from the Idle event
                    using (doc.LockDocument())
                    using (var tr = doc.TransactionManager.StartTransaction())
                    {
                        foreach (ObjectId id in kvp.Value)
                        {
                            if (id.IsErased) continue;
                            Line line = (Line)tr.GetObject(id, OpenMode.ForRead);
                            UpdateWallGeometry(line, tr, ed);
                        }
                        tr.Commit();
                    }
                    ed.Regen(); // Force hatch and geometry redraw
                }
                catch (System.Exception ex)
                {
                    try { ed.WriteMessage($"\n[coreCAD ERROR] Sync failed: {ex.Message}"); } catch { }
                }
            }
        }

        private void UpdateWallGeometry(Line line, Transaction tr, Editor ed)
        {
            if (line == null) return;
            var identity = XDataManager.GetIdentity(line);
            if (identity == null) return;

            ObjectIdCollection childrenIds = XDataManager.GetChildren(line, tr);
            if (childrenIds.Count == 0)
            {
                childrenIds = XDataManager.FindEntitiesByGuid(line.Database, identity.Value.guid, tr);
            }

            if (childrenIds.Count == 0) return;

            List<Entity> children = new List<Entity>();
            foreach (ObjectId id in childrenIds)
            {
                if (id == line.ObjectId) continue;
                if (!id.IsValid || id.IsErased) continue;
                
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
                    ent.RecordGraphicsModified(true); // Force DB to see changes
                }

                foreach (var ent in children.OfType<Hatch>())
                {
                    ent.EvaluateHatch(true); // Re-calculate based on updated boundary
                }
            }
        }

        private void CleanupChildren(Line line)
        {
            if (line == null || line.Database == null) return;
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.GetDocument(line.Database);
            if (doc == null) return;

            try
            {
                using (doc.LockDocument())
                using (var tr = doc.TransactionManager.StartTransaction())
                {
                    var identity = XDataManager.GetIdentity(line);
                    if (identity != null)
                    {
                        ObjectIdCollection children = XDataManager.GetChildren(line, tr);
                        if (children.Count == 0)
                        {
                            children = XDataManager.FindEntitiesByGuid(line.Database, identity.Value.guid, tr);
                        }

                        foreach (ObjectId id in children)
                        {
                            if (id != line.ObjectId && id.IsValid && !id.IsErased)
                            {
                                Entity ent = (Entity)tr.GetObject(id, OpenMode.ForWrite);
                                ent.Erase();
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
