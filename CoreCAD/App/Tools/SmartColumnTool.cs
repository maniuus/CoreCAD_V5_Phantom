using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using CoreCAD.Models;
using System;

namespace CoreCAD.App.Tools
{
    public class SmartColumnTool : AbstractSmartTool
    {
        private Point3d _center;
        private double _width = 400;
        private double _height = 400;
        private string _mark = "K1";

        protected override bool GetUserInput(Editor ed)
        {
            PromptPointOptions ppo = new PromptPointOptions("\nKlik titik pusat kolom: ");
            PromptPointResult ppr = ed.GetPoint(ppo);
            if (ppr.Status != PromptStatus.OK) return false;
            _center = ppr.Value;

            PromptDoubleOptions pdoW = new PromptDoubleOptions("\nMasukkan Lebar (mm): ") { DefaultValue = _width };
            PromptDoubleResult pdrW = ed.GetDouble(pdoW);
            if (pdrW.Status != PromptStatus.OK) return false;
            _width = pdrW.Value;

            PromptDoubleOptions pdoH = new PromptDoubleOptions("\nMasukkan Tinggi (mm): ") { DefaultValue = _height };
            PromptDoubleResult pdrH = ed.GetDouble(pdoH);
            if (pdrH.Status != PromptStatus.OK) return false;
            _height = pdrH.Value;

            PromptStringOptions pso = new PromptStringOptions("\nMasukkan Mark (misal: K1): ") { DefaultValue = _mark };
            PromptResult psr = ed.GetString(pso);
            if (psr.Status == PromptStatus.OK) _mark = psr.StringResult;

            return true;
        }

        protected override Entity DrawVisual(Database db)
        {
            Polyline poly = new Polyline(4);
            double hw = _width / 2.0;
            double hh = _height / 2.0;

            poly.AddVertexAt(0, new Point2d(_center.X - hw, _center.Y - hh), 0, 0, 0);
            poly.AddVertexAt(1, new Point2d(_center.X + hw, _center.Y - hh), 0, 0, 0);
            poly.AddVertexAt(2, new Point2d(_center.X + hw, _center.Y + hh), 0, 0, 0);
            poly.AddVertexAt(3, new Point2d(_center.X - hw, _center.Y + hh), 0, 0, 0);
            poly.Closed = true;
            
            return poly;
        }

        protected override SmartObject CreateSyncData(Entity ent, string guid)
        {
            var obj = new SmartObject
            {
                Guid = guid,
                RoleId = "STRUCTURAL_COLUMN"
            };

            // --- INJECT DNA (Sifat Global) ---
            obj.Dna.Mark = _mark; 
            obj.Dna.Width = _width; 
            obj.Dna.Height = _height;
            obj.Dna.Material = "CONCRETE";

            // --- INJECT INSTANCE (Data Lokal File) ---
            var inst = new InstanceData
            {
                SourceFile = Persistence.CoreJSONEngine.GetValidPath(ent.Database),
                ViewType = "PLAN",
                Geometry = new GeometryData
                {
                    LocalX = _center.X,
                    LocalY = _center.Y,
                    LocalZ = _center.Z,
                    Width = _width,   // Tetap simpan di sini untuk performa visual
                    Height = _height
                }
            };

            obj.Instances.Add(inst);
            return obj;
        }
    }
}
