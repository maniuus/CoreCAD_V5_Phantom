using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace CoreCAD.Models
{
    /// <summary>
    /// Represents a library asset template for CoreCAD objects.
    /// </summary>
    [JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
    public class ObjectTemplate
    {
        public string TemplateId { get; set; } = string.Empty;
        public string Name { get; set; } = string. Empty;
        public string Discipline { get; set; } = string.Empty; // architecture, structure, etc.
        public Dictionary<string, double> DefaultParameters { get; set; } = new();
    }

    /// <summary>
    /// Represents a live entity instance in the AutoCAD drawing.
    /// </summary>
    [JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
    public class CoreEntity
    {
        public string Guid { get; set; } = string.Empty;
        public string RoleId { get; set; } = string.Empty;
        public string Mark { get; set; } = string.Empty; // e.g. "D1", "P1"
        public int VersionId { get; set; } = 1;
        public string ParentId { get; set; } = string.Empty;
        public string TemplateId { get; set; } = string.Empty;
        public string SourceFile { get; set; } = string.Empty;
        public string ViewType { get; set; } = string.Empty; // PLAN, SECTION, DETAIL, etc.
        public string TargetGuid { get; set; } = string.Empty; 
        public string LabelFormat { get; set; } = string.Empty; 
        public bool IsDeleted { get; set; } 
        
        // Physical DNA (Metadata)
        public string Material { get; set; } = string.Empty;
        public string PhysicsNote { get; set; } = string.Empty;
        
        public GeometryData Geometry { get; set; } = new();
        public Dictionary<string, string> Attributes { get; set; } = new();
    }

    [JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
    public class GeometryData
    {
        [JsonConverter(typeof(CoreCAD.Persistence.Precision4Converter))]
        public double Width { get; set; }
        
        [JsonConverter(typeof(CoreCAD.Persistence.Precision4Converter))]
        public double Height { get; set; }
        
        [JsonConverter(typeof(CoreCAD.Persistence.Precision4Converter))]
        public double LocalZ { get; set; }
        
        [JsonConverter(typeof(CoreCAD.Persistence.Precision6Converter))]
        public double LocalX { get; set; }
        
        [JsonConverter(typeof(CoreCAD.Persistence.Precision6Converter))]
        public double LocalY { get; set; }
        
        [JsonConverter(typeof(CoreCAD.Persistence.Precision4Converter))]
        public double Rotation { get; set; }
        
        public double Length { get; set; }
        public bool FlipState { get; set; } // Mirror state for doors/blocks

        [JsonConverter(typeof(CoreCAD.Persistence.Precision4Converter))]
        public double SlopePercentage { get; set; }
    }
}
