using System;

namespace CoreCAD.Models
{
    /// <summary>
    /// [V5 DATA MODEL] KTP / Identitas Dinding.
    /// Pure data — tidak ada referensi AutoCAD di sini.
    /// </summary>
    public class Opening
    {
        public double Position { get; set; } // mm dari start
        public double Width { get; set; }    // lebar opening
        public string BlockName { get; set; } = "";

        public Opening() { }
        public Opening(double pos, double width, string blockName)
        {
            Position = pos;
            Width = width;
            BlockName = blockName;
        }
    }

    /// <summary>
    /// [V5 DATA MODEL] KTP / Identitas Dinding.
    /// Pure data — tidak ada referensi AutoCAD di sini.
    /// </summary>
    public class WallData
    {
        public string Id { get; set; }
        public double Thickness { get; set; }
        public System.Collections.Generic.List<Opening> Openings { get; set; } = new System.Collections.Generic.List<Opening>();

        public WallData() { } // Required for Newtonsoft.Json

        public WallData(double thickness)
        {
            Id = Guid.NewGuid().ToString();
            Thickness = thickness;
        }

        public WallData(string id, double thickness)
        {
            Id = id;
            Thickness = thickness;
        }
    }
}
