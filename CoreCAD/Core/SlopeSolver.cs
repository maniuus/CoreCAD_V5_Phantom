using System;
using System.Collections.Generic;
using System.Linq;
using CoreCAD.Models;

namespace CoreCAD.Core
{
    public static class SlopeSolver
    {
        /// <summary>
        /// Calculates the end elevation based on start elevation, length, and slope.
        /// Formula: Z_End = Z_Start + (L * (s / 100))
        /// </summary>
        public static double CalculateEndZ(double zStart, double length, double slopePct)
        {
            return zStart + (length * (slopePct / 100.0));
        }

        /// <summary>
        /// Calculates the slope percentage based on start/end elevation and horizontal length.
        /// Formula: s = ((Z_End - Z_Start) / L) * 100
        /// </summary>
        public static double CalculateSlope(double zStart, double zEnd, double length)
        {
            if (length < 0.0001) return 0;
            return ((zEnd - zStart) / length) * 100.0;
        }

        /// <summary>
        /// Propagates elevation changes through a connected chain of entities in the JSON model.
        /// </summary>
        public static void PropagateChain(List<CoreEntity> entities, string startGuid)
        {
            var entityMap = entities.ToDictionary(e => e.Guid);
            if (!entityMap.TryGetValue(startGuid, out var current)) return;

            var processed = new HashSet<string>();
            var queue = new Queue<CoreEntity>();
            queue.Enqueue(current);

            while (queue.Count > 0)
            {
                var parent = queue.Dequeue();
                processed.Add(parent.Guid);

                // Calculate the EndZ of the current entity
                // Note: Width in our mapping for Pipes represents Length
                double endZ = CalculateEndZ(parent.Geometry.LocalZ, parent.Geometry.Width, parent.Geometry.SlopePercentage);

                // Find children (entities connected to this one)
                // We use ParentID as the 'Connected From' indicator
                var children = entities.Where(e => e.ParentId == parent.Guid && !processed.Contains(e.Guid)).ToList();

                foreach (var child in children)
                {
                    // Propagate: Child StartZ must match Parent EndZ
                    child.Geometry.LocalZ = endZ;
                    
                    queue.Enqueue(child);
                }
            }
        }
    }
}
