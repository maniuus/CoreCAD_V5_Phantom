using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using CoreCAD.Models;

namespace CoreCAD.Core
{
    /// <summary>
    /// The CoreCAD Geometry & 2D Engine.
    /// Responsible for bidirectional geometric mapping between AutoCAD and JSON.
    /// </summary>
    public static class GeometryEngine
    {
        public static GeometryData ExtractEntityGeometry(Entity ent, string roleId)
        {
            var geo = new GeometryData();

            // 1. Basic Location and Rotation
            if (ent is BlockReference br)
            {
                geo.LocalX = br.Position.X;
                geo.LocalY = br.Position.Y;
                geo.Rotation = br.Rotation;
            }
            else if (ent is Line line)
            {
                geo.LocalX = line.StartPoint.X;
                geo.LocalY = line.StartPoint.Y;
                geo.Rotation = line.Angle;
                geo.Width = line.Length; // For walls, Length = Width in JSON
            }
            else if (ent is Polyline pl)
            {
                if (pl.NumberOfVertices == 4 && (roleId == ArchitectureRoles.WallExt || roleId == ArchitectureRoles.WallInt))
                {
                    Point2d v0 = pl.GetPoint2dAt(0);
                    Point2d v1 = pl.GetPoint2dAt(1);
                    Point2d v2 = pl.GetPoint2dAt(2);

                    double d01 = v0.GetDistanceTo(v1);
                    double d12 = v1.GetDistanceTo(v2);

                    // We assume the longer edge is 'Width' (w) per the user's Reconstruction logic
                    // and its angle is the 'Rotation'.
                    if (d01 >= d12)
                    {
                        geo.LocalX = v0.X;
                        geo.LocalY = v0.Y;
                        geo.Width = d01;
                        geo.Height = d12;
                        geo.Rotation = (v1 - v0).Angle;
                    }
                    else
                    {
                        // If v0-v1 was the thickness, then v1 is the origin for the length direction
                        geo.LocalX = v0.X;
                        geo.LocalY = v0.Y;
                        geo.Width = d01;
                        geo.Height = d12;
                        geo.Rotation = (v1 - v0).Angle;
                        // Actually, to stay consistent with Reconstruction: p2 = p1 + vW*w
                        // If we want the long edge to be w, we'll use d01 as w if it's the direction we want.
                    }
                }
                else
                {
                    var bounds = pl.Bounds;
                    if (bounds.HasValue)
                    {
                        geo.LocalX = bounds.Value.MinPoint.X;
                        geo.LocalY = bounds.Value.MinPoint.Y;
                        geo.Width = bounds.Value.MaxPoint.X - bounds.Value.MinPoint.X;
                        geo.Height = bounds.Value.MaxPoint.Y - bounds.Value.MinPoint.Y;
                        geo.Rotation = 0; // Default for non-wall polylines
                    }
                }
            }

            // 2. Role-Specific Logic (Refinement)
            switch (roleId)
            {
                case ArchitectureRoles.WallExt:
                case ArchitectureRoles.WallInt:
                    // For walls, we might need to extract 'Thickness' from Layer Name or Attributes
                    // For now, we assume the user already set it in Attributes
                    break;

                case MefpRoles.PipeCl:
                case MefpRoles.DuctCl:
                    if (ent is Line pipeLine)
                    {
                        // In MEP, Length is a critical parameter
                        geo.Width = pipeLine.Length;
                    }
                    break;
            }

            return geo;
        }

        /// <summary>
        /// Calculates vertical offset (Delta Z) based on horizontal length and slope.
        /// Formula: ΔZ = L * (s/100)
        /// </summary>
        public static double CalculateSlopeDeltaZ(double horizontalLength, double slopePct)
        {
            return horizontalLength * (slopePct / 100.0);
        }

