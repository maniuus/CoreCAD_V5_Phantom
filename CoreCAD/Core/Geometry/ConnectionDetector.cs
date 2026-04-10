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
        public static Line? FindNeighborAtPoint(Line target, Point3d junctionPoint, double tolerance = 50.0)
        {
            Database db = target.Database;
            // Protective check: WorldDraw can call this on non-database entities (JIGs)
            if (db == null) return null;

            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                // Fix: Using target.OwnerId instead of db.CurrentSpaceId for stability during reactors
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(target.OwnerId, OpenMode.ForRead);
                if (btr == null) return null;

                foreach (ObjectId id in btr)
                {
                    if (id == target.ObjectId) continue;

                    // Filter for Line entities
                    if (id.ObjectClass.IsDerivedFrom(RXClass.GetClass(typeof(Line))))
                    {
                        Line neighbor = (Line)tr.GetObject(id, OpenMode.ForRead);
                        
                        // Rule 3: Radius Check (50mm Tolerance)
                        if (neighbor.StartPoint.DistanceTo(junctionPoint) < tolerance || 
                            neighbor.EndPoint.DistanceTo(junctionPoint) < tolerance)
                        {
                            return neighbor; // RETURN FIRST MATCH (Protocol Focus: L-Junction)
                        }
                    }
                }
            }
            return null;
        }
    }
}
