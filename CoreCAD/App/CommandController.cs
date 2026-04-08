using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using CoreCAD.Models;
using CoreCAD.Persistence;
using CoreCAD.App.Tools;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CoreCAD.App
{
    public class CommandController
    {
        [CommandMethod("CORE_CREATE_COLUMN")]
        public void CreateColumn()
        {
            new SmartColumnTool().Execute();
        }

        [CommandMethod("CORE_CREATE_DOOR")]
        public void CreateDoor()
        {
            new SmartDoorTool().Execute();
        }

        [CommandMethod("CORE_PULL_SYNC")]
        public void ExecutePull()
        {
            RunSyncLogic(false); // Mode Update
        }

        [CommandMethod("CORE_TELEPORT")]
        public void ExecuteTeleport()
        {
            RunSyncLogic(true); // Mode Generative (Teleport)
        }

        [CommandMethod("CORE_CLEANUP_DB")]
        public void CleanupDatabase()
        {
            CoreJSONEngine.CleanOrphanedData();
        }

        private void RunSyncLogic(bool generateMissing)
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            ed.WriteMessage(generateMissing ? "\n[CoreCAD] Memulai Teleportasi Objek Massal..." : "\n[CoreCAD] Memulai koordinasi data dari Master JSON...");

            // MUTE WATCHER UNTUK MENCEGAH STUCK/DEADLOCK
            DatabaseWatcher.IsDisabled = true;

            try
            {
                var master = CoreJSONEngine.LoadMaster();
                if (master == null || master.Count == 0) return;

                var jsonMap = new Dictionary<string, SmartObject>();
                foreach (var e in master) if (!string.IsNullOrEmpty(e.Guid)) jsonMap[e.Guid] = e;

                using (doc.LockDocument())
                {
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                        // Mode WRITE hanya jika Teleport (generateMissing)
                        var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], generateMissing ? OpenMode.ForWrite : OpenMode.ForRead);

                        var existingGuids = new HashSet<string>();
                        foreach (ObjectId id in ms)
                        {
                            var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                            if (ent == null || !XDataHelper.HasIdentity(ent)) continue;

                            string cadGuid = XDataHelper.GetGuid(ent);
                            existingGuids.Add(cadGuid);

                            if (jsonMap.TryGetValue(cadGuid, out var dataJson))
                            {
                                ent.UpgradeOpen();
                                UpdateObjectVisual(ent, dataJson);
                            }
                        }

                        if (generateMissing)
                        {
                            int teleportCount = 0;
                            foreach (var pair in jsonMap)
                            {
                                if (!existingGuids.Contains(pair.Key))
                                {
                                    Entity? newEnt = GenerateVisualFromData(pair.Value, db);
                                    if (newEnt != null)
                                    {
                                        ms.AppendEntity(newEnt);
                                        tr.AddNewlyCreatedDBObject(newEnt, true);
                                        XDataHelper.SetIdentity(newEnt, pair.Value);
                                        teleportCount++;
                                    }
                                }
                            }
                            ed.WriteMessage($"\n[Success] Teleportasi Berhasil! {teleportCount} objek baru mendarat.");
                        }

                        tr.Commit();
                    }
                }
                doc.TransactionManager.QueueForGraphicsFlush();
            }
            catch (System.Exception ex) { ed.WriteMessage($"\n[Error] Gagal sinkronisasi: {ex.Message}"); }
            finally
            {
                // HIDUPKAN KEMBALI WATCHER
                DatabaseWatcher.IsDisabled = false;
            }
        }

        private Entity? GenerateVisualFromData(SmartObject data, Database db)
        {
            // Abstraksi: Di masa depan ini akan memanggil ToolFactory
            if (data.RoleId == "STRUCTURAL_COLUMN")
            {
                Polyline poly = new Polyline(4);
                poly.AddVertexAt(0, Point2d.Origin, 0, 0, 0);
                poly.AddVertexAt(1, Point2d.Origin, 0, 0, 0);
                poly.AddVertexAt(2, Point2d.Origin, 0, 0, 0);
                poly.AddVertexAt(3, Point2d.Origin, 0, 0, 0);
                poly.Closed = true;
                UpdateObjectVisual(poly, data);
                return poly;
            }
            if (data.RoleId == "ARCH_DOOR_SINGLE")
            {
                Polyline poly = new Polyline(4);
                poly.AddVertexAt(0, Point2d.Origin, 0, 0, 0); // Dummy points
                poly.AddVertexAt(1, Point2d.Origin, 0, 0, 0);
                poly.AddVertexAt(2, Point2d.Origin, 0, 0, 0);
                poly.AddVertexAt(3, Point2d.Origin, 0, 0, 0);
                poly.Closed = true;
                UpdateObjectVisual(poly, data);
                return poly;
            }
            return null;
        }

        private void UpdateObjectVisual(Entity ent, SmartObject data)
        {
            double w = data.Width;
            double h = data.Height;
            if (w <= 1 || h <= 1) return;

            var geom = data.Instances.FirstOrDefault()?.Geometry ?? data.LegacyGeometry;
            if (geom == null) return;

            // Gunakan Koordinat Absolut (Mencegah Accumulation Bug)
            double cx = geom.LocalX;
            double cy = geom.LocalY;
            double rot = geom.Rotation;
            double flip = geom.FlipState ? -1.0 : 1.0;

            if (ent is Polyline poly)
            {
                // Hitung koordinat lokal dulu
                double hw = w / 2.0;
                double hh = h / 2.0;

                Point2d p0, p1, p2, p3;

                if (data.RoleId == "ARCH_DOOR_SINGLE")
                {
                    double thick = data.Dna.LeafThickness;
                    if (thick <= 0) thick = 40;
                    p0 = new Point2d(0, 0);
                    p1 = new Point2d(w, 0);
                    p2 = new Point2d(w, thick * flip);
                    p3 = new Point2d(0, thick * flip);
                }
                else
                {
                    p0 = new Point2d(-hw, -hh);
                    p1 = new Point2d(hw, -hh);
                    p2 = new Point2d(hw, hh);
                    p3 = new Point2d(-hw, hh);
                }

                // Terapkan Rotasi dan Posisi secara ABSOLUT (Satu Kali Hitung)
                poly.SetPointAt(0, RotatePoint(p0, rot, cx, cy));
                poly.SetPointAt(1, RotatePoint(p1, rot, cx, cy));
                poly.SetPointAt(2, RotatePoint(p2, rot, cx, cy));
                poly.SetPointAt(3, RotatePoint(p3, rot, cx, cy));
            }
        }

        private Point2d RotatePoint(Point2d pt, double angle, double cx, double cy)
        {
            double cos = Math.Cos(angle);
            double sin = Math.Sin(angle);
            double nx = (pt.X * cos) - (pt.Y * sin) + cx;
            double ny = (pt.X * sin) + (pt.Y * cos) + cy;
            return new Point2d(nx, ny);
        }
    }
}
