using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using CoreCAD.Core.Registry;
using System.Collections.Generic;

namespace CoreCAD.Core.Geometry
{
    public class JointManager
    {
        public struct JunctionResult
        {
            public bool hasJunction;
            public Point3d pLuar;
            public Point3d pDalam;
            public bool atStart;
            public ObjectId neighborId;
        }

        public static List<JunctionResult> GetJunctions(Line target, Transaction tr)
        {
            List<JunctionResult> results = new List<JunctionResult>();

            // 1. Check Start Point
            var resStart = CheckAtPoint(target, target.StartPoint, tr, true);
            if (resStart.hasJunction) results.Add(resStart);

            // 2. Check End Point
            var resEnd = CheckAtPoint(target, target.EndPoint, tr, false);
            if (resEnd.hasJunction) results.Add(resEnd);

            return results;
        }

        private static JunctionResult CheckAtPoint(Line target, Point3d pt, Transaction tr, bool atStart)
        {
            JunctionResult result = new JunctionResult { hasJunction = false, atStart = atStart };

            Line? neighbor = ConnectionDetector.FindNeighborsAtPoint(target, pt, tr, 50.0).FirstOrDefault();
            if (neighbor == null) return result;

            var idA = XDataManager.GetIdentity(target);
            var idB = XDataManager.GetIdentity(neighbor);
            if (idA == null || idB == null) return result;

            // Normalize vectors: Always pointing AWAY from the junction point
            Vector3d u = atStart ? (target.EndPoint - target.StartPoint) : (target.StartPoint - target.EndPoint);
            
            bool neighborAtStart = neighbor.StartPoint.DistanceTo(pt) < 50.0;
            Vector3d v = neighborAtStart ? (neighbor.EndPoint - neighbor.StartPoint) : (neighbor.StartPoint - neighbor.EndPoint);

            Point3d[]? miter = JunctionEngine.CalculateMiterV5(u, v, pt, idA.Value.thickness, idB.Value.thickness);
            if (miter != null)
            {
                result.hasJunction = true;
                result.pLuar = miter[0]; // EXTERIOR (+)
                result.pDalam = miter[1]; // INTERIOR (-)
                result.neighborId = neighbor.ObjectId;
            }

            return result;
        }
    }
}
