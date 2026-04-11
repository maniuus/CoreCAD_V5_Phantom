using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using System;

namespace CoreCAD.Overrules
{
    /// <summary>
    /// [CLEAN MODE] Overrule untuk menyembunyikan Grip (titik biru) pada raga dinding.
    /// Ini mempermudah pemilihan "Line KTP" karena tidak tertumpuk oleh Grip Polyline/Hatch yang kompleks.
    /// </summary>
    public class WallViewGripOverrule : GripOverrule
    {
        private static WallViewGripOverrule? _instance;
        public static WallViewGripOverrule Instance => _instance ??= new WallViewGripOverrule();

        public WallViewGripOverrule()
        {
            // [OPTIMASI] Hanya trigger jika entitas memiliki XData "CORECAD_VIEW"
            // Filter ini sangat efisien karena ditangani langsung oleh AutoCAD engine.
            SetXDataFilter("CORECAD_VIEW");
        }

        public override void GetGripPoints(Entity entity, GripDataCollection grips, double curViewUnitSize, int gripSize, Vector3d curViewDir, GetGripPointsFlags bitFlags)
        {
            // Jangan tambahkan grip apa pun ke koleksi.
            // Hasil: Objek terpilih tetapi tidak ada handle biru yang muncul.
        }

        public override void GetGripPoints(Entity entity, Point3dCollection gripPoints, IntegerCollection snappedGripIndices, IntegerCollection osnapGripIndices)
        {
            // Fallback untuk legacy grip points (jika diperlukan)
        }

        public override void MoveGripPointsAt(Entity entity, GripDataCollection grips, Vector3d offset, MoveGripPointsFlags bitFlags)
        {
            // Matikan kemampuan memindahkan vertex via grip (Safety Lock)
        }

        public override void MoveGripPointsAt(Entity entity, IntegerCollection indices, Vector3d offset)
        {
            // Fallback untuk legacy movement
        }
    }
}
