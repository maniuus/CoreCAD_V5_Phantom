using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using CoreCAD.Core.Services;
using CoreCAD.Modules.Architecture;
using System;
using System.Collections.Generic;

namespace CoreCAD.Core.Geometry
{
    public static class WallGeometryService
    {
        public static Point3dCollection CalculateWallVertices(Line line, double thickness)
        {
            if (line == null) return new Point3dCollection();

            // Instantiate a transient SmartWall model for calculation
            SmartWall wall = new SmartWall
            {
                StartPoint = line.StartPoint,
                EndPoint = line.EndPoint,
                Thickness = thickness
            };

            // Delegates to SmartWall model for pure rectangular baseline (no miter — miter is applied by BooleanUnionEngine)
            return wall.GetVertices();
        }

        public static Polyline CreateWallBoundary(Point3dCollection pts, string layer)
        {
            Polyline pl = new Polyline();
            for (int i = 0; i < pts.Count; i++)
            {
                pl.AddVertexAt(i, new Point2d(pts[i].X, pts[i].Y), 0, 0, 0);
            }
            pl.Closed = true;
            pl.Layer = layer;
            
            // Set Elevation to match the first point's Z
            if (pts.Count > 0)
            {
                pl.Elevation = pts[0].Z;
            }
            pl.Normal = Vector3d.ZAxis;
            
            return pl;
        }

        public static Hatch CreateWallHatch(Polyline boundary, string layer)
        {
            Hatch hatch = new Hatch();
            hatch.SetDatabaseDefaults();
            hatch.Layer = layer;
            hatch.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
            return hatch;
        }
    }
}
