using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace CoreCAD.Models
{
    [JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
    public class PhysicalDNA
    {
        public string Mark { get; set; } = string.Empty;
        public double Width { get; set; }
        public double Height { get; set; }
        public double LeafThickness { get; set; } = 40.0;
        public double SwingAngle { get; set; } = 90.0;
        public string Material { get; set; } = string.Empty;
        public string PhysicsNote { get; set; } = string.Empty;
        public Dictionary<string, string> Attributes { get; set; } = new();
    }

    [JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
    public class InstanceData
    {
        public string SourceFile { get; set; } = string.Empty;
        public string ViewType { get; set; } = string.Empty;
        public GeometryData Geometry { get; set; } = new();
        public bool IsHidden { get; set; }
        public int VersionId { get; set; } = 1;
    }

    [JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
    public class SmartObject
    {
        public string Guid { get; set; } = string.Empty;
        public string RoleId { get; set; } = string.Empty;
        public string ParentId { get; set; } = string.Empty;
        public bool IsDeleted { get; set; }
        
        public PhysicalDNA Dna { get; set; } = new();
        public List<InstanceData> Instances { get; set; } = new();

        // COMPATIBILITY LAYER: Membaca data legacy 'geometry' di root JSON
        [JsonProperty("geometry")]
        public GeometryData? LegacyGeometry { get; set; }
        
        [JsonProperty("mark")]
        public string? LegacyMark { get; set; }

        // Helper untuk mendapatkan Width secara cerdas (Super Hybrid)
        [JsonIgnore]
        public double Width
        {
            get
            {
                if (Dna.Width != 0) return Dna.Width;
                var inst = Instances.FirstOrDefault();
                if (inst != null && inst.Geometry.Width != 0) return inst.Geometry.Width;
                return LegacyGeometry?.Width ?? 0;
            }
        }
        
        [JsonIgnore]
        public double Height
        {
            get
            {
                if (Dna.Height != 0) return Dna.Height;
                var inst = Instances.FirstOrDefault();
                if (inst != null && inst.Geometry.Height != 0) return inst.Geometry.Height;
                return LegacyGeometry?.Height ?? 0;
            }
        }

        public InstanceData? GetInstance(string fileName)
        {
            return Instances.Find(i => i.SourceFile.Equals(fileName, System.StringComparison.OrdinalIgnoreCase));
        }
    }
}
