// =============================================================================
// V5 MIGRATION: File ini di-ISOLASI menggunakan Compiler Directive.
// Kode asli WallSyncReactor (Idle-event, ObjectModified reactor, SyncGroup pipeline)
// dinonaktifkan dan akan DIGANTIKAN oleh arsitektur Command-driven di CmdRebuild.cs.
// =============================================================================

namespace CoreCAD.Core.Services
{
    // Placeholder — reaktor baru akan diimplementasikan di Commands/CmdRebuild.cs
    public class WallSyncReactor
    {
        public static WallSyncReactor Instance = new WallSyncReactor();
        public void Register(Autodesk.AutoCAD.DatabaseServices.Database db) { }
        public void Unregister(Autodesk.AutoCAD.DatabaseServices.Database db) { }
        public void RegisterDocEvents(Autodesk.AutoCAD.ApplicationServices.Document doc) { }
    }
}

#if false
// --- SEMUA KODE DI BAWAH INI DINONAKTIFKAN (FASE 3 LOBOTOMI) ---

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using CoreCAD.Core.Geometry;
using CoreCAD.Core.Registry;
using CoreCAD.Modules.Architecture;
using Autodesk.AutoCAD.EditorInput;
using CoreCAD.Core.Diagnostics;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace CoreCAD.Core.Services
{
    public class WallSyncReactor_LEGACY
    {
        public static WallSyncReactor_LEGACY Instance = new WallSyncReactor_LEGACY();
        
        private static ConcurrentQueue<ObjectId> _pendingUpdates = new ConcurrentQueue<ObjectId>();
        private static HashSet<ObjectId> _uniqueUpdates = new HashSet<ObjectId>();
        private static bool _isIdleSubscribed = false;
        private static volatile bool _isCommandActive = false;
        
        private static bool _isSyncing = false;

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
            if (_isSyncing || e.DBObject == null || e.DBObject.IsErased) return;

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
            if (_isSyncing) return;
            // Cleanup logic would go here if needed
        }

        private void QueueUpdate(ObjectId id)
        {
            if (id.IsNull || !id.IsValid || id.IsErased) return;
            if (!_uniqueUpdates.Contains(id))
            {
                _uniqueUpdates.Add(id);
                _pendingUpdates.Enqueue(id);
            }
            TriggerIdleUpdate();
        }

        private void TriggerIdleUpdate()
        {
            if (!_isIdleSubscribed && !_pendingUpdates.IsEmpty)
            {
                Autodesk.AutoCAD.ApplicationServices.Application.Idle += OnApplicationIdle;
                _isIdleSubscribed = true;
            }
        }

        private void OnApplicationIdle(object? sender, EventArgs e)
        {
            Autodesk.AutoCAD.ApplicationServices.Application.Idle -= OnApplicationIdle;
            _isIdleSubscribed = false;
            
            if (_isCommandActive) return;

            ProcessPendingUpdates();
        }

        private void ProcessPendingUpdates()
        {
            if (_pendingUpdates.IsEmpty) return;
            if (_isSyncing) return;

            _isSyncing = true;
            
            try
            {
                HashSet<ObjectId> batchIds = new HashSet<ObjectId>();
                while (_pendingUpdates.TryDequeue(out ObjectId id))
                {
                    _uniqueUpdates.Remove(id);
                    batchIds.Add(id);
                }

                var docGroups = new Dictionary<Document, List<ObjectId>>();
                foreach (var id in batchIds)
                {
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
                    
                    try
                    {
                        if (doc == null || !doc.IsActive || doc.IsDisposed) continue;

                        using (doc.LockDocument())
                        using (var tr = doc.TransactionManager.StartTransaction())
                        {
                            HashSet<string> uniqueGroupIds = new HashSet<string>();
                            foreach (ObjectId id in kvp.Value)
                            {
                                if (id.IsNull || id.IsErased) continue;
                                Line line = (Line)tr.GetObject(id, OpenMode.ForRead);
                                string gid = GroupManager.EnsureGroupId(line, tr);
                                if (!string.IsNullOrEmpty(gid)) uniqueGroupIds.Add(gid);
                            }

                            if (uniqueGroupIds.Count > 0)
                            {
                                DebugLogger.Log($"Syncing {uniqueGroupIds.Count} clusters in {doc.Name}");
                                foreach (string groupId in uniqueGroupIds)
                                {
                                    try { SyncGroup(doc.Database, groupId, tr); }
                                    catch (System.Exception ex) { DebugLogger.Error($"SyncGroup failure [Group: {groupId}]", ex); }
                                }
                            }
                            tr.Commit();
                        }
                    }
                    catch (System.Exception ex)
                    {
                        DebugLogger.Error("ProcessPendingUpdates critical failure in document loop", ex);
                    }
                }
            }
            finally
            {
                _isSyncing = false;
            }
        }

        private void SyncGroup(Database db, string groupId, Transaction tr)
        {
            var skeletons = GroupManager.FindGroupMembers(db, groupId, tr);
            if (skeletons.Count == 0 || string.IsNullOrEmpty(groupId)) return;

            ObjectId ownerId = skeletons[0].OwnerId;
            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(ownerId, OpenMode.ForWrite);

            Polyline? newFlesh = BooleanUnionEngine.UniteSkeletons(skeletons, tr);
            if (newFlesh == null) return;

            Polyline? existingPoly = FindSharedEntity<Polyline>(db, groupId, tr, ownerId);
            Hatch? existingHatch = FindSharedEntity<Hatch>(db, groupId, tr, ownerId);
            
            if (existingPoly == null)
            {
                existingPoly = new Polyline();
                XDataManager.SetGroupId(existingPoly, groupId);
                btr.AppendEntity(existingPoly);
                tr.AddNewlyCreatedDBObject(existingPoly, true);
            }

            for (int i = 0; i < existingPoly.NumberOfVertices; i++) existingPoly.RemoveVertexAt(0);
            for (int i = 0; i < newFlesh.NumberOfVertices; i++)
                existingPoly.AddVertexAt(i, newFlesh.GetPoint2dAt(i), newFlesh.GetBulgeAt(i), 0, 0);
            existingPoly.Closed = true;
            existingPoly.RecordGraphicsModified(true);

            CleanupIndividualFlesh(skeletons, tr);

            if (existingHatch == null)
            {
                existingHatch = new Hatch();
                existingHatch.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
                XDataManager.SetGroupId(existingHatch, groupId);
                btr.AppendEntity(existingHatch);
                tr.AddNewlyCreatedDBObject(existingHatch, true);
            }

            existingHatch.Associative = true;
            if (existingHatch.NumberOfLoops > 0)
                for (int i = existingHatch.NumberOfLoops - 1; i >= 0; i--) existingHatch.RemoveLoopAt(i);
            existingHatch.AppendLoop(HatchLoopTypes.Outermost, new ObjectIdCollection { existingPoly.ObjectId });
            existingHatch.EvaluateHatch(true);
            existingHatch.RecordGraphicsModified(true);
        }

        private T? FindSharedEntity<T>(Database db, string groupId, Transaction tr, ObjectId ownerId) where T : Entity
        {
            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(ownerId, OpenMode.ForRead);
            foreach (ObjectId id in btr)
            {
                if (id.IsErased) continue;
                if (id.ObjectClass.IsDerivedFrom(RXClass.GetClass(typeof(T))))
                {
                    Entity ent = (Entity)tr.GetObject(id, OpenMode.ForWrite);
                    if (XDataManager.GetGroupId(ent) == groupId) return (T)ent;
                }
            }
            return null;
        }

        private void CleanupIndividualFlesh(List<Line> skeletons, Transaction tr)
        {
            foreach (var line in skeletons)
            {
                ObjectIdCollection children = XDataManager.GetChildren(line, tr);
                foreach (ObjectId childId in children)
                {
                    if (childId.IsNull || !childId.IsValid || childId.IsErased) continue;
                    Entity childR = (Entity)tr.GetObject(childId, OpenMode.ForRead);
                    bool isIndividual = string.IsNullOrEmpty(XDataManager.GetGroupId(childR));
                    if (isIndividual)
                    {
                        Entity childW = (Entity)tr.GetObject(childId, OpenMode.ForWrite);
                        childW.Erase();
                    }
                }
                XDataManager.LinkChildren(line, Enumerable.Empty<ObjectId>(), tr);
            }
        }
    }
}

#endif
