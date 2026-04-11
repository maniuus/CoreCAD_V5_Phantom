using System.Collections.Generic;

namespace CoreCAD.Engine
{
    /// <summary>
    /// [V5 RENDER ENGINE] The View Generator — reads WallData and renders geometry.
    /// Replaces the BooleanUnionEngine (heavy Region/BRep) and WallDrawJig (EntityJig).
    ///
    /// Strategy:
    ///   - Plan View   : Direct 2D polyline from wall vertices (no solid ops)
    ///   - Preview     : Lightweight polygon via JigPrompts, no DrawJig class
    ///   - Junction    : Mitered/bisected endpoints resolved by WallData geometry
    /// </summary>
    public static class ViewGenerator
    {
        // TODO V5: Implementasi GeneratePlanView(WallData wall) -> Polyline
        // TODO V5: Implementasi GeneratePreview(Point3d start, Point3d end, double thickness) -> Point3dCollection
        // TODO V5: Implementasi ResolveJunctions(IEnumerable<WallData> walls) -> update WallData endpoints
    }
}
