using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using CoreCAD.Core.Registry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CoreCAD.Core.Geometry
{
    public static class GroupManager
    {
        /// <summary>
        /// Finds all lines belonging to the same GroupId.
        /// </summary>
        public static List<Line> FindGroupMembers(Database db, string groupId, Transaction tr)
        {
            List<Line> members = new List<Line>();
            if (string.IsNullOrEmpty(groupId)) return members;

            // Scan ONLY current space for efficiency — GroupIds are space-local
            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
            foreach (ObjectId id in btr)
            {
                if (id.IsErased || !id.IsValid) continue;
                if (id.ObjectClass.IsDerivedFrom(RXClass.GetClass(typeof(Line))))
                {
                    Line line = (Line)tr.GetObject(id, OpenMode.ForRead);
                    if (XDataManager.GetGroupId(line) == groupId)
                        members.Add(line);
                }
            }
            return members;
        }

        /// <summary>
        /// Orchestrates Auto-Grouping when a wall node touches another wall node.
        /// Handles multi-junctions (L, T, Cross) and GroupId merging.
        /// </summary>
        public static string EnsureGroupId(Line line, Transaction tr)
        {
            string currentId = XDataManager.GetGroupId(line);
            
            // 1. Gather ALL neighbors from both ends
            var neighbors = ConnectionDetector.FindNeighborsAtPoint(line, line.StartPoint, tr);
            neighbors.AddRange(ConnectionDetector.FindNeighborsAtPoint(line, line.EndPoint, tr));

            // 2. DISOLUTION CHECK: If no neighbors and had a group, clear it if it was the last one
            if (neighbors.Count == 0)
            {
                if (!string.IsNullOrEmpty(currentId))
                {
                    // Check if anyone else is still in this group
                    var others = FindGroupMembers(line.Database, currentId, tr);
                    if (others.Count <= 1)
                    {
                        XDataManager.SetGroupId(line, "");
                        return "";
                    }
                }
                return currentId;
            }

            // 3. MERGE LOGIC: Select the dominant GroupId from neighbors
            string? dominantId = neighbors
                .Select(n => XDataManager.GetGroupId(n))
                .FirstOrDefault(id => !string.IsNullOrEmpty(id));

            if (string.IsNullOrEmpty(dominantId))
            {
                dominantId = string.IsNullOrEmpty(currentId) ? Guid.NewGuid().ToString("D") : currentId;
            }

            // 4. PROPAGATION: Force entire cluster to the winner
            if (XDataManager.GetGroupId(line) != dominantId)
            {
                XDataManager.SetGroupId(line, dominantId);
            }

            foreach (var n in neighbors)
            {
                if (XDataManager.GetGroupId(n) != dominantId)
                {
                    XDataManager.SetGroupId(n, dominantId);
                }
            }
            
            return dominantId;
        }
    }
}
