using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using CoreCAD.Core.Base;
using CoreCAD.Core.Registry;
using System;

namespace CoreCAD.Modules.Architecture
{
    public class SmartWall : CoreCADEntity
    {
        public double Thickness { get; set; } = 150.0;
        public double Height { get; set; } = 3000.0;
        public Point3d StartPoint { get; set; }
        public Point3d EndPoint { get; set; }
        public string Role { get; set; } = "MASTER";

        public double Length => StartPoint.DistanceTo(EndPoint);

        public SmartWall()
        {
            CoreCAD_ID = Guid.NewGuid();
        }

        public Point3dCollection GetVertices(bool includeMiter, Line? parent = null)
        {
            if (Length < 0.1) return new Point3dCollection();

            Vector3d dir = (EndPoint - StartPoint).GetNormal();
            Vector3d normal = new Vector3d(-dir.Y, dir.X, 0) * (Thickness / 2.0);

            Point3d p1 = StartPoint + normal;
            Point3d p4 = StartPoint - normal;
            Point3d p2 = EndPoint + normal;
            Point3d p3 = EndPoint - normal;

            return new Point3dCollection { p1, p2, p3, p4 };
        }

        public override double GetVolume() => (Length * Thickness * Height) / 1_000_000_000.0;
        public override void SyncFromJSON() { }

        public override void SaveToXData(Entity ent, Transaction tr)
        {
            XDataManager.EnsureRegApp(ent.Database, tr);
            XDataManager.SetIdentity(ent, CoreCAD_ID, MaterialId, LevelId, PseudoZ, Role);
        }

        public override void LoadFromXData(Entity ent)
        {
            var identity = XDataManager.GetIdentity(ent);
            if (identity != null)
            {
                CoreCAD_ID = identity.Value.guid;
                MaterialId = identity.Value.materialId;
                LevelId = identity.Value.levelId;
                PseudoZ = identity.Value.pseudoZ;
                Role = identity.Value.role;
            }
        }
    }
}
