using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using CoreCAD.Models;
using System;

namespace CoreCAD.App.Tools
{
    public class SmartDoorTool : AbstractSmartTool
    {
        private Point3d _center;
        private double _width = 900;
        private double _height = 2100;
        private double _thickness = 40;
        private double _rotation = 0;
        private bool _isFlipped = false;
        private string _mark = "P1";

        protected override bool GetUserInput(Editor ed)
        {
            // 1. Pilih Titik Pasang
            PromptPointOptions ppo = new PromptPointOptions("\nKlik titik pasang pintu: ");
            PromptPointResult ppr = ed.GetPoint(ppo);
            if (ppr.Status != PromptStatus.OK) return false;
            _center = ppr.Value;

            // 2. Tentukan Arah Buukan (Rotation)
            PromptAngleOptions pao = new PromptAngleOptions("\nTentukan arah bukaan pintu: ") { BasePoint = _center, UseBasePoint = true };
            PromptDoubleResult par = ed.GetAngle(pao);
            if (par.Status != PromptStatus.OK) return false;
            _rotation = par.Value;

            // 3. Opsi Flip
            PromptKeywordOptions pko = new PromptKeywordOptions("\nStatus Pintu [Flip/Normal] <Normal>: ", "Flip Normal");
            pko.AllowNone = true;
            PromptResult pkr = ed.GetKeywords(pko);
            if (pkr.Status == PromptStatus.OK && pkr.StringResult == "Flip")
            {
                _isFlipped = true;
            }

            return true;
        }

        protected override Entity DrawVisual(Database db)
        {
            // Membuat grup visual pintu (Kusen + Daun + Swing)
            Polyline poly = new Polyline();
            
            double w = _width;
            double t = _thickness;
            double flip = _isFlipped ? -1.0 : 1.0;

            // Titik-titik lokal sebelum rotasi
            poly.AddVertexAt(0, new Point2d(0, 0), 0, 0, 0);
            poly.AddVertexAt(1, new Point2d(w, 0), 0, 0, 0);
            poly.AddVertexAt(2, new Point2d(w, t * flip), 0, 0, 0);
            poly.AddVertexAt(3, new Point2d(0, t * flip), 0, 0, 0);
            poly.Closed = true;

            // Transformasi Rotasi & Posisi
            poly.TransformBy(Matrix3d.Rotation(_rotation, Vector3d.ZAxis, Point3d.Origin));
            poly.TransformBy(Matrix3d.Displacement(_center.GetAsVector()));

            return poly;
        }

        protected override SmartObject CreateSyncData(Entity ent, string guid)
        {
            var obj = new SmartObject
            {
                Guid = guid,
                RoleId = "ARCH_DOOR_SINGLE",
                ParentId = ""
            };

            // --- SEKTOR DNA (Sifat Barang - Global) ---
            obj.Dna.Mark = _mark; 
            obj.Dna.Width = _width;
            obj.Dna.Height = _height;
            obj.Dna.LeafThickness = _thickness;
            obj.Dna.Material = "WOOD_PLYWOOD";

            // --- SEKTOR INSTANCE (Lokasi di Gambar - Lokal) ---
            var inst = new InstanceData
            {
                SourceFile = Persistence.CoreJSONEngine.GetValidPath(ent.Database),
                ViewType = "PLAN",
                Geometry = new GeometryData
                {
                    LocalX = _center.X,
                    LocalY = _center.Y,
                    LocalZ = _center.Z,
                    Rotation = _rotation,
                    FlipState = _isFlipped,
                    Width = _width,
                    Height = _height
                }
            };

            obj.Instances.Add(inst);
            return obj;
        }
    }
}
