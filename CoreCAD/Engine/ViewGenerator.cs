using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using CoreCAD.Persistence;
using CoreCAD.Models;
using CoreCAD.Utils;
using CoreCAD.Core;
using System.Collections.Generic;
using System.Linq;

namespace CoreCAD.Engine
{
    // ═══════════════════════════════════════════════════════════════════════════
    // INTERNAL DATA MODEL — hanya dipakai di Engine, tidak di-expose keluar.
    // Menyimpan data mentah + vertex hasil kalkulasi untuk satu dinding.
    // ═══════════════════════════════════════════════════════════════════════════
    /// <summary>Data untuk penempatan simbol opening (pintu/jendela).</summary>
    internal class OpeningInstance
    {
        public string BlockName = "";
        public Point2d Location;
        public Vector2d Direction;
        public double Thickness;
    }

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

        // Flags untuk menyembunyikan "Cap" (Garis penutup ujung) jika tersambung
        public bool HasStartJoin = false;
        public bool HasEndJoin   = false;

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
            ObjectId[] ids = btr.Cast<ObjectId>().ToArray();
            foreach (ObjectId id in ids)
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

            // [STANDARD] Pastikan Layer Standard ada sebelum proses drafting
            StandardManager.Instance.Load(); // Refresh config
            StandardManager.Instance.EnsureLayer(db, tr, "Wall");
            StandardManager.Instance.EnsureLayer(db, tr, "WallHatch");
            StandardManager.Instance.EnsureLayer(db, tr, "Door");
            StandardManager.Instance.EnsureLayer(db, tr, "Grip");

            // ── STEP A: Kumpulkan semua WallModel (Sliced) & OpeningInstance ────────────────────────────
            var ed = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument.Editor;
            ed.WriteMessage("\n[Step A] Building geometry models...");
            List<OpeningInstance> openings = new List<OpeningInstance>();
            List<WallModel> walls = BuildWallModels(btr, tr, openings);
            if (walls.Count == 0 && openings.Count == 0) return;

            // ── STEP B & C: Look-Ahead — deteksi junction, hitung miter ──────
            ed.WriteMessage("\n[Step B/C] Resolving junctions...");
            ResolveMiters(walls);

            // ── STEP D & E: Cetak raga dinding (Invisible) + Hatch + Outline Visual ──────
            ed.WriteMessage("\n[Step D/E/F] Generating visual skins and hatches...");
            foreach (WallModel model in walls)
            {
                // D — Polyline pembatas (untuk Hatch), dibuat Invisible
                Polyline? wallPoly = BuildPolylineFromModel(model);
                if (wallPoly == null) continue;

                wallPoly.Visible = false; // [ANTI-DIAGONAL] Sembunyikan garis kotak
                btr.AppendEntity(wallPoly);
                tr.AddNewlyCreatedDBObject(wallPoly, true);
                TagAsView(wallPoly);

                // E — Hatch ANSI31
                Hatch wallHatch = BuildHatch(wallPoly);
                btr.AppendEntity(wallHatch);
                tr.AddNewlyCreatedDBObject(wallHatch, true);
                TagAsView(wallHatch);

                // F — [ANTI-DIAGONAL] Gambar garis tepi visual (Skins & Caps)
                GenerateVisualOutline(model, tr, btr);
            }

            // ── STEP G: Insert Opening Symbols (Blocks) ──────────────────────
            ed.WriteMessage("\n[Step G] Inserting symbols...");
            foreach (var op in openings)
            {
                InsertOpeningBlock(op, tr, btr);
            }

