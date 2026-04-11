using Autodesk.AutoCAD.Geometry;
using System;

namespace CoreCAD.Utils
{
    /// <summary>
    /// [V5 MATH ENGINE] Matematika murni untuk kalkulasi Miter Joint.
    /// Tidak ada referensi AutoCAD DB di sini — hanya angka dan vektor.
    /// Algoritma berbasis: parametric line intersection (P + t*D = Q + s*E).
    /// </summary>
    public static class WallMath
    {
        /// <summary>
        /// Toleransi deteksi "Shared Node" (Titik Bersama) dalam mm.
        /// Dua endpoint dianggap menempel jika jaraknya < 1mm.
        /// </summary>
        public const double NODE_TOLERANCE = 1.0;

        /// <summary>
        /// Cek apakah dua titik 2D adalah "Shared Node" (bertetangga).
        /// </summary>
        public static bool IsSharedNode(Point2d a, Point2d b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return (dx * dx + dy * dy) < (NODE_TOLERANCE * NODE_TOLERANCE);
        }

        /// <summary>
        /// Hitung titik perpotongan dua garis parametrik (Line Intersection).
        ///
        /// L1: P + t * D1
        /// L2: Q + s * D2
        ///
        /// Diselesaikan dengan Cross Product:
        ///   cross = D1 x D2  (determinant 2x2)
        ///   t = (Q-P) x D2 / cross
        ///   result = P + t * D1
        ///
        /// Return null jika garis sejajar (cross ≈ 0).
        /// </summary>
        /// <param name="p">Titik asal garis 1</param>
        /// <param name="d1">Arah (direction, tidak harus normalize) garis 1</param>
        /// <param name="q">Titik asal garis 2</param>
        /// <param name="d2">Arah garis 2</param>
        public static Point2d? GetLineIntersection(Point2d p, Vector2d d1, Point2d q, Vector2d d2)
        {
            // Cross product dari dua vektor 2D (determinan)
            double cross = d1.X * d2.Y - d1.Y * d2.X;

            // Jika mendekati nol → garis sejajar atau berimpit → tidak ada titik temu unik
            if (Math.Abs(cross) < 1e-10) return null;

            // Delta vektor dari P ke Q
            double dx = q.X - p.X;
            double dy = q.Y - p.Y;

            // Parameter t: seberapa jauh di sepanjang L1
            double t = (dx * d2.Y - dy * d2.X) / cross;

            // Titik perpotongan
            return new Point2d(p.X + t * d1.X, p.Y + t * d1.Y);
        }
    }
}
