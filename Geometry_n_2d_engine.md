# IMPLEMENTATION PLAN: GEOMETRY & 2D ENGINE (V5.0)

## 1. OBJECTIVE
Implement a robust geometry engine that extracts real-world dimensions from AutoCAD entities and maps them to the CoreCAD SSOT (Single Source of Truth) in JSON.

## 2. CORE COMPONENTS
### A. `GeometryEngine.cs` (Static Logic)
- **`ExtractEntityGeometry(Entity ent, string roleId)`**:
  - `Line`: Map length to `Width` (if wall) or `Length` (if pipe).
  - `Polyline`: Calculate bounding area and segment lengths.
  - `BlockReference`: Map insertion point to `LocalX, LocalY` and rotation.
- **`CalculateZAlpha(double length, double slopePct)`**:
  - Implements $\Delta Z = L \times (s/100)$.
- **`ApplyEntityGeometry(Entity ent, GeometryData geo)`**:
  - Updates CAD entity geometry (position, rotation, dimensions) from JSON Data.
  - Enforces Visual $Z=0$ while maintaining 2.5D data in XData.

### B. Integration with `JsonEngine.cs`
- Modify `ScanDrawing` to call `GeometryEngine.ExtractEntityGeometry`.
- Ensure `GeometryData` is fully populated during synchronization.

### C. 2.5D Enforcement Logic
- Ensure all visual entities in the Master DWG are kept at $Z=0$ (visual).
- Store "True Elevation" (Local_Z) exclusively in XData and JSON.

## 3. EXECUTION STEPS
1. [x] Create `CoreCAD.Core.GeometryEngine` class.
2. [x] Implement specialized extraction for Architecture roles (Walls, Openings).
3. [x] Implement specialized extraction for MEFP roles (Pipes, Ducts).
4. [x] Integrate with `JsonEngine.ScanDrawing`.
5. [x] Add unit tests/validation command (VALIDATE_GEO).

## 4. SUCCESS CRITERIA
- GUID + Geometry sync between CAD and JSON has < 0.001mm drift.
- MEFP Pipe slopes correctly calculate Z-offset in JSON.