        /// <summary>
        /// Projects a point from 2.5D space (X, Y, Z_real) to Section View (HorizontalDist, Z_real).
        /// </summary>
        public static Point3d ProjectToSection(Point3d worldPoint, Point3d sectionStart)
        {
            double dx = worldPoint.X - sectionStart.X;
            double dy = worldPoint.Y - sectionStart.Y;
            double horizontalDist = Math.Sqrt(dx * dx + dy * dy);
            
            return new Point3d(horizontalDist, worldPoint.Z, 0);
        }
        /// <summary>
        /// 2. APPLY: Mengubah geometri objek CAD berdasarkan data JSON (Reconstruction Logic).
        /// </summary>
        public static void ApplyEntityGeometry(Entity ent, GeometryData geo, string roleId, string? labelText = null)
        {
            if (ent == null || geo == null) return;

            if (ent is BlockReference br)
            {
                br.Position = new Point3d(geo.LocalX, geo.LocalY, 0);
                br.Rotation = geo.Rotation;
            }
            else if (ent is Line line)
            {
                Point3d start = new Point3d(geo.LocalX, geo.LocalY, 0);
                Vector3d dir = new Vector3d(Math.Cos(geo.Rotation), Math.Sin(geo.Rotation), 0);
                line.StartPoint = start;
                line.EndPoint = start + (dir * geo.Width);
            }
            else if (ent is Polyline pl)
            {
                // LOGIKA ANTI-MENCENG (RECONSTRUCTION LOGIC)
                if (pl.NumberOfVertices == 4 && (roleId == ArchitectureRoles.WallExt || roleId == ArchitectureRoles.WallInt))
                {
                    ReconstructPolyline(pl, geo);
                }
                else
                {
                    var bounds = pl.Bounds;
                    if (bounds.HasValue)
                    {
                        Vector3d moveVec = new Point3d(geo.LocalX, geo.LocalY, 0) - bounds.Value.MinPoint;
                        pl.TransformBy(Matrix3d.Displacement(moveVec));
                    }
                }
            }
            else if (ent is DBText txt)
            {
                // Prioritaskan labelText dari Smart Resolver jika tersedia
                if (labelText != null)
                {
                    txt.TextString = labelText;
                }
                else if (roleId == "label_elev")
                {
                    txt.TextString = "EL +" + geo.LocalZ.ToString("N2");
                }
                txt.Position = new Point3d(geo.LocalX, geo.LocalY, 0);
                txt.Rotation = geo.Rotation;
            }
            else if (ent is MText mtxt)
            {
                // Prioritaskan labelText dari Smart Resolver jika tersedia
                if (labelText != null)
                {
                    mtxt.Contents = labelText;
                }
                else if (roleId == "label_elev")
                {
                    mtxt.Contents = "EL +" + geo.LocalZ.ToString("N2");
                }
                mtxt.Location = new Point3d(geo.LocalX, geo.LocalY, 0);
                mtxt.Rotation = geo.Rotation;
            }
        }

        private static void ReconstructPolyline(Polyline pl, GeometryData geo)
        {
            // 1. Ambil Parameter dari SSOT (JSON)
            double w = geo.Width;
            double h = geo.Height;
            double rot = geo.Rotation; // Sudut dalam Radian
            Point2d origin = new Point2d(geo.LocalX, geo.LocalY);

            // 2. Hitung Vektor Arah (Unit Vectors)
            // Vektor Width (vW) mengikuti rotasi
            Vector2d vW = new Vector2d(Math.Cos(rot), Math.Sin(rot));
            // Vektor Height (vH) WAJIB tegak lurus vW agar tidak menceng
            Vector2d vH = vW.GetPerpendicularVector();

            // 3. Kalkulasi 4 Titik Sudut secara Absolut
            Point2d p1 = origin;                       // Bottom-Left
            Point2d p2 = origin + (vW * w);            // Bottom-Right
            Point2d p3 = p2 + (vH * h);                // Top-Right
            Point2d p4 = origin + (vH * h);            // Top-Left

            // 4. Inject ke AutoCAD (Direct Assignment)
            pl.SetPointAt(0, p1);
            pl.SetPointAt(1, p2);
            pl.SetPointAt(2, p3);
            pl.SetPointAt(3, p4);

            pl.Closed = true;
        }

        /// <summary>
        /// BRICK 5: GENERATIVE DRAFTING
        /// Factory method to create base AutoCAD entities based on the Role_ID defined in JSON.
        /// </summary>
        public static Entity? CreateNewEntityFromRole(string roleId)
        {
            if (string.IsNullOrEmpty(roleId)) return null;

            // Simplified: Determine base shape based on role string clues
            string lowerRole = roleId.ToLower();
            
            if (lowerRole.Contains("wall") || lowerRole.Contains("rect") || lowerRole.Contains("frame"))
            {
                Polyline pl = new Polyline(4);
                // Initialize with 4 vertices so it's a valid target for ReconstructPolyline later
                for (int i = 0; i < 4; i++)
                {
                    pl.AddVertexAt(i, new Point2d(0, 0), 0, 0, 0);
                }
                pl.Closed = true;
                return pl;
            }
            else if (lowerRole.Contains("pipe") || lowerRole.Contains("line") || lowerRole.Contains("cl"))
            {
                // Return a simple unit line that will be correctly scaled by ApplyEntityGeometry
                return new Line(Point3d.Origin, new Point3d(1, 0, 0));
            }
            else if (lowerRole.Contains("label") || lowerRole.Contains("text"))
            {
                return new DBText { TextString = "EL +0.00", Height = 2.5 };
            }

            return null; // For roles that don't have a default geometric body yet
        }
    }
}
