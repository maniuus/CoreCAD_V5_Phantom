using System;
using Autodesk.AutoCAD.DatabaseServices;

namespace CoreCAD.Core.Base
{
    /// <summary>
    /// Base class for all CoreCAD Managed Entities.
    /// Implements the "One Soul, Many Bodies" philosophy by separating 
    /// physical identity (GUID) from visual CAD geometry.
    /// </summary>
    public abstract class CoreCADEntity
    {
        /// <summary>
        /// Logical Identity (KTP Digital). Unique across all files in the project.
        /// </summary>
        public Guid CoreCAD_ID { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Floor/Level anchor ID.
        /// </summary>
        public string LevelId { get; set; } = "LEVEL_0";

        /// <summary>
        /// Logical elevation (Pseudo-Z) relative to the Level base height.
        /// </summary>
        public double PseudoZ { get; set; }

        /// <summary>
        /// Link to the JSON Material Library (e.g., "WALL_EXTERIOR_300").
        /// </summary>
        public string MaterialId { get; set; } = string.Empty;

        /// <summary>
        /// Calculates the net volume including opening subtractions.
        /// </summary>
        /// <returns>Volume in cubic meters (m3).</returns>
        public abstract double GetVolume();

        /// <summary>
        /// Synchronizes the entity parameters from the external JSON master.
        /// </summary>
        public abstract void SyncFromJSON();

        /// <summary>
        /// Injects entity metadata into AutoCAD XData.
        /// </summary>
        /// <param name="ent">Target AutoCAD Entity.</param>
        /// <param name="tr">Active transaction.</param>
        public abstract void SaveToXData(Entity ent, Transaction tr);

        /// <summary>
        /// Extracts entity metadata from AutoCAD XData.
        /// </summary>
        /// <param name="ent">Source AutoCAD Entity.</param>
        public abstract void LoadFromXData(Entity ent);
    }
}