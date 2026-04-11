using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using CoreCAD.Persistence;
using CoreCAD.Engine;
using CoreCAD.Models;
using System;

namespace CoreCAD.Core.Services
{
    /// <summary>
    /// [V5 REACTOR] Mengelola sinkronisasi real-time antara Proxy Entity (Grip) dan Data (WallData).
    /// Mengimplementasikan "Proxy-Line Constraints" agar handle opening terkunci pada as dinding.
    /// </summary>
    public class WallSyncReactor
    {
        public static WallSyncReactor Instance = new WallSyncReactor();
        private bool _isSyncing = false;

        private static HashSet<Database> _pendingDbs = new HashSet<Database>();
        private static bool _isIdleRegistered = false;

        /// <summary>
        /// Flag untuk menangguhkan reactor saat Mesin Utama sedang bekerja (Bulk Purge/Bake).
        /// Mencegah 'eWasOpenForWrite' dan penghapusan data secara tidak sengaja.
        /// </summary>
        public static bool IsSuspended { get; set; } = false;

        public void Register(Database db)
        {
            db.ObjectModified += Database_ObjectModified;
            db.ObjectErased += Database_ObjectErased;
        }

        public void Unregister(Database db)
        {
            db.ObjectModified -= Database_ObjectModified;
            db.ObjectErased -= Database_ObjectErased;
        }

        public void RegisterDocEvents(Document doc) { }

        private void RequestSync(Database db)
        {
            if (db == null) return;
            _pendingDbs.Add(db);
            if (!_isIdleRegistered)
            {
                Autodesk.AutoCAD.ApplicationServices.Application.Idle += OnIdle;
                _isIdleRegistered = true;
            }
        }

        private static void OnIdle(object sender, EventArgs e)
        {
            Autodesk.AutoCAD.ApplicationServices.Application.Idle -= OnIdle;
            _isIdleRegistered = false;

            if (_pendingDbs.Count == 0) return;

            // Clone list agar tidak konflik saat iterasi
            var dbs = new Database[_pendingDbs.Count];
            _pendingDbs.CopyTo(dbs);
            _pendingDbs.Clear();

            foreach (var db in dbs)
            {
                if (db.IsDisposed) continue;

                // [SOP] Gunakan Transaksi Bersih di luar event handler
                IsSuspended = true;
                try
                {
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        try 
                        {
                            // REBUILD GLOBAL (Bisa di-optimize menjadi Single Wall nantinya)
                            ViewGenerator.PurgeOldViews(db, tr);
                            ViewGenerator.BakeAllWalls(db, tr);
                            tr.Commit();
                        }
                        catch { tr.Abort(); }
                    }
                }
                finally
                {
                    IsSuspended = false;
                }
            }
        }

        private void Database_ObjectModified(object sender, ObjectEventArgs e)
        {
            if (IsSuspended || _isSyncing || e.DBObject == null || e.DBObject.IsErased) return;

            if (e.DBObject is Line gripLine)
            {
                var gripData = XDataManager.GetGripData(gripLine);
                if (gripData == null) return;

                _isSyncing = true;
                try
                {
                    // [SOP] HANYA UPDATE DATA, Jangan modifikasi visual
                    UpdateWallDataFromGrip(gripLine, gripData.Value.hostHandle, gripData.Value.opIndex);
                    RequestSync(gripLine.Database);
                }
                catch (System.Exception ex)
                {
                    Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage($"\n[Reactor Error]: {ex.Message}");
                }
                finally
                {
                    _isSyncing = false;
                }
            }
        }

        private void UpdateWallDataFromGrip(Line gripLine, string hostHandle, int opIndex)
        {
            Database db = gripLine.Database;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                long hVal = long.Parse(hostHandle, System.Globalization.NumberStyles.HexNumber);
                Handle h = new Handle(hVal);
                if (!db.TryGetObjectId(h, out ObjectId hostId)) return;

                Line? hostWall = tr.GetObject(hostId, OpenMode.ForWrite) as Line;
                if (hostWall == null) return;

                WallData? data = XDataManager.GetWallData(hostWall);
                if (data == null || opIndex >= data.Openings.Count) return;

                // LOGIKA CONSTRAINTS (Calculations only)
                Point3d S = hostWall.StartPoint;
                Point3d midGrip = gripLine.StartPoint + (gripLine.EndPoint - gripLine.StartPoint) * 0.5;
                Point3d projectedMid = hostWall.GetClosestPointTo(midGrip, false);
                
                double newPos = (projectedMid - S).Length;
                double newWidth = (gripLine.EndPoint - gripLine.StartPoint).Length;

                // Update Source of Truth (XData)
                data.Openings[opIndex].Position = newPos;
                data.Openings[opIndex].Width = newWidth;
                XDataManager.AttachWallData(tr, db, hostWall, data);

                tr.Commit(); // Data tersimpan, tapi visual belum berubah (menunggu Idle)
            }
        }

        private void Database_ObjectErased(object sender, ObjectErasedEventArgs e)
        {
            if (IsSuspended || _isSyncing || e.DBObject == null) return;

            if (e.DBObject is Line gripLine && e.Erased)
            {
                var gripData = XDataManager.GetGripData(gripLine);
                if (gripData == null) return;

                _isSyncing = true;
                try
                {
                    RemoveOpeningFromGrip(gripLine, gripData.Value.hostHandle, gripData.Value.opIndex);
                    RequestSync(gripLine.Database);
                }
                finally
                {
                    _isSyncing = false;
                }
            }
        }

        private void RemoveOpeningFromGrip(Line gripLine, string hostHandle, int opIndex)
        {
             Database db = gripLine.Database;
             using (Transaction tr = db.TransactionManager.StartTransaction())
             {
                 long hVal = long.Parse(hostHandle, System.Globalization.NumberStyles.HexNumber);
                 Handle h = new Handle(hVal);
                 if (db.TryGetObjectId(h, out ObjectId hostId))
                 {
                     Line? hostWall = tr.GetObject(hostId, OpenMode.ForWrite) as Line;
                     if (hostWall != null)
                     {
                         WallData? data = XDataManager.GetWallData(hostWall);
                         if (data != null && opIndex < data.Openings.Count)
                         {
                             data.Openings.RemoveAt(opIndex);
                             XDataManager.AttachWallData(tr, db, hostWall, data);
                         }
                     }
                 }
                 tr.Commit();
             }
        }
    }
}
