using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;

namespace CoreCAD.Modules.Architecture
{
    /// <summary>
    /// Real-time "Phantom Preview" for SmartWall placement.
    /// Draws a polygon representing the wall thickness during drafting.
    /// </summary>
    public class WallDrawJig : DrawJig
    {
        private readonly SmartWall _wall;
        private Point3d _cursorPos;

        public WallDrawJig(SmartWall wall)
        {
            _wall = wall;
            _cursorPos = wall.StartPoint;
        }

        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            var options = new JigPromptPointOptions("\nSpecify wall end point: ")
            {
                BasePoint = _wall.StartPoint,
                UseBasePoint = true,
                UserInputControls = UserInputControls.Accept3dCoordinates | 
                                   UserInputControls.NoZeroResponseAccepted
            };

            var result = prompts.AcquirePoint(options);
            if (result.Status == PromptStatus.OK)
            {
                if (result.Value.DistanceTo(_cursorPos) < 0.001)
                    return SamplerStatus.NoChange;

                _cursorPos = result.Value;
                _wall.EndPoint = _cursorPos;
                return SamplerStatus.OK;
            }

            return SamplerStatus.Cancel;
        }

        protected override bool WorldDraw(WorldDraw draw)
        {
            if (_wall.Length < 0.1) return true;

            // Use the SmartWall model to generate preview geometry
            Point3dCollection pts = _wall.GetVertices();

            if (pts.Count >= 4)
            {
                draw.Geometry.Polygon(pts);
                draw.Geometry.WorldLine(_wall.StartPoint, _wall.EndPoint);
            }

            return true;
        }
    }
}
