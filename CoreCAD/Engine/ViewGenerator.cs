using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using CoreCAD.Persistence;  // CoreCAD.Database namespace → CoreCAD.Persistence (anti-collision)
using CoreCAD.Models;
using System.Collections.Generic;

namespace CoreCAD.Engine
{
    /// <summary>
    /// [V5 ENGINE] Mesin Cetak — mengeksekusi siklus Purge → Bake:
    ///   PurgeOldViews : hapus semua Polyline berisi XData "CORECAD_VIEW"
    ///   BakeAllWalls  : cetak Polyline baru untuk tiap Line berXData "CORECAD_MODEL"
    /// </summary>
    public static class ViewGenerator
    {
        public const string APP_VIEW_NAME = "CORECAD_VIEW";

        /// <summary>
        /// Tahap 1 — Purge: kumpulkan semua ObjectId view lama, lalu hapus.
        /// CATATAN: Tidak boleh Erase dalam iterasi aktif — kita collect dulu.
        /// </summary>
        public static void PurgeOldViews(Autodesk.AutoCAD.DatabaseServices.Database db, Transaction tr)
        {
            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

            // Pass 1: kumpulkan ID view yang akan dihapus
            List<ObjectId> toErase = new List<ObjectId>();
            foreach (ObjectId id in btr)
            {
                if (id.IsErased) continue;
                Entity? ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null) continue;

                ResultBuffer? rb = ent.GetXDataForApplication(APP_VIEW_NAME);
                if (rb != null)
                {
                    toErase.Add(id);
                    rb.Dispose();
                }
            }

            // Pass 2: hapus setelah iterasi selesai
            foreach (ObjectId id in toErase)
            {
                Entity ent = (Entity)tr.GetObject(id, OpenMode.ForWrite);
                if (!ent.IsErased) ent.Erase(true);
            }
        }

        /// <summary>
        /// Tahap 2 — Bake: cetak Polyline kotak untuk setiap MASTER Line yang punya KTP.
        /// </summary>
        public static void BakeAllWalls(Autodesk.AutoCAD.DatabaseServices.Database db, Transaction tr)
        {
            // Pastikan RegApp "CORECAD_VIEW" terdaftar
            RegAppTable rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (!rat.Has(APP_VIEW_NAME))
            {
                rat.UpgradeOpen();
                RegAppTableRecord ratr = new RegAppTableRecord { Name = APP_VIEW_NAME };
                rat.Add(ratr);
                tr.AddNewlyCreatedDBObject(ratr, true);
            }

            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            // Buka ForWrite sekarang karena kita akan AppendEntity
            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

            // Kumpulkan Line master dulu (hindari iterasi bersamaan dengan append)
            List<ObjectId> masterLines = new List<ObjectId>();
            foreach (ObjectId id in btr)
            {
                if (id.IsErased) continue;
                if (id.ObjectClass.IsDerivedFrom(
                    Autodesk.AutoCAD.Runtime.RXClass.GetClass(typeof(Line))))
                {
                    masterLines.Add(id);
                }
            }

            // Bake: satu Polyline per Line yang punya WallData KTP
            foreach (ObjectId lineId in masterLines)
            {
                Line? wallLine = tr.GetObject(lineId, OpenMode.ForRead) as Line;
                if (wallLine == null) continue;

                WallData? data = XDataManager.GetWallData(wallLine);
                if (data == null) continue;

                Polyline wallPoly = BuildWallPolyline(wallLine, data.Thickness);
                btr.AppendEntity(wallPoly);
                tr.AddNewlyCreatedDBObject(wallPoly, true);

                // Tandai sebagai VIEW agar bisa di-purge berikutnya
                ResultBuffer rbView = new ResultBuffer(
                    new TypedValue((int)DxfCode.ExtendedDataRegAppName, APP_VIEW_NAME),
                    new TypedValue((int)DxfCode.ExtendedDataAsciiString, "IS_VIEW:TRUE")
                );
                wallPoly.XData = rbView;
                rbView.Dispose();
            }
        }

        // ── Helper: hitung 4 sudut kotak dinding dan kembalikan Polyline ──────────
        private static Polyline BuildWallPolyline(Line wallLine, double thickness)
        {
            Point3d start = wallLine.StartPoint;
            Point3d end   = wallLine.EndPoint;
            double half   = thickness / 2.0;

            Vector2d dir  = new Vector2d(end.X - start.X, end.Y - start.Y).GetNormal();
            Vector2d perp = new Vector2d(-dir.Y, dir.X) * half;

            Point2d p1 = new Point2d(start.X + perp.X, start.Y + perp.Y);
            Point2d p2 = new Point2d(end.X   + perp.X, end.Y   + perp.Y);
            Point2d p3 = new Point2d(end.X   - perp.X, end.Y   - perp.Y);
            Point2d p4 = new Point2d(start.X - perp.X, start.Y - perp.Y);

            Polyline poly = new Polyline();
            poly.SetDatabaseDefaults();
            poly.AddVertexAt(0, p1, 0, 0, 0);
            poly.AddVertexAt(1, p2, 0, 0, 0);
            poly.AddVertexAt(2, p3, 0, 0, 0);
            poly.AddVertexAt(3, p4, 0, 0, 0);
            poly.Closed = true;
            return poly;
        }
    }
}
