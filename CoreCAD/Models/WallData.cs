using System;

namespace CoreCAD.Models
{
    /// <summary>
    /// [V5 DATA MODEL] KTP / Identitas Dinding.
    /// Pure data — tidak ada referensi AutoCAD di sini.
    /// </summary>
    public class WallData
    {
        public string Id { get; set; }
        public double Thickness { get; set; }

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
