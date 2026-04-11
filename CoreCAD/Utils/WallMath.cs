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
        /// </summary>
        public static Point2d? GetLineIntersection(Point2d p, Vector2d d1, Point2d q, Vector2d d2)
        {
            double cross = d1.X * d2.Y - d1.Y * d2.X;
            if (Math.Abs(cross) < 1e-10) return null;

            double dx = q.X - p.X;
            double dy = q.Y - p.Y;
            double t = (dx * d2.Y - dy * d2.X) / cross;

            return new Point2d(p.X + t * d1.X, p.Y + t * d1.Y);
        }

        /// <summary>
        /// [ANTI-KELIPET] Validasi apakah titik miter aman (tidak melompati badan dinding).
        /// Titik miter diperbolehkan berada di "Luar" junction (Dot Product < 0) — ini normal.
        /// Titik ditolak jika masuk terlalu dalam ke arah badan dinding (Dot Product > 0 yang besar).
        /// </summary>
        public static bool ValidateMiterPoint(Point2d junction, Point2d centerOther, Point2d pInt)
        {
            Vector2d vBody = centerOther - junction;
            Vector2d vInt  = pInt - junction;

            double bodyLenSq = vBody.X * vBody.X + vBody.Y * vBody.Y;
            if (bodyLenSq < 1e-6) return false;

            // Dot product (proyeksi int ke arah body)
            double dot = (vBody.X * vInt.X + vBody.Y * vInt.Y) / bodyLenSq;

            // Jika dot > 0.1, artinya titik miter sudah masuk 10% ke dalam badan dinding.
            // Untuk persimpangan normal, miter tidak boleh masuk sedalam itu.
            // Jika dot < 0, artinya titik memanjang ke luar junction (ini benar).
            return dot < 0.1; 
        }

        /// <summary>
        /// Mengecek apakah sebuah poligon 4 titik (V0-V1-V2-V3) adalah poligon sederhana
        /// (tidak melilit/self-intersecting). Untuk segiempat, cukup cek apakah sisi berlawanan berpotongan.
        /// </summary>
        public static bool IsSimplePolygon(Point2d v0, Point2d v1, Point2d v2, Point2d v3)
        {
            // Sisi berlawanan: (V0-V1) vs (V2-V3) dan (V1-V2) vs (V3-V0)
            if (DoSegmentsIntersect(v0, v1, v2, v3)) return false;
            if (DoSegmentsIntersect(v1, v2, v3, v0)) return false;
            return true;
        }

        /// <summary>
        /// [ROBUST] Hitung vektor pembagi sudut (Bisector) dari dua vektor arah.
        /// </summary>
        public static Vector2d? CalculateBisector(Vector2d u1, Vector2d u2)
        {
            // Sum of two unit vectors bisects them
            Vector2d sum = u1 + u2;
            if (sum.Length < 1e-6) return null; // Collinear opposite
            return sum.GetNormal();
        }

        /// <summary>
        /// Helper: Apakah segmen garis AB memotong segmen CD?
        /// </summary>
        private static bool DoSegmentsIntersect(Point2d a, Point2d b, Point2d c, Point2d d)
        {
            double det = (b.X - a.X) * (d.Y - c.Y) - (b.Y - a.Y) * (d.X - c.X);
            if (Math.Abs(det) < 1e-10) return false; // Sejajar

            double t = ((c.X - a.X) * (d.Y - c.Y) - (c.Y - a.Y) * (d.X - c.X)) / det;
            double u = ((c.X - a.X) * (b.Y - a.Y) - (c.Y - a.Y) * (b.X - a.X)) / det;

            double eps = 1e-4;
            return (t > eps && t < 1.0 - eps && u > eps && u < 1.0 - eps);
        }

        /// <summary>
        /// Mengecek apakah titik P berada di atas segmen garis S->E (di tengah, bukan di ujung).
        /// Digunakan untuk deteksi T-Junction.
        /// </summary>
        public static bool IsPointOnSegment(Point2d p, Point2d s, Point2d e)
        {
            Vector2d vSeg = e - s;
            Vector2d vP = p - s;

            double lenSq = (vSeg.X * vSeg.X + vSeg.Y * vSeg.Y);
            if (lenSq < 1e-6) return false;

            // Dot product untuk mencari parameter T (0 to 1)
            double t = (vSeg.X * vP.X + vSeg.Y * vP.Y) / lenSq;

            // Berikan margin kecil (epsilon) agar tidak bertabrakan dengan SharedNode (ujung-ujung)
            const double eps = 0.01; 
            if (t < eps || t > 1.0 - eps) return false;

            // Cek jarak tegak lurus (perpendicular distance)
            Point2d pOnLine = new Point2d(s.X + t * vSeg.X, s.Y + t * vSeg.Y);
            double distSq = (p.X - pOnLine.X) * (p.X - pOnLine.X) + (p.Y - pOnLine.Y) * (p.Y - pOnLine.Y);

            return distSq < (NODE_TOLERANCE * NODE_TOLERANCE);
        }
    }
}
