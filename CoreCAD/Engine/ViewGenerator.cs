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
    // ═══════════════════════════════════════════════════════════════════════════
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
        public Point2d  Start;
        public Point2d  End;

        public Point2d V0; // Start-Left
        public Point2d V1; // End-Left
        public Point2d V2; // End-Right
        public Point2d V3; // Start-Right

        public bool HasStartJoin = false;
        public bool HasEndJoin   = false;

        public Vector2d Direction =>
            new Vector2d(End.X - Start.X, End.Y - Start.Y).GetNormal();
    }

    public static class ViewGenerator
    {
        public const string APP_VIEW_NAME = "CORECAD_VIEW";

        public static void PurgeOldViews(Autodesk.AutoCAD.DatabaseServices.Database db, Transaction tr)
        {
            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

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

            foreach (ObjectId id in toErase)
            {
                Entity ent = (Entity)tr.GetObject(id, OpenMode.ForWrite);
                if (!ent.IsErased) ent.Erase(true);
            }
        }

        public static void BakeAllWalls(Autodesk.AutoCAD.DatabaseServices.Database db, Transaction tr)
        {
            EnsureRegApp(db, tr);

            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

            // [STANDARD PRE-FLIGHT]
            StandardManager.Instance.Load();
            StandardManager.Instance.EnsureLayer(db, tr, "Wall");
            StandardManager.Instance.EnsureLayer(db, tr, "WallHatch");
            StandardManager.Instance.EnsureLayer(db, tr, "Door");
            StandardManager.Instance.EnsureLayer(db, tr, "Grip");

            var ed = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument.Editor;

            // ── STEP A: Kumpulkan models & openings ────────────────────────────
            ed.WriteMessage("\n[Step A] Building geometry models...");
            List<OpeningInstance> openings = new List<OpeningInstance>();
            List<WallModel> walls = BuildWallModels(btr, tr, openings);
            if (walls.Count == 0 && openings.Count == 0) return;

            // ── STEP B & C: Resolving junctions ────────────────────────────
            ed.WriteMessage("\n[Step B/C] Resolving junctions...");
            ResolveMiters(walls);

            // ── STEP D/E/F: Generating visuals ────────────────────────────
            ed.WriteMessage("\n[Step D/E/F] Generating visual skins and hatches...");
            foreach (WallModel model in walls)
            {
                Polyline? wallPoly = BuildPolylineFromModel(model);
                if (wallPoly == null) continue;

                wallPoly.Visible = false;
                btr.AppendEntity(wallPoly);
                tr.AddNewlyCreatedDBObject(wallPoly, true);
                TagAsView(wallPoly);

                Hatch wallHatch = BuildHatch(wallPoly);
                btr.AppendEntity(wallHatch);
                tr.AddNewlyCreatedDBObject(wallHatch, true);
                TagAsView(wallHatch);

                GenerateVisualOutline(model, tr, btr);
            }

            // ── STEP G: Symbols ────────────────────────────
            ed.WriteMessage("\n[Step G] Inserting symbols...");
            foreach (var op in openings)
            {
                InsertOpeningBlock(op, tr, btr);
            }

            // ── STEP H: Grips ────────────────────────────
            ed.WriteMessage("\n[Step H] Creating interactive grips...");
            GenerateOpeningGrips(db, tr, btr);
        }

        public static void BakeSingleWall(Autodesk.AutoCAD.DatabaseServices.Database db, ObjectId lineId)
        {
            Core.Services.WallSyncReactor.IsSuspended = true;
            try
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BakeAllWalls(db, tr);
                    tr.Commit();
                }
            }
            finally
            {
                Core.Services.WallSyncReactor.IsSuspended = false;
            }
        }

        private static List<WallModel> BuildWallModels(BlockTableRecord btr, Transaction tr, List<OpeningInstance> openingInstances)
        {
            var editor = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument.Editor;
            List<WallModel> models = new List<WallModel>();
            string targetLayer = StandardManager.Instance.GetLayer("Centerline");

            ObjectId[] ids = btr.Cast<ObjectId>().ToArray();
            foreach (ObjectId id in ids)
            {
                if (id.IsErased || !id.ObjectClass.IsDerivedFrom(RXClass.GetClass(typeof(Line)))) continue;

                Line? line = tr.GetObject(id, OpenMode.ForRead) as Line;
                if (line == null || line.Layer != targetLayer) continue;

                WallData? data = XDataManager.GetWallData(line);
                if (data == null) continue;

                Point2d S = new Point2d(line.StartPoint.X, line.StartPoint.Y);
                Point2d E = new Point2d(line.EndPoint.X,   line.EndPoint.Y);
                Vector2d fullDir = (E - S).GetNormal();
                double fullLen = (E - S).Length;

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
                for (int i = 0; i < splitPoints.Count - 1; i++)
                {
                    double d1 = splitPoints[i];
                    double d2 = splitPoints[i+1];
                    if (d2 - d1 < 1.0) continue;

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
                        LineId = id,
                        Thickness = data.Thickness,
                        Start = segS, End = segE,
                        V0 = segS + perp * half,
                        V1 = segE + perp * half,
                        V2 = segE - perp * half,
                        V3 = segS - perp * half
                    });
                }
                editor.WriteMessage($"\n[Debug] Wall {data.Id.Substring(0,8)}: Len {fullLen:F0}, Openings: {data.Openings.Count}, Segments: {segmentCount}");
            }
            return models;
        }

        private static void ResolveMiters(List<WallModel> walls)
        {
            for (int i = 0; i < walls.Count; i++)
            {
                for (int j = 0; j < walls.Count; j++)
                {
                    if (i == j) continue;
                    WallModel a = walls[i];
                    WallModel b = walls[j];

                    bool useButt = Math.Abs(a.Thickness - b.Thickness) > 1.0;

                    if (WallMath.IsSharedNode(a.Start, b.Start)) {
                        if (useButt) ApplyButtJoin(a, true, b); else ApplyMiter(a, true, b, true);
                    }
                    if (WallMath.IsSharedNode(a.Start, b.End)) {
                        if (useButt) ApplyButtJoin(a, true, b); else ApplyMiter(a, true, b, false);
                    }
                    if (WallMath.IsSharedNode(a.End, b.Start)) {
                        if (useButt) ApplyButtJoin(a, false, b); else ApplyMiter(a, false, b, true);
                    }
                    if (WallMath.IsSharedNode(a.End, b.End)) {
                        if (useButt) ApplyButtJoin(a, false, b); else ApplyMiter(a, false, b, false);
                    }

                    if (WallMath.IsPointOnSegment(b.Start, a.Start, a.End)) ApplyTJoin(b, true, a);
                    if (WallMath.IsPointOnSegment(b.End, a.Start, a.End)) ApplyTJoin(b, false, a);
                }
            }
        }

        private static void ApplyMiter(WallModel a, bool isStartOfA, WallModel b, bool isStartOfB)
        {
            Vector2d u1 = isStartOfA ? a.Direction : -a.Direction;
            Vector2d u2 = isStartOfB ? b.Direction : -b.Direction;

            double cross = (u1.X * u2.Y) - (u1.Y * u2.X);
            if (System.Math.Abs(cross) < 0.001) return;

            Vector2d? vBis = WallMath.CalculateBisector(u1, u2);
            if (!vBis.HasValue) return;
            Vector2d bis = vBis.Value;

            double dot = u1.X * u2.X + u1.Y * u2.Y;
            dot = System.Math.Clamp(dot, -1.0, 1.0);
            double theta = System.Math.Acos(dot);
            double miterLen = (a.Thickness / 2.0) / (System.Math.Sin(theta / 2.0) + 1e-9);

            double maxLen = a.Thickness * 2.0;
            if (miterLen > maxLen) miterLen = maxLen;

            Point2d J = isStartOfA ? a.Start : a.End;
            Point2d J_other = isStartOfA ? a.End : a.Start;

            Point2d pInner = new Point2d(J.X + bis.X * miterLen, J.Y + bis.Y * miterLen);
            Point2d pOuter = new Point2d(J.X - bis.X * miterLen, J.Y - bis.Y * miterLen);

            Point2d pLeft = (cross > 0) ? pInner : pOuter;
            Point2d pRight = (cross > 0) ? pOuter : pInner;

            if (isStartOfA) {
                if (WallMath.ValidateMiterPoint(J, J_other, pLeft)) a.V0 = pLeft;
                if (WallMath.ValidateMiterPoint(J, J_other, pRight)) a.V3 = pRight;
                a.HasStartJoin = true; 
            } else {
                if (WallMath.ValidateMiterPoint(J, J_other, pLeft)) a.V2 = pLeft;
                if (WallMath.ValidateMiterPoint(J, J_other, pRight)) a.V1 = pRight;
                a.HasEndJoin = true;
            }
        }

        private static void ApplyButtJoin(WallModel a, bool isStartOfA, WallModel b)
        {
            if (a.Thickness > b.Thickness) return;

            Vector2d dirA = isStartOfA ? a.Direction : -a.Direction;
            Vector2d perpA = new Vector2d(-dirA.Y, dirA.X);
            Vector2d dirB = b.Direction;
            Vector2d perpB = new Vector2d(-dirB.Y, dirB.X);

            Point2d J = isStartOfA ? a.Start : a.End;
            double halfA = a.Thickness / 2.0;
            double halfB = b.Thickness / 2.0;

            double sideCheck = dirA.X * perpB.X + dirA.Y * perpB.Y;
            double offsetB = (sideCheck > 0) ? -halfB : halfB;

            Point2d faceBPoint = new Point2d(b.Start.X + perpB.X * offsetB, b.Start.Y + perpB.Y * offsetB);
            
            Point2d edgeLeftA = new Point2d(J.X + perpA.X * halfA, J.Y + perpA.Y * halfA);
            Point2d? pLeft = WallMath.GetLineIntersection(edgeLeftA, dirA, faceBPoint, dirB);

            Point2d edgeRightA = new Point2d(J.X - perpA.X * halfA, J.Y - perpA.Y * halfA);
            Point2d? pRight = WallMath.GetLineIntersection(edgeRightA, dirA, faceBPoint, dirB);

            if (isStartOfA) {
                if (pLeft.HasValue) a.V0 = pLeft.Value;
                if (pRight.HasValue) a.V3 = pRight.Value;
                a.HasStartJoin = true;
            } else {
                if (pLeft.HasValue) a.V2 = pLeft.Value;
                if (pRight.HasValue) a.V1 = pRight.Value;
                a.HasEndJoin = true;
            }
        }

        private static void ApplyTJoin(WallModel branch, bool isStartOfB, WallModel main)
        {
            double offsetDist = main.Thickness / 2.0;
            Vector2d dir = branch.Direction;
            if (!isStartOfB) dir = -dir;
            Vector2d retract = dir * offsetDist;

            if (isStartOfB) {
                branch.V0 = branch.V0 + retract;
                branch.V3 = branch.V3 + retract;
                branch.HasStartJoin = true;
            } else {
                branch.V1 = branch.V1 + retract;
                branch.V2 = branch.V2 + retract;
                branch.HasEndJoin = true;
            }
        }

        private static Polyline? BuildPolylineFromModel(WallModel model)
        {
            if (!WallMath.IsSimplePolygon(model.V0, model.V1, model.V2, model.V3)) return null;
            Polyline poly = new Polyline();
            poly.SetDatabaseDefaults();
            poly.AddVertexAt(0, model.V0, 0, 0, 0);
            poly.AddVertexAt(1, model.V1, 0, 0, 0);
            poly.AddVertexAt(2, model.V2, 0, 0, 0);
            poly.AddVertexAt(3, model.V3, 0, 0, 0);
            poly.Closed = true;
            return poly;
        }

        private static Hatch BuildHatch(Polyline boundary)
        {
            Hatch hatch = new Hatch();
            hatch.SetDatabaseDefaults();
            hatch.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
            hatch.Layer = StandardManager.Instance.GetLayer("WallHatch");
            hatch.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByLayer, 256);
            hatch.AppendLoop(HatchLoopTypes.Default, new ObjectIdCollection { boundary.ObjectId });
            hatch.EvaluateHatch(true);
            return hatch;
        }

        private static void GenerateVisualOutline(WallModel model, Transaction tr, BlockTableRecord btr)
        {
            CreateVisualLine(model.V0, model.V1, tr, btr);
            CreateVisualLine(model.V2, model.V3, tr, btr);
            if (!model.HasStartJoin) CreateVisualLine(model.V3, model.V0, tr, btr);
            if (!model.HasEndJoin)   CreateVisualLine(model.V1, model.V2, tr, btr);
        }

        private static void CreateVisualLine(Point2d start, Point2d end, Transaction tr, BlockTableRecord btr)
        {
            Line line = new Line(new Point3d(start.X, start.Y, 0), new Point3d(end.X, end.Y, 0));
            line.SetDatabaseDefaults();
            line.Layer = StandardManager.Instance.GetLayer("Wall");
            line.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByLayer, 256);
            TagAsView(line);
            btr.AppendEntity(line);
            tr.AddNewlyCreatedDBObject(line, true);
        }

        private static void GenerateOpeningGrips(Database db, Transaction tr, BlockTableRecord btr)
        {
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
                    
                    TagAsView(grip);
                    XDataManager.AttachGripData(tr, db, grip, wallLine.Handle.ToString(), i);
                    btr.AppendEntity(grip);
                    tr.AddNewlyCreatedDBObject(grip, true);
                }
            }
        }

        private static void InsertOpeningBlock(OpeningInstance op, Transaction tr, BlockTableRecord btr)
        {
            Database db = btr.Database;
            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (string.IsNullOrEmpty(op.BlockName) || !bt.Has(op.BlockName)) return;

            BlockReference br = new BlockReference(new Point3d(op.Location.X, op.Location.Y, 0), bt[op.BlockName]);
            br.SetDatabaseDefaults();
            br.Rotation = op.Direction.Angle;
            br.Layer = StandardManager.Instance.GetLayer("Door");
            
            TagAsView(br);
            btr.AppendEntity(br);
            tr.AddNewlyCreatedDBObject(br, true);
        }

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
