using System;
using Newtonsoft.Json;

namespace CoreCAD.Persistence
{
    /// <summary>
    /// Bidirectional JSON Precision Converters (SSOT Granularity Control).
    /// </summary>
    public class Precision6Converter : JsonConverter
    {
        public override bool CanConvert(Type objectType) => objectType == typeof(double) || objectType == typeof(double?);
        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            if (value is double d) writer.WriteValue(Math.Round(d, 6));
            else writer.WriteNull();
        }
        public override bool CanRead => false;
        public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer) => throw new NotImplementedException();
    }

    public class Precision4Converter : JsonConverter
    {
        public override bool CanConvert(Type objectType) => objectType == typeof(double) || objectType == typeof(double?);
        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            if (value is double d) writer.WriteValue(Math.Round(d, 4));
            else writer.WriteNull();
        }
        public override bool CanRead => false;
        public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer) => throw new NotImplementedException();
    }

    public class Precision2Converter : JsonConverter
    {
        public override bool CanConvert(Type objectType) => objectType == typeof(double) || objectType == typeof(double?);
        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            if (value is double d) writer.WriteValue(Math.Round(d, 2));
            else writer.WriteNull();
        }
        public override bool CanRead => false;
        public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer) => throw new NotImplementedException();
    }
}
