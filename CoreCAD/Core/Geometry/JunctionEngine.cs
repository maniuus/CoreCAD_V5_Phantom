using Autodesk.AutoCAD.Geometry;
using System;

namespace CoreCAD.Core.Geometry
{
    /// <summary>
    /// PROTOKOL L-JUNCTION: Pure Vector Bisector (Tail-to-Tail).
    /// "Vektor tidak pernah bohong, hanya kita yang sering salah arah."
    /// </summary>
    public static class JunctionEngine
    {
        public static Point3d[]? CalculateMiterPoints(Vector3d u, Vector3d v, Point3d pInt, double thickness)
        {
            // 1. Hitung Sudut (shortest angle)
            double theta = u.GetAngleTo(v);
            
            // 2. Antisipasi Sudut Paralel (Degenerate Guard)
            if (theta < 0.087 || theta > 3.05) return null;

            // 3. LOGIKA PENJUMLAHAN (BISECTOR)
            // Vektor b adalah normalisasi dari (u + v)
            Vector3d b = (u.GetNormal() + v.GetNormal()).GetNormal();
            if (b.Length < 0.001) return null;

            // 4. PENENTUAN JARAK OFFSET
            // Jarak Offset (d) = (Thickness / 2) / Sin(theta / 2)
            double halfTheta = theta / 2.0;
            double sinHalf = Math.Sin(halfTheta);
            if (Math.Abs(sinHalf) < 0.001) return null;

            double d = (thickness / 2.0) / sinHalf;

            // 5. TITIK LUAR & DALAM
            Point3d pLuar = pInt + (b * d);
            Point3d pDalam = pInt - (b * d);

            return new Point3d[] { pLuar, pDalam };
        }
    }
}
