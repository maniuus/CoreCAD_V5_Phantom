using Autodesk.AutoCAD.Runtime;

namespace CoreCAD.Commands
{
    /// <summary>
    /// [V5 COMMAND] CC_REBUILD — Command-driven replacement for the Application.Idle reactor.
    /// 
    /// Philosophy:
    ///   - No more automatic reactor firing (eliminates eInvalidOpenState race conditions).
    ///   - User/system explicitly calls CC_REBUILD to regenerate all wall geometry from WallData.
    ///   - One transaction, one pass: reads all MASTER lines -> WallData -> ViewGenerator -> commit.
    /// </summary>
    public class CmdRebuild
    {
        [CommandMethod("CC_REBUILD", CommandFlags.Modal)]
        public void Execute()
        {
            // TODO V5: Implementasi execute pipeline:
            //   1. Baca semua Line dengan XData CORECAD_ENGINE dari current space
            //   2. Deserialize ke List<WallData>
            //   3. Jalankan ViewGenerator.ResolveJunctions(walls)
            //   4. Untuk setiap WallData, jalankan ViewGenerator.GeneratePlanView(wall)
            //   5. Update/Create Polyline + Hatch dalam satu TransactionHelper.ExecuteAtomic
            //   6. ed.Regen()
        }
    }
}
