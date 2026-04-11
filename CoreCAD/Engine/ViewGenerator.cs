using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using CoreCAD.Persistence;  // CoreCAD.Database folder → namespace CoreCAD.Persistence
using CoreCAD.Models;
using System.Collections.Generic;

namespace CoreCAD.Engine
{
    /// <summary>
    /// [V5 ENGINE] Mesin Cetak + Tukang Cat — siklus Purge → Bake:
    ///   PurgeOldViews : hapus semua entitas berXData "CORECAD_VIEW" (Polyline + Hatch)
    ///   BakeAllWalls  : cetak Polyline kotak + Hatch ANSI31 untuk tiap Line KTP
    /// </summary>
    public static class ViewGenerator
    {
        public const string APP_VIEW_NAME = "CORECAD_VIEW";

        // ── PURGE ────────────────────────────────────────────────────────────────
        /// <summary>
        /// Kumpulkan semua ObjectId bertanda CORECAD_VIEW, lalu hapus setelah iterasi.
        /// WAJIB dua pass: tidak boleh Erase dalam iterasi aktif BlockTableRecord.
        /// </summary>
        public static void PurgeOldViews(Autodesk.AutoCAD.DatabaseServices.Database db, Transaction tr)
        {
            BlockTable bt   = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

            // Pass 1 — collect
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

            // Pass 2 — erase (iterasi sudah selesai, aman)
            foreach (ObjectId id in toErase)
            {
                Entity ent = (Entity)tr.GetObject(id, OpenMode.ForWrite);
                if (!ent.IsErased) ent.Erase(true);
            }
        }

        // ── BAKE ─────────────────────────────────────────────────────────────────
        /// <summary>
        /// Untuk setiap Line yang punya KTP (WallData), cetak:
        ///   1. Polyline kotak (batas dinding)
        ///   2. Hatch ANSI31 dengan batas Polyline tersebut
        /// Keduanya diberi stempel "CORECAD_VIEW" agar ikut ter-purge saat CC_REBUILD.
        /// </summary>
        public static void BakeAllWalls(Autodesk.AutoCAD.DatabaseServices.Database db, Transaction tr)
        {
            // Pastikan RegApp VIEW terdaftar
            RegAppTable rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (!rat.Has(APP_VIEW_NAME))
            {
                rat.UpgradeOpen();
                RegAppTableRecord ratr = new RegAppTableRecord { Name = APP_VIEW_NAME };
                rat.Add(ratr);
                tr.AddNewlyCreatedDBObject(ratr, true);
            }

            BlockTable bt   = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

            // Pass 1 — collect Line yang punya KTP (hindari iterasi + append bersamaan)
            List<ObjectId> masterLines = new List<ObjectId>();
            foreach (ObjectId id in btr)
            {
                if (id.IsErased) continue;
                if (id.ObjectClass.IsDerivedFrom(RXClass.GetClass(typeof(Line))))
                    masterLines.Add(id);
            }

            // Pass 2 — bake Polyline + Hatch per Line
            foreach (ObjectId lineId in masterLines)
            {
                Line? wallLine = tr.GetObject(lineId, OpenMode.ForRead) as Line;
                if (wallLine == null) continue;

                WallData? data = XDataManager.GetWallData(wallLine);
                if (data == null) continue;

                // 1. CETAK POLYLINE KOTAK
                Polyline wallPoly = BuildWallPolyline(wallLine, data.Thickness);
                btr.AppendEntity(wallPoly);
                tr.AddNewlyCreatedDBObject(wallPoly, true);

                // Stempel VIEW di Polyline
                TagAsView(wallPoly);

                // 2. CETAK HATCH ANSI31
                Hatch wallHatch = new Hatch();
                wallHatch.SetDatabaseDefaults();
                wallHatch.SetHatchPattern(HatchPatternType.PreDefined, "ANSI31");
                wallHatch.PatternScale = 15.0;

                // Wajib AppendEntity sebelum AppendLoop (butuh valid ObjectId)
                btr.AppendEntity(wallHatch);
                tr.AddNewlyCreatedDBObject(wallHatch, true);

                // Hubungkan Polyline sebagai batas Hatch
                ObjectIdCollection boundaryIds = new ObjectIdCollection { wallPoly.ObjectId };
                wallHatch.AppendLoop(HatchLoopTypes.Default, boundaryIds);
                wallHatch.EvaluateHatch(true);

                // Stempel VIEW di Hatch agar ikut ter-purge
                TagAsView(wallHatch);
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static Polyline BuildWallPolyline(Line wallLine, double thickness)
        {
            Point3d start = wallLine.StartPoint;
            Point3d end   = wallLine.EndPoint;
            double  half  = thickness / 2.0;

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

        /// <summary>
        /// Tempelkan stempel CORECAD_VIEW ke entitas.
        /// Entitas sudah harus ada di database (AppendEntity sudah dipanggil).
        /// </summary>
        private static void TagAsView(Entity ent)
        {
            using (ResultBuffer rb = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, APP_VIEW_NAME),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, "IS_VIEW:TRUE")
            ))
            {
                ent.XData = rb;
            }
        }
    }
}
