using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace CoreCAD.Core.Services
{
    /// <summary>
    /// Service for reading and managing JSON-based project settings and libraries.
    /// Provides data-driven configurations for layers, materials, and drafting rules.
    /// </summary>
    public static class JsonService
    {
        private static DrawingStandard _standard = new DrawingStandard();
        private static string StandardFilePath => Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty, "drawing_standard.json");

        static JsonService()
        {
            LoadDrawingStandard();
        }

        public static void LoadDrawingStandard()
        {
            try
            {
                if (File.Exists(StandardFilePath))
                {
                    string json = File.ReadAllText(StandardFilePath);
                    var data = JsonSerializer.Deserialize<DrawingStandard>(json);
                    if (data != null) _standard = data;
                }
            }
            catch (Exception ex)
            {
                Logger.Write(new Exception("Failed to load drawing_standard.json", ex));
                // Fallback implemented via default constructor of DrawingStandard
            }
        }

        public static LayerConfig GetLayerConfig(string key)
        {
            if (_standard.Layers != null && _standard.Layers.TryGetValue(key, out var config))
            {
                return config;
            }

            // High-Safety Fallback (Logic Sesuai SOP: No Nulls)
            return new LayerConfig { Name = $"C-{key.ToUpper()}", Color = 7 };
        }

        public static double GetDefaultThickness() => 150.0;
        public static double GetDefaultHeight() => 3000.0;
        public static string GetDefaultMaterial() => "STD_WALL_150";
        public static string GetCurrentLevel() => "LEVEL_1";
    }

    public class DrawingStandard
    {
        public Dictionary<string, LayerConfig>? Layers { get; set; } = new Dictionary<string, LayerConfig>();
    }

    public class LayerConfig
    {
        public string Name { get; set; } = "C-DEF";
        public short Color { get; set; } = 7;
        public string Linetype { get; set; } = "Continuous";
    }
}
