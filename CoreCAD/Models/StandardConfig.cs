using System.Collections.Generic;
using Newtonsoft.Json;

namespace CoreCAD.Models
{
    public class StandardConfig
    {
        public Dictionary<string, LayerConfig> Layers { get; set; } = new();
    }

    public class LayerConfig
    {
        public string Name { get; set; } = "0";
        public short  Color { get; set; } = 7;
        public string Linetype { get; set; } = "Continuous";
    }
}
