using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace CoreCAD.Models
{
    /// <summary>
    /// Root object for the Project Master JSON database.
    /// </summary>
    [JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
    public class ProjectMaster
    {
        public ProjectInfo ProjectInfo { get; set; } = new();
        public List<CoreEntity> Entities { get; set; } = new();
        public List<string> ProjectFiles { get; set; } = new();
    }

    [JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
    public class ProjectInfo
    {
        public string ProjectName { get; set; } = string.Empty;
        public double BaseElevation { get; set; }
        public DateTime LastSyncTimestamp { get; set; }
        
        // Dictionary mapping SourceFile name to its clipping boundaries
        public Dictionary<string, BoundData> ViewBounds { get; set; } = new();
    }

    [JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
    public class BoundData
    {
        public double MinX { get; set; }
        public double MinY { get; set; }
        public double MinZ { get; set; } = -999999; // Default wide range
        public double MaxX { get; set; }
        public double MaxY { get; set; }
        public double MaxZ { get; set; } = 999999;
        public string BoundHandle { get; set; } = string.Empty; // Handle to AutoCAD entity (Polyline/Rect)
    }
}
