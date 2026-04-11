using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using CoreCAD.Persistence;
using CoreCAD.Models;
using CoreCAD.Utils;
using System.Collections.Generic;

namespace CoreCAD.Engine
{
    // ═══════════════════════════════════════════════════════════════════════════
    // INTERNAL DATA MODEL — hanya dipakai di Engine, tidak di-expose keluar.
    // Menyimpan data mentah + vertex hasil kalkulasi untuk satu dinding.
    // ═══════════════════════════════════════════════════════════════════════════
    internal class WallModel
    {
        public ObjectId LineId;
        public double   Thickness;
        public Point2d  Start;   // 2D projection titik awal garis As
        public Point2d  End;     // 2D projection titik akhir garis As

        // Vertex sudut — default: kotak siku-siku, diperbarui setelah miter resolve
        public Point2d V0;  // Start-Left
        public Point2d V1;  // End-Left
        public Point2d V2;  // End-Right
        public Point2d V3;  // Start-Right

        /// <summary>Arah dinding (Start → End), sudah dinormalisasi.</summary>
        public Vector2d Direction =>
            new Vector2d(End.X - Start.X, End.Y - Start.Y).GetNormal();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // VIEW GENERATOR — Mesin Cetak dengan pipeline 5-Step Look-Ahead
    // ═══════════════════════════════════════════════════════════════════════════
    public static class ViewGenerator
    {
        public const string APP_VIEW_NAME = "CORECAD_VIEW";

        // ── PURGE ────────────────────────────────────────────────────────────
        /// <summary>
        /// Hapus semua view lama (Polyline + Hatch bertanda CORECAD_VIEW).
        /// 2-pass: collect dulu → erase setelah iterasi selesai.
        /// </summary>
        public static void PurgeOldViews(Autodesk.AutoCAD.DatabaseServices.Database db, Transaction tr)
        {
            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

            // Pass 1 — collect
            List<ObjectId> toErase = new List<ObjectId>();
            foreach (ObjectId id in btr)
            {
                if (id.IsErased) continue;
                Entity? ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null) continue;
                ResultBuffer? rb = ent.GetXDataForApplication(APP_VIEW_NAME);
                if (rb != null) { toErase.Add(id); rb.Dispose(); }
            }

            // Pass 2 — erase
            foreach (ObjectId id in toErase)
            {
                Entity ent = (Entity)tr.GetObject(id, OpenMode.ForWrite);
                if (!ent.IsErased) ent.Erase(true);
            }
        }

