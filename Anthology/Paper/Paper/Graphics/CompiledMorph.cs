using System.Numerics;

namespace Prowl.PaperUI.Graphics
{
    /// <summary>
    /// Represents a pre-compiled morph between two shapes for efficient runtime morphing
    /// </summary>
    public class CompiledMorph
    {
        private readonly List<Vector2> _fromPoints;
        private readonly List<Vector2> _toPoints;
        private readonly MorphShape _originalFrom;
        private readonly MorphShape _originalTo;

        // Constructor is internal - use MorphShape.Compile to create
        internal CompiledMorph(MorphShape from, MorphShape to, List<Vector2> fromPoints, List<Vector2> toPoints)
        {
            _originalFrom = from;
            _originalTo = to;
            _fromPoints = fromPoints;
            _toPoints = toPoints;
        }

        /// <summary>
        /// Evaluates the morph at a specific t value and applies it to the NanoVG context
        /// </summary>
        /// <param name="vg">The NanoVG context to draw to</param>
        /// <param name="t">Interpolation factor (0-1)</param>
        public void Evaluate(Canvas vg, float x, float y, float t)
        {
            // Fast path for extreme values
            if (t <= float.Epsilon)
            {
                vg.BeginPath();
                foreach (var cmd in _originalFrom.Commands)
                    vg.AppendCommand(cmd);
                return;
            }
            else if (t >= 1 - float.Epsilon)
            {
                vg.BeginPath();
                foreach (var cmd in _originalTo.Commands)
                    vg.AppendCommand(cmd);
                return;
            }

            // Interpolate points directly (already normalized and matched)
            var morphedPoints = new List<Vector2>(_fromPoints.Count);
            for (int i = 0; i < _fromPoints.Count; i++)
            {
                morphedPoints.Add(Vector2.Lerp(_fromPoints[i], _toPoints[i], t));
            }

            // Convert points to path
            vg.BeginPath();
            if (morphedPoints.Count > 0)
            {
                vg.MoveTo(x + morphedPoints[0].X, y + morphedPoints[0].Y);
                for (int i = 1; i < morphedPoints.Count; i++)
                {
                    vg.LineTo(x + morphedPoints[i].X, y + morphedPoints[i].Y);
                }
                vg.ClosePath();
            }
        }

        /// <summary>
        /// Returns the interpolated points at a specific t value without drawing
        /// </summary>
        /// <param name="t">Interpolation factor (0-1)</param>
        public List<Vector2> GetPoints(float t)
        {
            if (t <= float.Epsilon)
                return new List<Vector2>(_fromPoints);
            else if (t >= 1 - float.Epsilon)
                return new List<Vector2>(_toPoints);

            var morphedPoints = new List<Vector2>(_fromPoints.Count);
            for (int i = 0; i < _fromPoints.Count; i++)
            {
                morphedPoints.Add(Vector2.Lerp(_fromPoints[i], _toPoints[i], t));
            }
            return morphedPoints;
        }
    }
}