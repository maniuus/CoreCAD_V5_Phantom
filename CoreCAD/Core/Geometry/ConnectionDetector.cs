using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using CoreCAD.Core.Registry;
using System.Collections.Generic;

namespace CoreCAD.Core.Geometry
{
    /// <summary>
    /// BLUEPRINT STEP 2: Proximity identification for Junctions.
    /// Targeted at sub-50mm endpoint detection.
    /// </summary>
    public static class ConnectionDetector
    {
        public static List<Line> FindNeighborsAtPoint(Line target, Point3d junctionPoint, Transaction? tr = null, double tolerance = 100.0)
        {
            List<Line> neighbors = new List<Line>();
            Database db = target.Database;
            if (db == null) return neighbors;

            bool isLocalTr = (tr == null);
            Transaction actualTr = tr ?? db.TransactionManager.StartOpenCloseTransaction();

            try
            {
                BlockTableRecord btr = (BlockTableRecord)actualTr.GetObject(target.OwnerId, OpenMode.ForRead);
                if (btr == null) return neighbors;

                foreach (ObjectId id in btr)
                {
                    if (id == target.ObjectId) continue;
                    if (id.ObjectClass.IsDerivedFrom(RXClass.GetClass(typeof(Line))))
                    {
                        Line neighbor = (Line)actualTr.GetObject(id, OpenMode.ForRead);
                        if (neighbor.StartPoint.DistanceTo(junctionPoint) < tolerance || 
                            neighbor.EndPoint.DistanceTo(junctionPoint) < tolerance)
                        {
                            neighbors.Add(neighbor);
                        }
                    }
                }
            }
            finally
            {
                if (isLocalTr) actualTr.Dispose();
            }
            
            return neighbors;
        }
    }
}