        // ── BAKE (pipeline 5 Step) ────────────────────────────────────────────
        /// <summary>
        /// Pipeline utama:
        ///   A → Kumpulkan WallModel dari semua Line KTP
        ///   B → Deteksi Shared Node (titik bersama antar dinding)
        ///   C → Kalkulasi Miter atau biarkan kotak (siku-siku)
        ///   D → Bangun Polyline dari vertex hasil kalkulasi
        ///   E → Cetak Polyline + Hatch ke drawing
        /// </summary>
        public static void BakeAllWalls(Autodesk.AutoCAD.DatabaseServices.Database db, Transaction tr)
        {
            EnsureRegApp(db, tr);

            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

            // ── STEP A: Kumpulkan semua WallModel ────────────────────────────
            List<WallModel> walls = BuildWallModels(btr, tr);
            if (walls.Count == 0) return;

            // ── STEP B & C: Look-Ahead — deteksi junction, hitung miter ──────
            ResolveMiters(walls);

            // ── STEP D & E: Cetak Polyline + Hatch ───────────────────────────
            foreach (WallModel model in walls)
            {
                // D — Polyline dari vertex hasil kalkulasi
                Polyline wallPoly = BuildPolylineFromModel(model);
                btr.AppendEntity(wallPoly);
                tr.AddNewlyCreatedDBObject(wallPoly, true);
                TagAsView(wallPoly);

                // E — Hatch ANSI31
                Hatch wallHatch = new Hatch();
                wallHatch.SetDatabaseDefaults();
                wallHatch.SetHatchPattern(HatchPatternType.PreDefined, "ANSI31");
                wallHatch.PatternScale = 15.0;
                btr.AppendEntity(wallHatch);
                tr.AddNewlyCreatedDBObject(wallHatch, true);
                wallHatch.AppendLoop(HatchLoopTypes.Default, new ObjectIdCollection { wallPoly.ObjectId });
                wallHatch.EvaluateHatch(true);
                TagAsView(wallHatch);
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // PRIVATE PIPELINE STEPS
        // ══════════════════════════════════════════════════════════════════════

        // ── Step A ────────────────────────────────────────────────────────────
        private static List<WallModel> BuildWallModels(BlockTableRecord btr, Transaction tr)
        {
            // Collect Line IDs dulu (hindari iterasi + query bersamaan)
            List<ObjectId> lineIds = new List<ObjectId>();
            foreach (ObjectId id in btr)
            {
                if (!id.IsErased && id.ObjectClass.IsDerivedFrom(RXClass.GetClass(typeof(Line))))
                    lineIds.Add(id);
            }

            List<WallModel> models = new List<WallModel>();
            foreach (ObjectId lineId in lineIds)
            {
                Line? line = tr.GetObject(lineId, OpenMode.ForRead) as Line;
                if (line == null) continue;

                WallData? data = XDataManager.GetWallData(line);
                if (data == null) continue;  // Bukan Line KTP CoreCAD, skip

                Point2d s    = new Point2d(line.StartPoint.X, line.StartPoint.Y);
                Point2d e    = new Point2d(line.EndPoint.X,   line.EndPoint.Y);
                double  half = data.Thickness / 2.0;

                // Vektor normalisasi arah dan perpendikular kiri
                Vector2d dir  = new Vector2d(e.X - s.X, e.Y - s.Y).GetNormal();
                Vector2d perp = new Vector2d(-dir.Y, dir.X);

                // Vertex default: sudut siku-siku (90°)
                models.Add(new WallModel
                {
                    LineId    = lineId,
                    Thickness = data.Thickness,
                    Start     = s,
                    End       = e,
                    V0 = new Point2d(s.X + perp.X * half, s.Y + perp.Y * half),  // Start-Left
                    V1 = new Point2d(e.X + perp.X * half, e.Y + perp.Y * half),  // End-Left
                    V2 = new Point2d(e.X - perp.X * half, e.Y - perp.Y * half),  // End-Right
                    V3 = new Point2d(s.X - perp.X * half, s.Y - perp.Y * half),  // Start-Right
                });
            }
            return models;
        }

        // ── Step B & C ────────────────────────────────────────────────────────
        /// <summary>
        /// Iterasi semua pasangan dinding (O(n²)).
        /// Untuk setiap shared node, panggil ApplyMiter untuk update vertex wall A.
        /// Wall B akan di-update saat giliran loop berikutnya (A & B ditukar).
        /// </summary>
        private static void ResolveMiters(List<WallModel> walls)
        {
            for (int i = 0; i < walls.Count; i++)
            {
                for (int j = 0; j < walls.Count; j++)
                {
                    if (i == j) continue;

                    WallModel a = walls[i];
                    WallModel b = walls[j];

                    // 4 kombinasi endpoint: A.Start-B.Start, A.Start-B.End, dll.
                    if (WallMath.IsSharedNode(a.Start, b.Start))
                        ApplyMiter(a, isStartOfA: true,  b, isStartOfB: true);

                    if (WallMath.IsSharedNode(a.Start, b.End))
                        ApplyMiter(a, isStartOfA: true,  b, isStartOfB: false);

                    if (WallMath.IsSharedNode(a.End, b.Start))
                        ApplyMiter(a, isStartOfA: false, b, isStartOfB: true);

                    if (WallMath.IsSharedNode(a.End, b.End))
                        ApplyMiter(a, isStartOfA: false, b, isStartOfB: false);
                }
            }
        }

        /// <summary>
        /// Hitung dan terapkan titik miter ke vertex wall A di sisi junction-nya.
        /// Algoritma:
        ///   1. Arahkan vektor "menjauh dari junction" untuk A dan B
        ///   2. Offset ke kiri → "left edge origin" di titik junction
        ///   3. Intersect kedua left edge → titik miter kiri
        ///   4. Idem untuk right edge → titik miter kanan
        ///   5. Update V0/V3 (jika start) atau V1/V2 (jika end) wall A
        /// </summary>
        private static void ApplyMiter(WallModel a, bool isStartOfA, WallModel b, bool isStartOfB)
        {
            // Arah A menjauh dari junction
            Vector2d dirA = isStartOfA
                ? a.Direction
                : new Vector2d(-a.Direction.X, -a.Direction.Y);

            // Arah B menjauh dari junction
            Vector2d dirB = isStartOfB
                ? b.Direction
                : new Vector2d(-b.Direction.X, -b.Direction.Y);

            // Perpendikular kiri masing-masing (rotating 90° counter-clockwise dari arah jalan)
            Vector2d perpA = new Vector2d(-dirA.Y,  dirA.X);
            Vector2d perpB = new Vector2d(-dirB.Y,  dirB.X);

            // Titik junction (shared node)
            Point2d J = isStartOfA ? a.Start : a.End;

            double halfA = a.Thickness / 2.0;
            double halfB = b.Thickness / 2.0;

            // ── LEFT MITER ──────────────────────────────────────────────────
            // Titik asal left edge di junction untuk masing-masing dinding
            Point2d leftA = new Point2d(J.X + perpA.X * halfA, J.Y + perpA.Y * halfA);
            Point2d leftB = new Point2d(J.X + perpB.X * halfB, J.Y + perpB.Y * halfB);

            // Perpotongan dua garis tepi kiri (infinite line)
            Point2d? leftMiter = WallMath.GetLineIntersection(leftA, dirA, leftB, dirB);

            // ── RIGHT MITER ─────────────────────────────────────────────────
            Point2d rightA = new Point2d(J.X - perpA.X * halfA, J.Y - perpA.Y * halfA);
            Point2d rightB = new Point2d(J.X - perpB.X * halfB, J.Y - perpB.Y * halfB);

            Point2d? rightMiter = WallMath.GetLineIntersection(rightA, dirA, rightB, dirB);

            // ── UPDATE VERTEX WALL A ─────────────────────────────────────────
            if (isStartOfA)
            {
                if (leftMiter.HasValue)  a.V0 = leftMiter.Value;   // Start-Left
                if (rightMiter.HasValue) a.V3 = rightMiter.Value;  // Start-Right
            }
            else
            {
                if (leftMiter.HasValue)  a.V1 = leftMiter.Value;   // End-Left
                if (rightMiter.HasValue) a.V2 = rightMiter.Value;  // End-Right
            }
        }

        // ── Step D ────────────────────────────────────────────────────────────
        private static Polyline BuildPolylineFromModel(WallModel model)
        {
            Polyline poly = new Polyline();
            poly.SetDatabaseDefaults();
            poly.AddVertexAt(0, model.V0, 0, 0, 0);
            poly.AddVertexAt(1, model.V1, 0, 0, 0);
            poly.AddVertexAt(2, model.V2, 0, 0, 0);
            poly.AddVertexAt(3, model.V3, 0, 0, 0);
            poly.Closed = true;
            return poly;
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static void EnsureRegApp(Autodesk.AutoCAD.DatabaseServices.Database db, Transaction tr)
        {
            RegAppTable rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (!rat.Has(APP_VIEW_NAME))
            {
                rat.UpgradeOpen();
                RegAppTableRecord ratr = new RegAppTableRecord { Name = APP_VIEW_NAME };
                rat.Add(ratr);
                tr.AddNewlyCreatedDBObject(ratr, true);
            }
        }

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
