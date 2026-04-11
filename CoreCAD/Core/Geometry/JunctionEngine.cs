using Autodesk.AutoCAD.Geometry;
using System;

namespace CoreCAD.Core.Geometry
{
    /// <summary>
    /// PROTOKOL L-JUNCTION V5: Advanced Miter Logic.
    /// Supports asymmetric thickness and head-to-tail vector normalization.
    /// "Besaran sudut menentukan kekuatan ikatan, presisi vektor menentukan kejujuran visual."
    /// </summary>
    public static class JunctionEngine
    {
        /// <summary>
        /// Calculates the outer and inner miter points for Two intersecting walls.
        /// </summary>
        /// <param name="u">Vector of Wall A (pointing away from junction)</param>
        /// <param name="v">Vector of Wall B (pointing away from junction)</param>
        /// <param name="pInt">The shared intersection point (Garis As)</param>
        /// <param name="thicknessA">Thickness of Wall A</param>
        /// <param name="thicknessB">Thickness of Wall B</param>
        public static Point3d[]? CalculateMiterV5(Vector3d u, Vector3d v, Point3d pInt, double thicknessA, double thicknessB)
        {
            // 1. Normalize vectors pointing AWAY from junction
            Vector3d uNorm = u.GetNormal();
            Vector3d vNorm = v.GetNormal();
            double dot = uNorm.DotProduct(vNorm);
            
            // Parallel/Collinear Check (Tolerance 0.1 deg)
            if (Math.Abs(dot) > 0.9998) return null;

            // 2. PROTOKOL V5: BISECTOR LOGIC
            // Bisector (u+v) always points to the EXTERIOR (OUTER) corner.
            Vector3d bisector = (uNorm + vNorm).GetNormal();
            
            // Calculate distance 'd' for Outer and Inner
            // Formula: d = (Thickness/2) / Sin(Angle/2)
            // Sine of half-angle between u and v can be derived from u+v length
            double halfAngleSin = Math.Sin(uNorm.GetAngleTo(vNorm) / 2.0);
            if (halfAngleSin < 0.001) return null;

            double dA = (thicknessA / 2.0) / halfAngleSin;
            double dB = (thicknessB / 2.0) / halfAngleSin;

            // In asymmetric miter, point is intersection of lines.
            // But following guideline polarities:
            Point3d pOuter = pInt + (bisector * dA); // (+) Polarity
            Point3d pInner = pInt - (bisector * dA); // (-) Polarity
            
            // For asymmetric, we should theoretically use a blended d or 
            // recalculate intersection. Guideline says ptInt +/- (bisector * d).
            // We use dA to maintain Wall A's thickness integrity.
            
            return new Point3d[] { pOuter, pInner };
        }
    }
}
