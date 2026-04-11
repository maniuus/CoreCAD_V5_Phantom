// =============================================================================
// V5 MIGRATION: File ini di-ISOLASI menggunakan Compiler Directive.
// Perintah CC_WALL dan CC_CENTERLINE lama di-disable karena:
//   - CC_WALL       digantikan oleh Commands/CmdWall.cs (sistem KTP baru)
//   - CC_CENTERLINE belum ada penggantinya, akan dimigrasi ke Commands/ belakangan
// Dua CommandMethod dengan nama sama = error NETLOAD di AutoCAD.
// =============================================================================

namespace CoreCAD.Modules.Architecture
{
    // File ini sengaja dikosongkan. Kode asli ada di blok #if false di bawah.
}

#if false
// --- SEMUA KODE DI BAWAH INI DINONAKTIFKAN (FASE 3 LOBOTOMI) ---

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using CoreCAD.Core.Services;
using CoreCAD.Core.Transactions;
using CoreCAD.Core.Registry;
using CoreCAD.Core.Geometry;
using System;
using System.Linq;

namespace CoreCAD.Modules.Architecture
{
    public class Commands_LEGACY
    {
        [CommandMethod("CC_WALL", CommandFlags.Modal)]
        public void CreateSmartWall()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Editor ed = doc.Editor;
            Database db = doc.Database;

            TransactionHelper.ExecuteAtomic((tr, currDb, currEd) =>
            {
                LayerService.EnsureLayer(currDb, tr, LayerService.CenterlineLayer, LayerService.ColorCenterline, "CENTER");
                LayerService.EnsureLayer(currDb, tr, LayerService.WallLayer, LayerService.ColorWall);
                LayerService.EnsureLayer(currDb, tr, LayerService.WallHatchLayer, LayerService.ColorWallHatch);
            }, "Standard Architectural Layers Initialized.");

            PromptPointOptions ppo1 = new PromptPointOptions("\nSpecify wall start point: ");
            PromptPointResult ppr1 = ed.GetPoint(ppo1);
            if (ppr1.Status != PromptStatus.OK) return;

            PromptPointOptions ppo2 = new PromptPointOptions("\nSpecify wall end point: ")
            {
                BasePoint = ppr1.Value,
                UseBasePoint = true,
                UseDashedLine = true
            };
            PromptPointResult ppr2 = ed.GetPoint(ppo2);
            if (ppr2.Status != PromptStatus.OK) return;

            SmartWall wall = new SmartWall
            {
                Thickness = JsonService.GetDefaultThickness(),
                Height = JsonService.GetDefaultHeight(),
                MaterialId = JsonService.GetDefaultMaterial(),
                LevelId = JsonService.GetCurrentLevel(),
                StartPoint = ppr1.Value,
                EndPoint = ppr2.Value,
                PseudoZ = ppr1.Value.Z
            };

            TransactionHelper.ExecuteAtomic((tr, currDb, currEd) =>
            {
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(currDb.CurrentSpaceId, OpenMode.ForWrite);

                Line line = new Line(wall.StartPoint, wall.EndPoint);
                line.SetDatabaseDefaults();
                line.Layer = LayerService.CenterlineLayer;
                btr.AppendEntity(line);
                tr.AddNewlyCreatedDBObject(line, true);

                wall.Role = "MASTER";
                wall.SaveToXData(line, tr);

                Point3dCollection pts = wall.GetVertices();
                if (pts.Count >= 4)
                {
                    Polyline pl = WallGeometryService.CreateWallBoundary(pts, LayerService.WallLayer);
                    btr.AppendEntity(pl);
                    tr.AddNewlyCreatedDBObject(pl, true);

                    wall.Role = "FOLLOWER";
                    wall.SaveToXData(pl, tr);

                    Hatch hatch = WallGeometryService.CreateWallHatch(pl, LayerService.WallHatchLayer);
                    btr.AppendEntity(hatch);
                    tr.AddNewlyCreatedDBObject(hatch, true);

                    hatch.Associative = true;
                    hatch.AppendLoop(HatchLoopTypes.Default, new ObjectIdCollection { pl.ObjectId });
                    hatch.EvaluateHatch(true);

                    wall.Role = "FOLLOWER";
                    wall.SaveToXData(hatch, tr);

                    DrawOrderTable dot = (DrawOrderTable)tr.GetObject(btr.DrawOrderTableId, OpenMode.ForWrite);
                    dot.MoveToBottom(new ObjectIdCollection { hatch.ObjectId });

                    XDataManager.LinkChildren(line, new ObjectId[] { pl.ObjectId, hatch.ObjectId }, tr);
                }
            }, "SmartWall V5 created.");

            ed.Regen();
        }

        [CommandMethod("CC_CENTERLINE", CommandFlags.Modal)]
        public void CreateCenterline()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;

            PromptPointOptions ppo1 = new PromptPointOptions("\nSpecify centerline start point: ");
            PromptPointResult ppr1 = ed.GetPoint(ppo1);
            if (ppr1.Status != PromptStatus.OK) return;

            PromptPointOptions ppo2 = new PromptPointOptions("\nSpecify next point: ")
            {
                BasePoint = ppr1.Value,
                UseBasePoint = true
            };
            PromptPointResult ppr2 = ed.GetPoint(ppo2);
            if (ppr2.Status != PromptStatus.OK) return;

            TransactionHelper.ExecuteAtomic((tr, currDb, currEd) =>
            {
                LayerService.EnsureLayer(currDb, tr, LayerService.CenterlineLayer, LayerService.ColorCenterline, "CENTER");
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(currDb.CurrentSpaceId, OpenMode.ForWrite);

                Line line = new Line(ppr1.Value, ppr2.Value);
                line.SetDatabaseDefaults();
                line.Layer = LayerService.CenterlineLayer;

                btr.AppendEntity(line);
                tr.AddNewlyCreatedDBObject(line, true);
            }, "Centerline created.");

            ed.Regen();
        }
    }
}

#endif
