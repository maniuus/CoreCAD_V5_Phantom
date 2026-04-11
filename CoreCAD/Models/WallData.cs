using System;

namespace CoreCAD.Models
{
    /// <summary>
    /// [V5 DATA MODEL] Represents the pure data/DNA of a SmartWall element.
    /// This is the single source of truth — no geometry, no AutoCAD references.
    /// All wall state lives here; the Engine reads this to render views.
    /// </summary>
    public class WallData
    {
        // === Identity ===
        public Guid Id { get; set; } = Guid.NewGuid();
        public string GroupId { get; set; } = string.Empty;

        // === Geometry Parameters ===
        public double StartX { get; set; }
        public double StartY { get; set; }
        public double EndX { get; set; }
        public double EndY { get; set; }
        public double Thickness { get; set; } = 150.0;
        public double Height { get; set; } = 3000.0;
        public double PseudoZ { get; set; } = 0.0;

        // === Material & Level ===
        public string MaterialId { get; set; } = "BATA_MERAH";
        public string LevelId { get; set; } = "L00_GF";

        // === Role (Master / Follower) ===
        public string Role { get; set; } = "MASTER";

        // === Metadata ===
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        // TODO V5: Tambahkan junction data, opening references, fire rating, dsb.
    }
}