            // ── STEP H: [V2] Generate Interactive Grips (Proxy Lines) ────────
            ed.WriteMessage("\n[Step H] Creating interactive grips...");
            GenerateOpeningGrips(db, tr, btr);
        }

        /// <summary>
        /// [V2] Update visual satu dinding saja (dipanggil oleh Reactor).
        /// </summary>
        public static void BakeSingleWall(Autodesk.AutoCAD.DatabaseServices.Database db, ObjectId lineId)
        {
            // Tangguhkan reactor agar tidak memicu recursion
            Core.Services.WallSyncReactor.IsSuspended = true;
            try
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    // Untuk saat ini, kita tetap panggil Global BakeAll 
                    // namun kedepannya bisa di-optimize per-entity.
                    BakeAllWalls(db, tr);
                    tr.Commit();
                }
            }
            finally
            {
                Core.Services.WallSyncReactor.IsSuspended = false;
            }
        }

        private static void GenerateOpeningGrips(Database db, Transaction tr, BlockTableRecord btr)
        {
            // Kita scan ulang line yang punya wall data
            ObjectId[] ids = btr.Cast<ObjectId>().ToArray();
            foreach (ObjectId id in ids)
            {
                if (id.IsErased) continue;
                Line? wallLine = tr.GetObject(id, OpenMode.ForRead) as Line;
                if (wallLine == null) continue;

                WallData? data = XDataManager.GetWallData(wallLine);
                if (data == null || data.Openings.Count == 0) continue;

                Point3d S = wallLine.StartPoint;
                Point3d E = wallLine.EndPoint;
                Vector3d dir = (E - S).GetNormal();

                for (int i = 0; i < data.Openings.Count; i++)
                {
                    var op = data.Openings[i];
                    double startDist = op.Position - (op.Width / 2.0);
                    double endDist   = op.Position + (op.Width / 2.0);

                    Line grip = new Line(S + dir * startDist, S + dir * endDist);
                    grip.SetDatabaseDefaults();
                    grip.Layer = StandardManager.Instance.GetLayer("Grip");
                    grip.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByLayer, 256);
                    
                    TagAsView(grip); // Agar kena Purge saat Rebuild
                    XDataManager.AttachGripData(tr, db, grip, wallLine.Handle.ToString(), i);

                    btr.AppendEntity(grip);
                    tr.AddNewlyCreatedDBObject(grip, true);
                }
            }
        }

        private static Hatch BuildHatch(Polyline boundary)
        {
            Hatch hatch = new Hatch();
            hatch.SetDatabaseDefaults();
            // [STANDARD] Ganti Pattern ke SOLID sesuai permintaan user
            hatch.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
            
            // Set Layer & Warna dari Standard
            hatch.Layer = StandardManager.Instance.GetLayer("WallHatch");
            hatch.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByLayer, 256); // ByLayer

            hatch.AppendLoop(HatchLoopTypes.Default, new ObjectIdCollection { boundary.ObjectId });
            hatch.EvaluateHatch(true);
            return hatch;
        }

        private static void GenerateVisualOutline(WallModel model, Transaction tr, BlockTableRecord btr)
        {
            // Skin Kiri (V0 -> V1) & Kanan (V2 -> V3)
            CreateVisualLine(model.V0, model.V1, tr, btr);
            CreateVisualLine(model.V2, model.V3, tr, btr);

            // Cap Start (V3 -> V0) - Jika TIDAK ADA joint di awal
            if (!model.HasStartJoin)
                CreateVisualLine(model.V3, model.V0, tr, btr);

            // Cap End (V1 -> V2) - Jika TIDAK ADA joint di akhir
            if (!model.HasEndJoin)
                CreateVisualLine(model.V1, model.V2, tr, btr);
        }

        private static void CreateVisualLine(Point2d start, Point2d end, Transaction tr, BlockTableRecord btr)
        {
            Line line = new Line(new Point3d(start.X, start.Y, 0), new Point3d(end.X, end.Y, 0));
            line.SetDatabaseDefaults();

            // [STANDARD] Set Layer & Warna sesuai standard "Wall"
            line.Layer = StandardManager.Instance.GetLayer("Wall");
            line.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByLayer, 256); // ByLayer

            TagAsView(line);
            btr.AppendEntity(line);
            tr.AddNewlyCreatedDBObject(line, true);
        }

        // ══════════════════════════════════════════════════════════════════════
        // PRIVATE PIPELINE STEPS
        // ══════════════════════════════════════════════════════════════════════

        // ── Step A (Slicing Logic) ────────────────────────────────────────────
        private static List<WallModel> BuildWallModels(BlockTableRecord btr, Transaction tr, List<OpeningInstance> openingInstances)
        {
            var editor = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument.Editor;
            
            List<ObjectId> lineIds = new List<ObjectId>();
            ObjectId[] ids = btr.Cast<ObjectId>().ToArray();
            foreach (ObjectId id in ids)
            {
                if (!id.IsErased && id.ObjectClass.IsDerivedFrom(RXClass.GetClass(typeof(Line))))
                    lineIds.Add(id);
            }

            List<WallModel> models = new List<WallModel>();
            string targetLayer = StandardManager.Instance.GetLayer("Centerline");

            foreach (ObjectId lineId in lineIds)
            {
                Line? line = tr.GetObject(lineId, OpenMode.ForRead) as Line;
                if (line == null) continue;

                // [V2 SAFETY] Skip visual skins/caps: Hanya proses garis di layer Centerline
                if (line.Layer != targetLayer) continue;

                WallData? data = XDataManager.GetWallData(line);
                if (data == null) continue;

                Point2d S = new Point2d(line.StartPoint.X, line.StartPoint.Y);
                Point2d E = new Point2d(line.EndPoint.X,   line.EndPoint.Y);
                Vector2d fullDir = (E - S).GetNormal();
                double fullLen = (E - S).Length;

                // [OPENING SLICER]
                List<double> splitPoints = new List<double> { 0.0, fullLen };
                foreach (var op in data.Openings)
                {
                    double startOp = op.Position - (op.Width / 2.0);
                    double endOp   = op.Position + (op.Width / 2.0);
                    
                    if (startOp > 0.1 && startOp < fullLen - 0.1) splitPoints.Add(startOp);
                    if (endOp > 0.1 && endOp < fullLen - 0.1) splitPoints.Add(endOp);

                    openingInstances.Add(new OpeningInstance {
                        BlockName = op.BlockName,
                        Location = S + (fullDir * op.Position),
                        Direction = fullDir,
                        Thickness = data.Thickness
                    });
                }
                splitPoints.Sort();

                int segmentCount = 0;
                // Build Solid Segments
                for (int i = 0; i < splitPoints.Count - 1; i++)
                {
                    double d1 = splitPoints[i];
                    double d2 = splitPoints[i+1];
                    if (d2 - d1 < 1.0) continue; // Safety: abaikan yang < 1mm

                    double mid = (d1 + d2) / 2.0;

                    bool isGap = false;
                    foreach (var op in data.Openings)
                    {
                        double startOp = op.Position - (op.Width / 2.0);
                        double endOp   = op.Position + (op.Width / 2.0);
                        if (mid > startOp + 0.1 && mid < endOp - 0.1) { isGap = true; break; }
                    }

                    if (isGap) continue;

                    segmentCount++;
                    Point2d segS = S + (fullDir * d1);
                    Point2d segE = S + (fullDir * d2);
                    double half = data.Thickness / 2.0;
                    Vector2d perp = new Vector2d(-fullDir.Y, fullDir.X);

                    models.Add(new WallModel {
                        LineId = lineId,
                        Thickness = data.Thickness,
                        Start = segS,
                        End   = segE,
                        V0 = segS + perp * half,
                        V1 = segE + perp * half,
                        V2 = segE - perp * half,
                        V3 = segS - perp * half,
                    });
                }
                
                editor.WriteMessage($"\n[Debug] Wall {data.Id.Substring(0,8)}: Len {fullLen:F0}, Openings: {data.Openings.Count}, Generated Segments: {segmentCount}");
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

                    // --- PRIORITY CHECK: Ketebalan Berbeda ---
                    // Jika tebal berbeda, gunakan Butt Join (thinner yields to thicker).
                    // Jika tebal sama (toleransi 1mm), gunakan Miter Join.
                    bool useButt = Math.Abs(a.Thickness - b.Thickness) > 1.0;

                    // 4 kombinasi endpoint: A.Start-B.Start, A.Start-B.End, dll.
                    if (WallMath.IsSharedNode(a.Start, b.Start))
                    {
                        if (useButt) ApplyButtJoin(a, isStartOfA: true, b);
                        else ApplyMiter(a, isStartOfA: true, b, isStartOfB: true);
                    }

                    if (WallMath.IsSharedNode(a.Start, b.End))
                    {
                        if (useButt) ApplyButtJoin(a, isStartOfA: true, b);
                        else ApplyMiter(a, isStartOfA: true, b, isStartOfB: false);
                    }

                    if (WallMath.IsSharedNode(a.End, b.Start))
                    {
                        if (useButt) ApplyButtJoin(a, isStartOfA: false, b);
                        else ApplyMiter(a, isStartOfA: false, b, isStartOfB: true);
                    }

                    if (WallMath.IsSharedNode(a.End, b.End))
                    {
                        if (useButt) ApplyButtJoin(a, isStartOfA: false, b);
                        else ApplyMiter(a, isStartOfA: false, b, isStartOfB: false);
                    }

                    // --- [T-JUNCTION DETECTION] ---
                    // Cek jika ujung B menabrak tengah-tengah segmen A
                    if (WallMath.IsPointOnSegment(b.Start, a.Start, a.End))
                        ApplyTJoin(b, isStartOfB: true, a);

                    if (WallMath.IsPointOnSegment(b.End, a.Start, a.End))
                        ApplyTJoin(b, isStartOfB: false, a);
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
            Vector2d u1 = isStartOfA ? a.Direction : -a.Direction;
            Vector2d u2 = isStartOfB ? b.Direction : -b.Direction;

            // [ANTI-NGACO] Cross Product untuk arah belokan dan stabilitas
            double cross = (u1.X * u2.Y) - (u1.Y * u2.X);
            if (System.Math.Abs(cross) < 0.001) return; // Collinear: Tidak perlu miter miring

            // [STEP B] Vektor Bisector (Membagi sudut u1 dan u2)
            Vector2d? vBis = WallMath.CalculateBisector(u1, u2);
            if (!vBis.HasValue) return;
            Vector2d bis = vBis.Value;

            // [STEP C] Hitung Panjang Miter via Sinus Setengah Sudut
            // θ = sudut antara u1 dan u2 (0 s/d 180)
            double dot = u1.X * u2.X + u1.Y * u2.Y;
            dot = System.Math.Clamp(dot, -1.0, 1.0);
            double theta = System.Math.Acos(dot);
            
            double halfA = a.Thickness / 2.0;
            double sinHalfTheta = System.Math.Sin(theta / 2.0);
            
            // L_miter = (T/2) / sin(θ/2)
            double miterLen = halfA / (sinHalfTheta + 1e-9);

            // [LIMITASI] Pagar pengaman: Maksimal 2x tebal dinding
            double maxLen = a.Thickness * 2.0;
            if (miterLen > maxLen) miterLen = maxLen;

            Point2d J = isStartOfA ? a.Start : a.End;
            Point2d J_other = isStartOfA ? a.End : a.Start;

            // [STEP D] Tentukan Inner & Outer Point
            // Bisector (u1+u2) selalu menunjuk ke sisi "Dalam" (Inner)
            Point2d pInner = new Point2d(J.X + bis.X * miterLen, J.Y + bis.Y * miterLen);
            Point2d pOuter = new Point2d(J.X - bis.X * miterLen, J.Y - bis.Y * miterLen);

            // [TOPOLOGI] Mapping berdasarkan arah belokan (Cross) dan lokasi junction (Start/End)
            Point2d pLeft, pRight;
            if (cross > 0) // Belok Kiri: Inner adalah Kiri, Outer adalah Kanan
            {
                pLeft = pInner;
                pRight = pOuter;
            }
            else // Belok Kanan: Inner adalah Kanan, Outer adalah Kiri
            {
                pLeft = pOuter;
                pRight = pInner;
            }

            // [ASSIGNMENT] Gunakan perbaikan topologi Phase 2: End junction menukar mapping
            if (isStartOfA)
            {
                // Start: Left -> V0, Right -> V3
                if (WallMath.ValidateMiterPoint(J, J_other, pLeft)) a.V0 = pLeft;
                if (WallMath.ValidateMiterPoint(J, J_other, pRight)) a.V3 = pRight;
                a.HasStartJoin = true; 
            }
            else
            {
                // End: pLeft -> V2 (Right), pRight -> V1 (Left)
                if (WallMath.ValidateMiterPoint(J, J_other, pLeft)) a.V2 = pLeft;
                if (WallMath.ValidateMiterPoint(J, J_other, pRight)) a.V1 = pRight;
                a.HasEndJoin = true;
            }
        }

        /// <summary>
        /// [PRIORITY] Dinding Thinner mengalah ke Dinding Thicker.
        /// Vertex wall A dibuat menempel rata ke 'muka' wall B.
        /// </summary>
        private static void ApplyButtJoin(WallModel a, bool isStartOfA, WallModel b)
        {
            // Jika A lebih tebal dari B, A tidak boleh mengalah.
            if (a.Thickness > b.Thickness) return;

            Vector2d dirA = isStartOfA ? a.Direction : -a.Direction;
            Vector2d perpA = new Vector2d(-dirA.Y, dirA.X);
            
            Vector2d dirB = b.Direction;
            Vector2d perpB = new Vector2d(-dirB.Y, dirB.X);

            Point2d J = isStartOfA ? a.Start : a.End;
            double halfA = a.Thickness / 2.0;
            double halfB = b.Thickness / 2.0;

            // Cari muka wall B yang paling dekat dengan arah wall A
            // Proyeksikan arah A ke perpB untuk tahu sisi mana yang ditabrak
            double sideCheck = dirA.X * perpB.X + dirA.Y * perpB.Y;
            double offsetB = (sideCheck > 0) ? -halfB : halfB; // Masuk ke arah b.perp atau sebaliknya

            // Garis Muka B: P = b.Start + perpB * offsetB, Direction = b.Direction
            Point2d faceBPoint = new Point2d(b.Start.X + perpB.X * offsetB, b.Start.Y + perpB.Y * offsetB);
            
            // Intersect tepi kiri A dengan muka B
            Point2d edgeLeftA = new Point2d(J.X + perpA.X * halfA, J.Y + perpA.Y * halfA);
            Point2d? pLeft = WallMath.GetLineIntersection(edgeLeftA, dirA, faceBPoint, dirB);

            // Intersect tepi kanan A dengan muka B
            Point2d edgeRightA = new Point2d(J.X - perpA.X * halfA, J.Y - perpA.Y * halfA);
            Point2d? pRight = WallMath.GetLineIntersection(edgeRightA, dirA, faceBPoint, dirB);

            if (isStartOfA)
            {
                // Start: Left -> V0, Right -> V3
                if (pLeft.HasValue) a.V0 = pLeft.Value;
                if (pRight.HasValue) a.V3 = pRight.Value;
                a.HasStartJoin = true;
            }
            else
            {
                // End: Left (backward) -> V2, Right (backward) -> V1
                if (pLeft.HasValue) a.V2 = pLeft.Value;
                if (pRight.HasValue) a.V1 = pRight.Value;
                a.HasEndJoin = true;
            }
        }

        /// <summary>
        /// [ROBUST] T-Junction Algorithm: Skin Projection.
        /// Retract ujung dinding 'branch' sejauh tebal_main/2 agar berhenti di 'Kulit' main.
        /// </summary>
        private static void ApplyTJoin(WallModel branch, bool isStartOfB, WallModel main)
        {
            double offsetDist = main.Thickness / 2.0;

            // Vektor arah branch (menjauhi endpoint)
            Vector2d dir = branch.Direction;
            if (!isStartOfB) dir = -dir; // Arah mundur

            // Offset mundur (Vektor_Arah_B * Jarak)
            Vector2d retract = dir * offsetDist;

            if (isStartOfB)
            {
                // Start B hits Main A
                // V0 (Left) dan V3 (Right) dimundurkan sejauh retract
                branch.V0 = branch.V0 + retract;
                branch.V3 = branch.V3 + retract;
                branch.HasStartJoin = true;
            }
            else
            {
                // End B hits Main A
                // V1 (Left) dan V2 (Right) dimundurkan sejauh retract
                branch.V1 = branch.V1 + retract;
                branch.V2 = branch.V2 + retract;
                branch.HasEndJoin = true;
            }
        }

        // ── Step D ────────────────────────────────────────────────────────────
        private static Polyline? BuildPolylineFromModel(WallModel model)
        {
            // [ANTI-KELIPET] Cek apakah poligon melilit sebelum diproses
            if (!WallMath.IsSimplePolygon(model.V0, model.V1, model.V2, model.V3))
            {
                // Jika melilit, jangan gambar (atau log error)
                return null;
            }

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

        private static void InsertOpeningBlock(OpeningInstance op, Transaction tr, BlockTableRecord btr)
        {
            Database db = btr.Database;
            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);

            // Jika Block tdk ditemukan, gambar kotak placeholder saja
            if (string.IsNullOrEmpty(op.BlockName) || !bt.Has(op.BlockName))
            {
                // Placeholder logic (garis silang atau kotak)
                return;
            }

            BlockReference br = new BlockReference(new Point3d(op.Location.X, op.Location.Y, 0), bt[op.BlockName]);
            br.SetDatabaseDefaults();
            br.Rotation = op.Direction.Angle;
            
            // Set Layer "Door"
            br.Layer = StandardManager.Instance.GetLayer("Door");
            
            TagAsView(br);
            btr.AppendEntity(br);
            tr.AddNewlyCreatedDBObject(br, true);
        }
    }
}
