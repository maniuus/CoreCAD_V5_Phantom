using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.BoundaryRepresentation;
using CoreCAD.Core.Registry;
using System;
using System.Collections.Generic;
using System.Linq;
using CoreCAD.Core.Diagnostics;
using Region = Autodesk.AutoCAD.DatabaseServices.Region;
using Face = Autodesk.AutoCAD.BoundaryRepresentation.Face;

namespace CoreCAD.Core.Geometry
{
    /// <summary>
    /// THE BLENDER: Performs Boolean Union operations on multiple wall skeletons
    /// to create a single, seamless manifold (One Flesh).
    /// </summary>
    public static class BooleanUnionEngine
    {
        public static Polyline? UniteSkeletons(IEnumerable<Line> skeletons, Transaction tr)
        {
            DebugLogger.Log($"Uniting {skeletons.Count()} skeletons.");
            List<Region> regions = new List<Region>();
            
            try
            {
                foreach (Line line in skeletons)
                {
                    Region? reg = CreateWallRegion(line, tr);
                    if (reg != null) regions.Add(reg);
                }

                if (regions.Count == 0) return null;
                if (regions.Count == 1)
                {
                    Polyline? solo = RegionToPolyline(regions[0]);
                    regions[0].Dispose();
                    regions.Clear();
                    return solo;
                }

                // 1. BOOLEAN UNION: consume non-master regions into master
                Region master = regions[0];
                for (int i = 1; i < regions.Count; i++)
                {
                    master.BooleanOperation(BooleanOperationType.BoolUnite, regions[i]);
                    regions[i].Dispose(); // Dispose immediately after consumption
                    regions[i] = null!;   // Mark as disposed for finally guard
                }

                DebugLogger.Log("Boolean Union successful.");

                // 2. EXTRACT BOUNDARY — master is still alive here
                Polyline? result = RegionToPolyline(master);
                master.Dispose(); // Dispose AFTER polyline is extracted
                regions.Clear();  // Clear so finally block has nothing to double-dispose
                return result;
            }
            catch (System.Exception ex)
            {
                DebugLogger.Error("UniteSkeletons error", ex);
                return null;
            }
            finally
            {
                // Exception-path guard: dispose any regions not yet released
                foreach (var reg in regions)
                    if (reg != null && !reg.IsDisposed) reg.Dispose();
            }

        }

        private static Region? CreateWallRegion(Line line, Transaction tr)
        {
            // Calculate the 4 points including miters
            var junctions = JointManager.GetJunctions(line, tr);
            
            // Baseline 
            Vector3d baseline = (line.EndPoint - line.StartPoint).GetNormal();
            double thickness = 150.0;
            var identity = XDataManager.GetIdentity(line);
            if (identity != null) thickness = identity.Value.thickness;

            Vector3d normal = new Vector3d(-baseline.Y, baseline.X, 0) * (thickness / 2.0);

            // Default Rectangular Points
            Point3d[] pts = new Point3d[4];
            pts[0] = line.StartPoint + normal;
            pts[1] = line.EndPoint + normal;
            pts[2] = line.EndPoint - normal;
            pts[3] = line.StartPoint - normal;

            // Apply Miters using V5 Vektor Bisector logic (Simplified for temporary region)
            foreach (var junc in junctions)
            {
                Point3d pLeft, pRight;
                double cp1 = (baseline.X * (junc.pLuar.Y - (junc.atStart ? line.StartPoint.Y : line.EndPoint.Y))) - 
                             (baseline.Y * (junc.pLuar.X - (junc.atStart ? line.StartPoint.X : line.EndPoint.X)));
                
                if (cp1 > 0) { pLeft = junc.pLuar; pRight = junc.pDalam; }
                else { pLeft = junc.pDalam; pRight = junc.pLuar; }

                if (junc.atStart) { pts[0] = pLeft; pts[3] = pRight; }
                else { pts[1] = pLeft; pts[2] = pRight; }
            }

            // Create temporary Polyline
            Polyline pl = new Polyline();
            for (int i = 0; i < 4; i++) pl.AddVertexAt(i, new Point2d(pts[i].X, pts[i].Y), 0, 0, 0);
            pl.Closed = true;

            using (DBObjectCollection curves = new DBObjectCollection { pl })
            {
                DBObjectCollection subRegions = Region.CreateFromCurves(curves);
                if (subRegions.Count > 0)
                {
                    Region res = (Region)subRegions[0];
                    // Dispose others in collection if any
                    for (int i = 1; i < subRegions.Count; i++) subRegions[i].Dispose();
                    return res;
                }
            }
            return null;
        }

        private static Polyline? RegionToPolyline(Region reg)
        {
            try
            {
                using (Brep brep = new Brep(reg))
                {
                    foreach (Face face in brep.Faces)
                    {
                        foreach (BoundaryLoop loop in face.Loops)
                        {
                            if (loop.LoopType == LoopType.LoopExterior)
                            {
                                Polyline pl = new Polyline { Closed = true };
                                int i = 0;
                                foreach (Edge edge in loop.Edges)
                                {
                                    // Extract points from edges to build the polyline
                                    Point3d start = edge.Vertex1.Point;
                                    pl.AddVertexAt(i++, new Point2d(start.X, start.Y), 0, 0, 0);
                                }
                                return pl;
                            }
                        }
                    }
                }
            }
            catch
            {
                // Fallback: Explode if Brep fails
                DBObjectCollection exploded = new DBObjectCollection();
                reg.Explode(exploded);
                // Joining exploded curves into a polyline is complex, 
                // but for simple wall unions, Brep should be stable.
            }
            return null;
        }
    }
}
