namespace CoreCAD.Core
{
    /// <summary>
    /// 1. PROJECT IDENTITIES
    /// Identitas aplikasi dan nama file database utama.
    /// </summary>
    public static class ProjectIdentities
    {
        public const string RegAppName = "CoreCAD_V5";
        public const string ProjectMaster = "CoreCAD_Master.json";
        public const string LibStandards = "library_standards.json";
    }

    /// <summary>
    /// 2. ARCHITECTURE ROLES (Untuk Denah/Potongan)
    /// </summary>
    public static class ArchitectureRoles
    {
        public const string WallExt = "wall_ext";
        public const string WallInt = "wall_int";
        public const string WallHatch = "wall_hatch";
        public const string DoorFrame = "door_frame";
        public const string DoorLeaf = "door_leaf";
        public const string WindowFrame = "window_frame";
        public const string WindowGlass = "window_glass";
        public const string FloorFinish = "floor_finish";
        public const string CeilingOutline = "ceiling_outline";
    }

    /// <summary>
    /// 3. STRUCTURE ROLES (Untuk Detail Pondasi/Kolom/Balok)
    /// </summary>
    public static class StructureRoles
    {
        public const string FndTop = "fnd_top";
        public const string FndBtm = "fnd_btm";
        public const string FndSand = "fnd_sand";
        public const string FndStone = "fnd_stone";
        public const string ColFace = "col_face";
        public const string ColRebarMain = "col_rebar_main";
        public const string ColRebarStirrup = "col_rebar_stirrup";
        public const string BeamTop = "beam_top";
        public const string BeamBtm = "beam_btm";
        public const string SlabThickness = "slab_thickness";
    }

    /// <summary>
    /// 4. MEFP ROLES (Untuk Skematik/Iso/Denah)
    /// </summary>
    public static class MefpRoles
    {
        public const string PipeCl = "pipe_cl";
        public const string PipeWall = "pipe_wall";
        public const string DuctCl = "duct_cl";
        public const string DuctWall = "duct_wall";
        public const string CableTray = "cable_tray";
        public const string EquipmentBase = "equipment_base";
        public const string SlopeSymbol = "slope_symbol";
        public const string FlowArrow = "flow_arrow";
    }

    /// <summary>
    /// 5. SHEET MANAGEMENT (Untuk Automasi 200 Hal)
    /// </summary>
    public static class SheetManagement
    {
        public static readonly string[] Categories = { "PLANS", "SECTIONS", "DETAILS", "SCHEDULES" };
        public static readonly string[] DisciplineCodes = { "AR", "ST", "ME", "PL", "EL" };
    }

    /// <summary>
    /// 6. PARAMETER KEYS (Untuk JSON Mapping)
    /// </summary>
    public static class ParameterKeys
    {
        public const string Width = "width";
        public const string Height = "height";
        public const string Length = "length";
        public const string Thickness = "thickness";
        public const string Diameter = "diameter";
        public const string SlopePct = "slope_pct";
        public const string ElevationTop = "elevation_top";
        public const string ElevationBtm = "elevation_btm";
        public const string MaterialSpec = "material_spec";
        public const string VendorName = "vendor_name";
    }

    /// <summary>
    /// 7. XDATA INDEX (Pengunci Urutan)
    /// </summary>
    public static class XDataIndex
    {
        public const int IdxGuid = 1;
        public const int IdxRole = 2;
        public const int IdxParent = 3;
        public const int IdxZ = 4;
    }
}