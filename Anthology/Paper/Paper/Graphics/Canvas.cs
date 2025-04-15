using FontStashSharp;
using Prowl.PaperUI.Utilities;
using Prowl.PaperUI;
using System.Drawing;
using System.Numerics;
using System.Text;
using Texture2D = System.Object;
using Prowl.PaperUI;

namespace Prowl.PaperUI.Graphics
{
    /// <summary>
    /// Core rendering canvas for 2D vector graphics in Paper.
	/// This Class is derived from NanoVG.
    /// </summary>
    public class Canvas
	{
        #region Fields
        private float _commandX, _commandY;
        private float _distTol;
        private float _tessTol;
        internal float _fringeWidth;

        private readonly bool _edgeAntiAlias;
        private readonly Stack<CanvasState> _savedStates = new Stack<CanvasState>();
        private readonly List<Command> _commands = new List<Command>();
        private readonly List<Path> _pathsCache = new List<Path>();

        private Rect _bounds = new Rect();
        internal CanvasState _currentState = new CanvasState();
        internal readonly RenderCache _renderCache;
        internal readonly ICanvasRenderer _renderer;
        internal TextRenderer _textRenderer;
        #endregion

        #region Properties
        public bool EdgeAntiAlias => _edgeAntiAlias;
        public bool StencilStrokes => _renderCache.StencilStrokes;

        public float DevicePixelRatio {
            get => _renderCache.DevicePixelRatio;
            set {
                _tessTol = 0.25f / value;
                _distTol = 0.01f / value;
                _fringeWidth = 1.0f / value;
                _renderCache.DevicePixelRatio = value;
            }
        }
        #endregion

        #region Constructor
        public Canvas(ICanvasRenderer renderer, bool edgeAntiAlias = true, bool stencilStrokes = true)
        {
            _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
            _edgeAntiAlias = edgeAntiAlias;
            _renderCache = new RenderCache(stencilStrokes);
            _textRenderer = new TextRenderer(this);
            ResetState();
            DevicePixelRatio = 1.0f;
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Captures the current path commands into a MorphShape
        /// </summary>
        public MorphShape CaptureMorphShape()
        {
            var shape = new MorphShape();
            foreach (var cmd in _commands)
                shape.Commands.Add(cmd);
            return shape;
        }

        /// <summary>
        /// Flushes the render cache to the renderer
        /// </summary>
        public void Flush()
        {
            _renderer.Draw(_renderCache.DevicePixelRatio, _renderCache.Calls, _renderCache.VertexArray.Array);
            _renderCache.Reset();
        }

        #region State Management
        public void SaveState()
        {
            _savedStates.Push(_currentState);
            _currentState = _currentState.Clone();
        }

        public void RestoreState() => _currentState = _savedStates.Pop();

        public void ResetState()
        {
            var state = _currentState;
            state.Fill = new Brush(Color.White);
            state.Stroke = new Brush(Color.Black);
            state.ShapeAntiAlias = 1;
            state.StrokeWidth = 1.0f;
            state.MiterLimit = 10.0f;
            state.LineCap = Prowl.PaperUI.LineCap.Butt;
            state.LineJoin = Prowl.PaperUI.LineCap.Miter;
            state.Alpha = 1.0f;
            state.Transform = Transform.Identity;
            state.Scissor.Extent.X = -1.0f;
            state.Scissor.Extent.Y = -1.0f;
            state.TextAlign = (Align)0;
        }
        #endregion

        #region Style Settings
        public void ShapeAntiAlias(int enabled) => _currentState.ShapeAntiAlias = enabled;
        public void StrokeWidth(float width) => _currentState.StrokeWidth = width;
        public void MiterLimit(float limit) => _currentState.MiterLimit = limit;
        public void LineCap(LineCap cap) => _currentState.LineCap = cap;
        public void LineJoin(LineCap join) => _currentState.LineJoin = join;
        public void GlobalAlpha(float alpha) => _currentState.Alpha = alpha;
        public void TextAlign(Align align) => _currentState.TextAlign = align;
        #endregion

        #region Transform Methods
        public void TransformBy(Transform t) => _currentState.Transform.Premultiply(ref t);
        public void ResetTransform() => _currentState.Transform = Transform.Identity;
        public void CurrentTransform(Transform xform) => _currentState.Transform = xform;
        #endregion

        #region Color and Paint Methods
        public void StrokeColor(Color color) => _currentState.Stroke = new Brush(color);

        public void StrokePaint(Brush paint)
        {
            _currentState.Stroke = paint;
            _currentState.Stroke.Transform.Multiply(ref _currentState.Transform);
        }

        public void FillColor(Color color) => _currentState.Fill = new Brush(color);

        public void FillPaint(Brush paint)
        {
            _currentState.Fill = paint;
            _currentState.Fill.Transform.Multiply(ref _currentState.Transform);
        }
        #endregion

        #region Gradient Methods
        /// <summary>
        /// Creates a linear gradient brush
        /// </summary>
        public Brush LinearGradient(float sx, float sy, float ex, float ey, Color icol, Color ocol)
        {
            var p = new Brush();
            float dx = ex - sx, dy = ey - sy;
            float d = (float)Math.Sqrt(dx * dx + dy * dy);

            // Normalize dx, dy if possible
            if (d > 0.0001f)
            {
                dx /= d;
                dy /= d;
            }
            else
            {
                dx = 0;
                dy = 1;
            }

            const float large = 1e5f;

            // Setup gradient transform
            p.Transform.A = dy;
            p.Transform.B = -dx;
            p.Transform.C = dx;
            p.Transform.D = dy;
            p.Transform.E = sx - dx * large;
            p.Transform.F = sy - dy * large;

            p.Extent.X = large;
            p.Extent.Y = large + d * 0.5f;
            p.Radius = 0.0f;
            p.Feather = Math.Max(1.0f, d);
            p.InnerColor = icol;
            p.OuterColor = ocol;

            return p;
        }

        /// <summary>
        /// Creates a radial gradient brush
        /// </summary>
        public Brush RadialGradient(float cx, float cy, float inr, float outr, Color icol, Color ocol)
        {
            var p = new Brush();
            var r = (inr + outr) * 0.5f;
            var f = outr - inr;

            p.Transform = Transform.CreateTranslation(cx, cy);
            p.Extent.X = p.Extent.Y = p.Radius = r;
            p.Feather = Math.Max(1.0f, f);
            p.InnerColor = icol;
            p.OuterColor = ocol;

            return p;
        }

        /// <summary>
        /// Creates a box gradient brush
        /// </summary>
        public Brush BoxGradient(float x, float y, float w, float h, float r, float f, Color icol, Color ocol)
        {
            var p = new Brush();
            p.Transform = Transform.CreateTranslation(x + w * 0.5f, y + h * 0.5f);
            p.Extent.X = w * 0.5f;
            p.Extent.Y = h * 0.5f;
            p.Radius = r;
            p.Feather = Math.Max(1.0f, f);
            p.InnerColor = icol;
            p.OuterColor = ocol;

            return p;
        }

        /// <summary>
        /// Creates an image pattern brush
        /// </summary>
        public Brush ImagePattern(float cx, float cy, float w, float h, float angleDegrees, Texture2D image, float alpha)
        {
            var p = new Brush();
            p.Transform = Transform.CreateRotate(angleDegrees) * Transform.CreateTranslation(cx, cy);
            p.Extent.X = w;
            p.Extent.Y = h;
            p.Image = image;
            p.InnerColor = p.OuterColor = PaperUtils.FromRGBA(255, 255, 255, (byte)(int)(255 * alpha));

            return p;
        }
        #endregion

        #region Scissor Methods
        /// <summary>
        /// Sets the scissor rectangle for clipping
        /// </summary>
        public void Scissor(float x, float y, float w, float h)
        {
            var state = _currentState;
            w = Math.Max(0.0f, w);
            h = Math.Max(0.0f, h);
            state.Scissor.Transform = Transform.CreateTranslation(x + w * 0.5f, y + h * 0.5f) * state.Transform;
            state.Scissor.Extent.X = w * 0.5f;
            state.Scissor.Extent.Y = h * 0.5f;
        }

        /// <summary>
        /// Intersects the current scissor rectangle with another rectangle
        /// </summary>
        public void IntersectScissor(float x, float y, float w, float h)
        {
            var state = _currentState;
            if (state.Scissor.Extent.X < 0)
            {
                Scissor(x, y, w, h);
                return;
            }

            var pxform = state.Scissor.Transform;
            var ex = state.Scissor.Extent.X;
            var ey = state.Scissor.Extent.Y;
            var invxorm = state.Transform.Inverse();
            pxform.Multiply(ref invxorm);

            // Calculate extent in current transform space
            var tex = ex * Math.Abs(pxform.A) + ey * Math.Abs(pxform.C);
            var tey = ex * Math.Abs(pxform.B) + ey * Math.Abs(pxform.D);

            // Find the intersection
            var rect = __isectRects(pxform.E - tex, pxform.F - tey, tex * 2, tey * 2, x, y, w, h);
            Scissor(rect.X, rect.Y, rect.Width, rect.Height);
        }

        /// <summary>
        /// Resets the scissor rectangle
        /// </summary>
        public void ResetScissor()
        {
            var state = _currentState;
            state.Scissor.Transform.Zero();
            state.Scissor.Extent.X = -1.0f;
            state.Scissor.Extent.Y = -1.0f;
        }
        #endregion

        #region Path Methods
        /// <summary>
        /// Begins a new path
        /// </summary>
        public void BeginPath()
        {
            _commands.Clear();
            _pathsCache.Clear();
        }

        /// <summary>
        /// Moves to a point in the current path
        /// </summary>
        public void MoveTo(float x, float y) => AppendCommand(CommandType.MoveTo, x, y);

        /// <summary>
        /// Adds a line segment to the path
        /// </summary>
        public void LineTo(float x, float y) => AppendCommand(CommandType.LineTo, x, y);

        /// <summary>
        /// Adds a cubic bezier curve to the path
        /// </summary>
        public void BezierTo(float c1x, float c1y, float c2x, float c2y, float x, float y) =>
            AppendCommand(c1x, c1y, c2x, c2y, x, y);

        /// <summary>
        /// Adds a quadratic bezier curve to the path (converted to cubic)
        /// </summary>
        public void QuadTo(float cx, float cy, float x, float y)
        {
            var x0 = _commandX;
            var y0 = _commandY;

            // Convert quadratic to cubic bezier
            AppendCommand(
                x0 + 2.0f / 3.0f * (cx - x0),
                y0 + 2.0f / 3.0f * (cy - y0),
                x + 2.0f / 3.0f * (cx - x),
                y + 2.0f / 3.0f * (cy - y),
                x, y);
        }

        /// <summary>
        /// Adds an arc curve to the path
        /// </summary>
        public void ArcTo(float x1, float y1, float x2, float y2, float radius)
        {
            if (_commands.Count == 0) return;

            var x0 = _commandX;
            var y0 = _commandY;

            // Skip if points are too close or radius is tiny
            if (__ptEquals(x0, y0, x1, y1, _distTol) != 0 ||
                __ptEquals(x1, y1, x2, y2, _distTol) != 0 ||
                __distPtSeg(x1, y1, x0, y0, x2, y2) < _distTol * _distTol ||
                radius < _distTol)
            {
                LineTo(x1, y1);
                return;
            }

            // Calculate tangent vectors
            var dx0 = x0 - x1;
            var dy0 = y0 - y1;
            var dx1 = x2 - x1;
            var dy1 = y2 - y1;
            PaperUtils.Normalize(ref dx0, ref dy0);
            PaperUtils.Normalize(ref dx1, ref dy1);

            // Find angle between tangents
            var a = MathF.Acos(dx0 * dx1 + dy0 * dy1);
            var d = radius / MathF.Tan(a / 2.0f);

            // If d is too large, fall back to a straight line
            if (d > 10000.0f)
            {
                LineTo(x1, y1);
                return;
            }

            // Calculate center and angles for arc
            float cx, cy, a0, a1;
            Winding dir;

            // Determine arc direction and center
            if (PaperUtils.Cross(dx0, dy0, dx1, dy1) > 0.0f)
            {
                cx = x1 + dx0 * d + dy0 * radius;
                cy = y1 + dy0 * d + -dx0 * radius;
                a0 = MathF.Atan2(dx0, -dy0);
                a1 = MathF.Atan2(-dx1, dy1);
                dir = Winding.ClockWise;
            }
            else
            {
                cx = x1 + dx0 * d + -dy0 * radius;
                cy = y1 + dy0 * d + dx0 * radius;
                a0 = MathF.Atan2(-dx0, dy0);
                a1 = MathF.Atan2(dx1, -dy1);
                dir = Winding.CounterClockWise;
            }

            Arc(cx, cy, radius, a0, a1, dir);
        }

        /// <summary>
        /// Closes the current path
        /// </summary>
        public void ClosePath() => AppendCommand(CommandType.Close);

        /// <summary>
        /// Sets the winding rule for the current path
        /// </summary>
        public void PathWinding(Solidity dir) => AppendCommand(dir);

        /// <summary>
        /// Adds an arc to the path
        /// </summary>
        public void Arc(float cx, float cy, float r, float a0, float a1, Winding dir)
        {
            // Determine initial command type
            var move = _commands.Count > 0 ? CommandType.LineTo : CommandType.MoveTo;

            // Normalize angle range
            var da = a1 - a0;
            if (dir == Winding.ClockWise)
            {
                if (Math.Abs(da) >= MathF.PI * 2)
                    da = MathF.PI * 2;
                else
                    while (da < 0.0f) da += MathF.PI * 2;
            }
            else
            {
                if (Math.Abs(da) >= MathF.PI * 2)
                    da = -MathF.PI * 2;
                else
                    while (da > 0.0f) da -= MathF.PI * 2;
            }

            // Split arc into bezier curves (max 5 segments)
            var ndivs = Math.Max(1, Math.Min((int)(Math.Abs(da) / (MathF.PI * 0.5f) + 0.5f), 5));
            var hda = da / ndivs / 2.0f;
            var kappa = Math.Abs(4.0f / 3.0f * (1.0f - MathF.Cos(hda)) / MathF.Sin(hda));
            if (dir == Winding.CounterClockWise)
                kappa = -kappa;

            float px = 0, py = 0, ptanx = 0, ptany = 0;

            // Generate bezier segments to approximate the arc
            for (var i = 0; i <= ndivs; i++)
            {
                var a = a0 + da * (i / (float)ndivs);
                var dx = MathF.Cos(a);
                var dy = MathF.Sin(a);
                var x = cx + dx * r;
                var y = cy + dy * r;
                var tanx = -dy * r * kappa;
                var tany = dx * r * kappa;

                if (i == 0)
                {
                    AppendCommand(move, x, y);
                }
                else
                {
                    AppendCommand(px + ptanx, py + ptany, x - tanx, y - tany, x, y);
                }

                px = x;
                py = y;
                ptanx = tanx;
                ptany = tany;
            }
        }

        /// <summary>
        /// Adds a rectangle to the path
        /// </summary>
        public void Rect(float x, float y, float w, float h)
        {
            AppendCommand(CommandType.MoveTo, x, y);
            AppendCommand(CommandType.LineTo, x, y + h);
            AppendCommand(CommandType.LineTo, x + w, y + h);
            AppendCommand(CommandType.LineTo, x + w, y);
            AppendCommand(CommandType.Close);
        }

        /// <summary>
        /// Adds a rounded rectangle with uniform corner radii
        /// </summary>
        public void RoundedRect(float x, float y, float w, float h, float r) =>
            RoundedRectVarying(x, y, w, h, r, r, r, r);

        /// <summary>
        /// Adds a rounded rectangle with different corner radii
        /// </summary>
        public void RoundedRectVarying(float x, float y, float w, float h, float radTopLeft, float radTopRight,
            float radBottomRight, float radBottomLeft)
        {
            // If all corners have tiny radii, fall back to regular rectangle
            if (radTopLeft < 0.1f && radTopRight < 0.1f && radBottomRight < 0.1f && radBottomLeft < 0.1f)
            {
                Rect(x, y, w, h);
                return;
            }

            // Calculate size limits for corners
            var halfw = Math.Abs(w) * 0.5f;
            var halfh = Math.Abs(h) * 0.5f;

            // Clamp corner radii to fit within the rectangle
            var rxBL = Math.Min(radBottomLeft, halfw) * Math.Sign(w);
            var ryBL = Math.Min(radBottomLeft, halfh) * Math.Sign(h);
            var rxBR = Math.Min(radBottomRight, halfw) * Math.Sign(w);
            var ryBR = Math.Min(radBottomRight, halfh) * Math.Sign(h);
            var rxTR = Math.Min(radTopRight, halfw) * Math.Sign(w);
            var ryTR = Math.Min(radTopRight, halfh) * Math.Sign(h);
            var rxTL = Math.Min(radTopLeft, halfw) * Math.Sign(w);
            var ryTL = Math.Min(radTopLeft, halfh) * Math.Sign(h);

            // Draw the rounded rectangle using cubic bezier curves for corners
            AppendCommand(CommandType.MoveTo, x, y + ryTL);
            AppendCommand(CommandType.LineTo, x, y + h - ryBL);
            AppendCommand(x, y + h - ryBL * (1 - PaperUtils.NVG_KAPPA90), x + rxBL * (1 - PaperUtils.NVG_KAPPA90), y + h, x + rxBL, y + h);
            AppendCommand(CommandType.LineTo, x + w - rxBR, y + h);
            AppendCommand(x + w - rxBR * (1 - PaperUtils.NVG_KAPPA90), y + h, x + w, y + h - ryBR * (1 - PaperUtils.NVG_KAPPA90), x + w, y + h - ryBR);
            AppendCommand(CommandType.LineTo, x + w, y + ryTR);
            AppendCommand(x + w, y + ryTR * (1 - PaperUtils.NVG_KAPPA90), x + w - rxTR * (1 - PaperUtils.NVG_KAPPA90), y, x + w - rxTR, y);
            AppendCommand(CommandType.LineTo, x + rxTL, y);
            AppendCommand(x + rxTL * (1 - PaperUtils.NVG_KAPPA90), y, x, y + ryTL * (1 - PaperUtils.NVG_KAPPA90), x, y + ryTL);
            AppendCommand(CommandType.Close);
        }

        /// <summary>
        /// Adds an ellipse to the path
        /// </summary>
        public void Ellipse(float cx, float cy, float rx, float ry)
        {
            // Create an ellipse using cubic bezier curves
            AppendCommand(CommandType.MoveTo, cx - rx, cy);
            AppendCommand(cx - rx, cy + ry * PaperUtils.NVG_KAPPA90, cx - rx * PaperUtils.NVG_KAPPA90, cy + ry, cx, cy + ry);
            AppendCommand(cx + rx * PaperUtils.NVG_KAPPA90, cy + ry, cx + rx, cy + ry * PaperUtils.NVG_KAPPA90, cx + rx, cy);
            AppendCommand(cx + rx, cy - ry * PaperUtils.NVG_KAPPA90, cx + rx * PaperUtils.NVG_KAPPA90, cy - ry, cx, cy - ry);
            AppendCommand(cx - rx * PaperUtils.NVG_KAPPA90, cy - ry, cx - rx, cy - ry * PaperUtils.NVG_KAPPA90, cx - rx, cy);
            AppendCommand(CommandType.Close);
        }

        /// <summary>
        /// Adds a circle to the path
        /// </summary>
        public void Circle(float cx, float cy, float r) => Ellipse(cx, cy, r, r);
        #endregion

        #region Render Methods
        /// <summary>
        /// Fills the current path with the fill brush
        /// </summary>
        public void Fill()
        {
            var state = _currentState;
            var fillPaint = state.Fill;

            __flattenPaths();

            // Expand fill based on anti-aliasing settings
            if (_edgeAntiAlias && state.ShapeAntiAlias != 0)
                __expandFill(_fringeWidth, Prowl.PaperUI.LineCap.Miter, 2.4f);
            else
                __expandFill(0.0f, Prowl.PaperUI.LineCap.Miter, 2.4f);

            // Apply alpha to fill colors
            MultiplyAlpha(ref fillPaint.InnerColor, state.Alpha);
            MultiplyAlpha(ref fillPaint.OuterColor, state.Alpha);

            _renderCache.RenderFill(ref fillPaint, ref state.Scissor, _fringeWidth, _bounds, _pathsCache);
        }

        /// <summary>
        /// Strokes the current path with the stroke brush
        /// </summary>
        public void Stroke()
        {
            var state = _currentState;

            // Calculate stroke width based on transform scale
            var scale = __getAverageScale(ref state.Transform);
            var strokeWidth = PaperUtils.ClampF(state.StrokeWidth * scale, 0.0f, 200.0f);
            var strokePaint = state.Stroke;

            // Handle very thin strokes by adjusting alpha
            if (strokeWidth < _fringeWidth)
            {
                var alpha = PaperUtils.ClampF(strokeWidth / _fringeWidth, 0.0f, 1.0f);
                MultiplyAlpha(ref strokePaint.InnerColor, alpha * alpha);
                MultiplyAlpha(ref strokePaint.OuterColor, alpha * alpha);
                strokeWidth = _fringeWidth;
            }

            // Apply global alpha
            MultiplyAlpha(ref strokePaint.InnerColor, state.Alpha);
            MultiplyAlpha(ref strokePaint.OuterColor, state.Alpha);

            __flattenPaths();

            // Expand stroke based on anti-aliasing settings
            if (_edgeAntiAlias && state.ShapeAntiAlias != 0)
                __expandStroke(strokeWidth * 0.5f, _fringeWidth, state.LineCap, state.LineJoin, state.MiterLimit);
            else
                __expandStroke(strokeWidth * 0.5f, 0.0f, state.LineCap, state.LineJoin, state.MiterLimit);

            _renderCache.RenderStroke(ref strokePaint, ref state.Scissor, _fringeWidth, strokeWidth, _pathsCache);
        }
        #endregion

        #region Text Methods
        /// <summary>
        /// Renders text with alignment based on current text alignment settings
        /// </summary>
        public void Text(SpriteFontBase font, string text, float x, float y, float width, float height, float layerDepth = 0.0f, float characterSpacing = 0.0f, float lineSpacing = 0.0f, FontSystemEffect effect = FontSystemEffect.None, int effectAmount = 0)
        {
            var align = _currentState.TextAlign;

            // Measure text size
            Vector2 textSize = font.MeasureString(text, null, characterSpacing, lineSpacing);

            float posX = x, posY = y;

            // Horizontal alignment
            if ((align & Align.Center) != 0)
                posX = x + (width - textSize.X) * 0.5f;
            else if ((align & Align.Right) != 0)
                posX = x + width - textSize.X;

            // Vertical alignment
            if ((align & Align.Middle) != 0)
                posY = y + (height - textSize.Y) * 0.5f;
            else if ((align & Align.Bottom) != 0)
                posY = y + height - textSize.Y;

            _textRenderer.Text(font, text, posX, posY, layerDepth, characterSpacing, lineSpacing, effect, effectAmount);
        }

        /// <summary>
        /// Renders text at the specified position
        /// </summary>
        public void Text(SpriteFontBase font, string text, float x, float y, float layerDepth = 0.0f, float characterSpacing = 0.0f, float lineSpacing = 0.0f, FontSystemEffect effect = FontSystemEffect.None, int effectAmount = 0) =>
            Text(font, text, x, y, 0, 0, layerDepth, characterSpacing, lineSpacing, effect, effectAmount);
        #endregion
        #endregion

        #region Internal Methods
        /// <summary>
        /// Adjusts the alpha component of a color
        /// </summary>
        internal static void MultiplyAlpha(ref Color c, float alpha)
        {
            byte na = (byte)(int)(c.A * alpha);
            c = PaperUtils.FromRGBA(c.R, c.G, c.B, na);
        }

        /// <summary>
        /// Appends a command to the command list
        /// </summary>
        internal void AppendCommand(Command command)
        {
            var state = _currentState;

            if (command.Type != CommandType.Close && command.Type != CommandType.Winding)
            {
                _commandX = command.P1;
                _commandY = command.P2;
            }

            // Transform points according to current transform
            switch (command.Type)
            {
                case CommandType.LineTo:
                case CommandType.MoveTo:
                    state.Transform.TransformPoint(out command.P1, out command.P2, command.P1, command.P2);
                    break;
                case CommandType.BezierTo:
                    state.Transform.TransformPoint(out command.P1, out command.P2, command.P1, command.P2);
                    state.Transform.TransformPoint(out command.P3, out command.P4, command.P3, command.P4);
                    state.Transform.TransformPoint(out command.P5, out command.P6, command.P5, command.P6);
                    break;
            }

            _commands.Add(command);
        }

        // Command creation helper methods
        private void AppendCommand(CommandType type) => AppendCommand(new Command(type));
        private void AppendCommand(Solidity solidity) => AppendCommand(new Command(solidity));
        private void AppendCommand(CommandType type, float p1, float p2) => AppendCommand(new Command(type, p1, p2));
        private void AppendCommand(float p1, float p2, float p3, float p4, float p5, float p6) =>
            AppendCommand(new Command(p1, p2, p3, p4, p5, p6));

        #region Path Construction
        private Path GetLastPath() => _pathsCache.Count > 0 ? _pathsCache[_pathsCache.Count - 1] : null;

        private Path __addPath()
        {
            var newPath = new Path { Winding = Winding.CounterClockWise };
            _pathsCache.Add(newPath);
            return newPath;
        }

        private void __addPoint(float x, float y, PointFlags flags)
        {
            var path = GetLastPath();
            if (path == null) return;

            // Check if point equals the last point
            if (path.Points.Count > 0)
            {
                var pt = path.LastPoint;
                if (__ptEquals(pt.X, pt.Y, x, y, _distTol) != 0)
                {
                    pt.Flags |= (byte)flags;
                    path.LastPoint = pt;
                    return;
                }
            }

            // Add new point
            var newPt = new CanvasPoint { X = x, Y = y, Flags = (byte)flags };
            path.Points.Add(newPt);
        }

        private void __closePath()
        {
            var path = GetLastPath();
            if (path != null)
                path.Closed = true;
        }

        private void __pathWinding(Winding winding)
        {
            var path = GetLastPath();
            if (path != null)
                path.Winding = winding;
        }

        struct BezierSegment
        {
            public float X1, Y1, X2, Y2, X3, Y3, X4, Y4;
            public int Level;
            public PointFlags Type;
        }

        /// <summary>
        /// Tessellates a bezier curve into line segments
        /// </summary>
        private void __tesselateBezier(float x1, float y1, float x2, float y2, float x3, float y3, float x4, float y4, int level, PointFlags type)
        {
            // Create a stack to store curve segments to process
            var segmentStack = new Stack<BezierSegment>(32); // Pre-allocate reasonable capacity

            // Push initial curve segment
            segmentStack.Push(new BezierSegment {
                X1 = x1,
                Y1 = y1,
                X2 = x2,
                Y2 = y2,
                X3 = x3,
                Y3 = y3,
                X4 = x4,
                Y4 = y4,
                Level = level,
                Type = type
            });

            // Process all segments
            while (segmentStack.Count > 0)
            {
                var segment = segmentStack.Pop();

                // Stop if we've reached maximum recursion depth
                if (segment.Level > 10)
                    continue;

                // Calculate midpoints
                var x12 = (segment.X1 + segment.X2) * 0.5f;
                var y12 = (segment.Y1 + segment.Y2) * 0.5f;
                var x23 = (segment.X2 + segment.X3) * 0.5f;
                var y23 = (segment.Y2 + segment.Y3) * 0.5f;
                var x34 = (segment.X3 + segment.X4) * 0.5f;
                var y34 = (segment.Y3 + segment.Y4) * 0.5f;
                var x123 = (x12 + x23) * 0.5f;
                var y123 = (y12 + y23) * 0.5f;

                // Calculate deviation from linear path
                var dx = segment.X4 - segment.X1;
                var dy = segment.Y4 - segment.Y1;
                var d2 = Math.Abs((segment.X2 - segment.X4) * dy - (segment.Y2 - segment.Y4) * dx);
                var d3 = Math.Abs((segment.X3 - segment.X4) * dy - (segment.Y3 - segment.Y4) * dx);

                // If curve is flat enough, add the endpoint
                if ((d2 + d3) * (d2 + d3) < _tessTol * (dx * dx + dy * dy))
                {
                    __addPoint(segment.X4, segment.Y4, segment.Type);
                    continue;
                }

                // Further subdivide curve
                var x234 = (x23 + x34) * 0.5f;
                var y234 = (y23 + y34) * 0.5f;
                var x1234 = (x123 + x234) * 0.5f;
                var y1234 = (y123 + y234) * 0.5f;

                // Push second half of curve (will be processed first since we're using a stack)
                segmentStack.Push(new BezierSegment {
                    X1 = x1234,
                    Y1 = y1234,
                    X2 = x234,
                    Y2 = y234,
                    X3 = x34,
                    Y3 = y34,
                    X4 = segment.X4,
                    Y4 = segment.Y4,
                    Level = segment.Level + 1,
                    Type = segment.Type
                });

                // Push first half of curve
                segmentStack.Push(new BezierSegment {
                    X1 = segment.X1,
                    Y1 = segment.Y1,
                    X2 = x12,
                    Y2 = y12,
                    X3 = x123,
                    Y3 = y123,
                    X4 = x1234,
                    Y4 = y1234,
                    Level = segment.Level + 1,
                    Type = 0
                });
            }
        }
        #endregion

        #region Path Processing
        /// <summary>
        /// Flattens the current path commands into point lists
        /// </summary>
        private void __flattenPaths()
        {
            // Skip if already flattened
            if (_pathsCache.Count > 0) return;

			Path lastPath = null;
            // Process all commands
            for (var i = 0; i < _commands.Count; ++i)
			{
				switch (_commands[i].Type)
				{
					case CommandType.MoveTo:
						lastPath = __addPath();
						__addPoint(_commands[i].P1, _commands[i].P2, PointFlags.Corner);
						break;
					case CommandType.LineTo:
						__addPoint(_commands[i].P1, _commands[i].P2, PointFlags.Corner);
						break;
					case CommandType.BezierTo:
						if (lastPath != null && lastPath.Points.Count > 0)
						{
							var last = lastPath.LastPoint;
							__tesselateBezier(last.X, last.Y,
								_commands[i].P1, _commands[i].P2,
								_commands[i].P3, _commands[i].P4,
								_commands[i].P5, _commands[i].P6, 
								0, PointFlags.Corner);
						}

						break;
					case CommandType.Close:
						__closePath();
						break;
					case CommandType.Winding:
						__pathWinding((Winding)_commands[i].P1);
						break;
				}
			}

            // Reset bounds
            _bounds.Min.X = _bounds.Min.Y = 1e6f;
			_bounds.Max.X = _bounds.Max.Y = -1e6f;

            // Process each path
            for (var j = 0; j < _pathsCache.Count; j++)
			{
				var path = _pathsCache[j];

                // Check if path is closed (last point equals first point)
                var p0Index = path.Count - 1;
				var p1Index = 0;
				if (__ptEquals(path.LastPoint.X, path.LastPoint.Y, path.FirstPoint.X, path.FirstPoint.Y, _distTol) != 0)
				{
					path.Points.RemoveAt(path.Points.Count - 1);
					--p0Index;
					path.Closed = true;
				}

                // Ensure correct winding order
                if (path.Points.Count > 2)
				{
					var area = __polyArea(path.Points);
					if (path.Winding == Winding.CounterClockWise && area < 0.0f)
						__polyReverse(path.Points);
					if (path.Winding == Winding.ClockWise && area > 0.0f)
						__polyReverse(path.Points);
				}

                // Calculate delta vectors and path bounds
                for (var i = 0; i < path.Points.Count; i++)
				{
					var p0 = path[p0Index];
					var p1 = path[p1Index];

                    // Calculate delta vector and length
                    p0.DeltaX = p1.X - p0.X;
					p0.DeltaY = p1.Y - p0.Y;
					p0.Length = PaperUtils.Normalize(ref p0.DeltaX, ref p0.DeltaY);

                    path[p0Index] = p0;
                    path[p1Index] = p1;

                    // Update bounds
                    _bounds.Min.X = Math.Min(_bounds.Min.X, p0.X);
					_bounds.Min.Y = Math.Min(_bounds.Min.Y, p0.Y);
					_bounds.Max.X = Math.Max(_bounds.Max.X, p0.X);
					_bounds.Max.Y = Math.Max(_bounds.Max.Y, p0.Y);

					p0Index = p1Index++;
				}
			}
		}

        /// <summary>
        /// Calculates join data for path rendering
        /// </summary>
        private void __calculateJoins(float w, LineCap lineJoin, float miterLimit)
        {
            var iw = w > 0.0f ? 1.0f / w : 0.0f;

			for (int i = 0; i < _pathsCache.Count; i++)
            {
                var path = _pathsCache[i];
                var nleft = 0;
                path.BevelCount = 0;

                // Process each point
                var p0Index = path.Count - 1;
                var p1Index = 0;
                for (int j = 0; j < path.Points.Count; j++)
				{
					var p0 = path[p0Index];
					var p1 = path[p1Index];

                    // Calculate tangent miter vector
                    var dlx0 = p0.DeltaY;
					var dly0 = -p0.DeltaX;
					var dlx1 = p1.DeltaY;
					var dly1 = -p1.DeltaX;
					p1.Dmx = (dlx0 + dlx1) * 0.5f;
					p1.Dmy = (dly0 + dly1) * 0.5f;

                    // Normalize miter vector
                    var dmr2 = p1.Dmx * p1.Dmx + p1.Dmy * p1.Dmy;
					if (dmr2 > 0.000001f)
					{
						var scale = 1.0f / dmr2;
						if (scale > 600.0f)
							scale = 600.0f;
						p1.Dmx *= scale;
						p1.Dmy *= scale;
					}

                    // Determine point flags
                    p1.Flags = (byte)((p1.Flags & (byte)PointFlags.Corner) != 0 ? PointFlags.Corner : 0);
					var cross = p1.DeltaX * p0.DeltaY - p0.DeltaX * p1.DeltaY;
					if (cross > 0.0f)
					{
						nleft++;
						p1.Flags |= (byte)PointFlags.Left;
					}

                    // Check for inner/outer bevel
                    var limit = Math.Max(1.01f, Math.Min(p0.Length, p1.Length) * iw);
					if (dmr2 * limit * limit < 1.0f)
						p1.Flags |= (byte)PointFlags.InnerBevel;

                    // Check for corner/bevel
                    if ((p1.Flags & (byte)PointFlags.Corner) != 0)
						if (dmr2 * miterLimit * miterLimit < 1.0f || lineJoin == Prowl.PaperUI.LineCap.Bevel || lineJoin == Prowl.PaperUI.LineCap.Round)
							p1.Flags |= (byte)PointFlags.Bevel;

                    // Count bevels
                    if ((p1.Flags & (byte)(PointFlags.Bevel | PointFlags.InnerBevel)) != 0)
						path.BevelCount++;

                    // Update points
                    path[p1Index] = p1;
                    path[p0Index] = p0;

                    p0Index = p1Index++;
				}

                // Determine if path is convex
                path.Convex = nleft == path.Points.Count;
			}
		}

        /// <summary>
        /// Expands a stroke path into a mesh
        /// </summary>
        private void __expandStroke(float w, float fringe, LineCap lineCap, LineCap lineJoin, float miterLimit)
		{
			var aa = fringe;
			var u0 = 0.0f;
			var u1 = 1.0f;

            // Adjust for anti-aliasing
            if (aa == 0.0f)
                u0 = u1 = 0.5f;

            // Calculate number of segments for end caps
            var ncap = __curveDivs(w, MathF.PI, _tessTol);
            w += aa * 0.5f;

            // Calculate join data
            __calculateJoins(w, lineJoin, miterLimit);

            // Process each path
            for (var i = 0; i < _pathsCache.Count; i++)
			{
				var vertexOffset = _renderCache.VertexCount;
				var path = _pathsCache[i];

				if (path.Points.Count < 2)
                    continue;

                float dx = 0, dy = 0;
				path.FillCount = 0;
				var loop = path.Closed;

                // Setup indices for looping
                int p0Index, p1Index, s, e;
				if (loop)
				{
					p0Index = path.Count - 1;
					p1Index = 0;
					s = 0;
					e = path.Points.Count;
				}
				else
				{
					p0Index = 0;
					p1Index = 1;
					s = 1;
					e = path.Points.Count - 1;
				}

				var p0 = path[p0Index];
				var p1 = path[p1Index];

                // Add start cap if not a loop
                if (!loop)
				{
					dx = p1.X - p0.X;
					dy = p1.Y - p0.Y;
					PaperUtils.Normalize(ref dx, ref dy);

                    // Handle different cap styles
                    if (lineCap == Prowl.PaperUI.LineCap.Butt)
						__buttCapStart(p0, dx, dy, w, -aa * 0.5f, aa, u0, u1);
					else if (lineCap == Prowl.PaperUI.LineCap.Butt || lineCap == Prowl.PaperUI.LineCap.Square)
						__buttCapStart(p0, dx, dy, w, w - aa, aa, u0, u1);
					else if (lineCap == Prowl.PaperUI.LineCap.Round)
						__roundCapStart(p0, dx, dy, w, ncap, aa, u0, u1);
				}

                // Add all path segments
                for (var j = s; j < e; ++j)
				{
					p0 = path[p0Index];
					p1 = path[p1Index];

                    // Handle joins
                    if ((p1.Flags & (byte)(PointFlags.Bevel | PointFlags.InnerBevel)) != 0)
					{
						if (lineJoin == Prowl.PaperUI.LineCap.Round)
							__roundJoin(p0, p1, w, w, u0, u1, ncap, aa);
						else
							__bevelJoin(p0, p1, w, w, u0, u1, aa);
					}
					else
                    {
                        // Add straight segment
                        _renderCache.AddVertex(p1.X + p1.Dmx * w, p1.Y + p1.Dmy * w, u0, 1);
						_renderCache.AddVertex(p1.X - p1.Dmx * w, p1.Y - p1.Dmy * w, u1, 1);
					}

					p0Index = p1Index++;
				}

                // Close the stroke or add end cap
                if (loop)
                {
                    // Connect back to start
                    var v = _renderCache.VertexArray[vertexOffset];
					_renderCache.AddVertex(v.Position.X, v.Position.Y, u0, 1);
					v = _renderCache.VertexArray[vertexOffset + 1];
					_renderCache.AddVertex(v.Position.X, v.Position.Y, u1, 1);
				}
				else
                {
                    // Add end cap
                    p0 = path[p0Index];
					p1 = path[p1Index];

					dx = p1.X - p0.X;
					dy = p1.Y - p0.Y;
					PaperUtils.Normalize(ref dx, ref dy);
					if (lineCap == Prowl.PaperUI.LineCap.Butt)
						__buttCapEnd(p1, dx, dy, w, -aa * 0.5f, aa, u0, u1);
					else if (lineCap == Prowl.PaperUI.LineCap.Butt || lineCap == Prowl.PaperUI.LineCap.Square)
						__buttCapEnd(p1, dx, dy, w, w - aa, aa, u0, u1);
					else if (lineCap == Prowl.PaperUI.LineCap.Round)
						__roundCapEnd(p1, dx, dy, w, ncap, aa, u0, u1);
				}

                // Store stroke geometry info
                path.StrokeOffset = vertexOffset;
				path.StrokeCount = _renderCache.VertexCount - vertexOffset;
			}
		}

        /// <summary>
        /// Expands a fill path into a mesh
        /// </summary>
        private void __expandFill(float w, LineCap lineJoin, float miterLimit)
		{
			var aa = _fringeWidth;
			var fringe = w > 0.0f;

            // Calculate join data
            __calculateJoins(w, lineJoin, miterLimit);

			var convex = _pathsCache.Count == 1 && _pathsCache[0].Convex;

            // Process each path
            for (var i = 0; i < _pathsCache.Count; i++)
			{
				var vertexOffset = _renderCache.VertexCount;
				var path = _pathsCache[i];
				var woff = 0.5f * aa;

                // Create fill vertices
                if (fringe)
				{
					var p0Index = path.Count - 1;
					var p1Index = 0;
					for (var j = 0; j < path.Points.Count; ++j)
					{
						var p0 = path[p0Index];
						var p1 = path[p1Index];

						if ((p1.Flags & (byte)PointFlags.Bevel) != 0)
						{
							var dlx0 = p0.DeltaY;
							var dly0 = -p0.DeltaX;
							var dlx1 = p1.DeltaY;
							var dly1 = -p1.DeltaX;
							if ((p1.Flags & (byte)PointFlags.Left) != 0)
							{
								var lx = p1.X + p1.Dmx * woff;
								var ly = p1.Y + p1.Dmy * woff;
								_renderCache.AddVertex(lx, ly, 0.5f, 1);
							}
							else
							{
								var lx0 = p1.X + dlx0 * woff;
								var ly0 = p1.Y + dly0 * woff;
								var lx1 = p1.X + dlx1 * woff;
								var ly1 = p1.Y + dly1 * woff;
								_renderCache.AddVertex(lx0, ly0, 0.5f, 1);
								_renderCache.AddVertex(lx1, ly1, 0.5f, 1);
							}
						}
						else
						{
							_renderCache.AddVertex(p1.X + p1.Dmx * woff, p1.Y + p1.Dmy * woff, 0.5f, 1);
						}

						p0Index = p1Index++;
					}
				}
				else
                {
                    // Simple fill with no fringe
                    for (var j = 0; j < path.Count; ++j)
					{
						var p = path[j];
						_renderCache.AddVertex(p.X, p.Y, 0.5f, 1);
					}
				}

                // Store fill geometry info
                path.FillOffset = vertexOffset;
				path.FillCount = _renderCache.VertexCount - vertexOffset;

                // Create stroke for fringe if needed
                if (fringe)
                {
                    vertexOffset = _renderCache.VertexCount;

                    var lw = w + woff;
					var rw = w - woff;
					var lu = 0.0f;
					var ru = 1.0f;

                    // For convex shapes, adjust fringe
                    if (convex)
					{
						lw = woff;
						lu = 0.5f;
					}

                    // Add fringe vertices
                    var p0Index = path.Count - 1;
					var p1Index = 0;

					for (var j = 0; j < path.Points.Count; ++j)
					{
						var p0 = path[p0Index];
						var p1 = path[p1Index];

						if ((p1.Flags & (byte)(PointFlags.Bevel | PointFlags.InnerBevel)) != 0)
						{
							__bevelJoin(p0, p1, lw, rw, lu, ru, _fringeWidth);
						}
						else
						{
							_renderCache.AddVertex(p1.X + p1.Dmx * lw, p1.Y + p1.Dmy * lw, lu, 1);
							_renderCache.AddVertex(p1.X - p1.Dmx * rw, p1.Y - p1.Dmy * rw, ru, 1);
						}

						p0Index = p1Index++;
					}

                    // Close the fringe
                    var v = _renderCache.VertexArray[vertexOffset];
					_renderCache.AddVertex(v.Position.X, v.Position.Y, lu, 1);
					v = _renderCache.VertexArray[vertexOffset + 1];
					_renderCache.AddVertex(v.Position.X, v.Position.Y, ru, 1);

                    // Store stroke geometry info
                    path.StrokeOffset = vertexOffset;
					path.StrokeCount = _renderCache.VertexCount - vertexOffset;
				}
				else
				{
					path.StrokeCount = 0;
				}
			}
        }
        #endregion

        #region Utility Methods
        /// <summary>
        /// Calculates the distance from a point to a line segment
        /// </summary>
        private static float __distPtSeg(float x, float y, float px, float py, float qx, float qy)
        {
            var pqx = qx - px;
            var pqy = qy - py;
            var dx = x - px;
            var dy = y - py;
            var d = pqx * pqx + pqy * pqy;
            var t = pqx * dx + pqy * dy;

            // Project point onto segment
            if (d > 0) t /= d;
            t = PaperUtils.ClampF(t, 0, 1);

            // Calculate closest point on segment
            dx = px + t * pqx - x;
            dy = py + t * pqy - y;

            return dx * dx + dy * dy;
        }

        /// <summary>
        /// Checks if two points are equal within a tolerance
        /// </summary>
        private static int __ptEquals(float x1, float y1, float x2, float y2, float tol)
        {
            var dx = x2 - x1;
            var dy = y2 - y1;
            return dx * dx + dy * dy < tol * tol ? 1 : 0;
        }

        /// <summary>
        /// Calculates the area of a triangle
        /// </summary>
        private static float __triarea2(float ax, float ay, float bx, float by, float cx, float cy)
        {
            var abx = bx - ax;
            var aby = by - ay;
            var acx = cx - ax;
            var acy = cy - ay;
            return acx * aby - abx * acy;
        }

        /// <summary>
        /// Calculates the area of a polygon
        /// </summary>
        private static float __polyArea(List<CanvasPoint> pts)
        {
            var area = 0.0f;
            for (var i = 2; i < pts.Count; i++)
            {
                var a = pts[0];
                var b = pts[i - 1];
                var c = pts[i];
                area += __triarea2(a.X, a.Y, b.X, b.Y, c.X, c.Y);
            }
            return area * 0.5f;
        }

        /// <summary>
        /// Reverses the order of points in a polygon
        /// </summary>
        internal static void __polyReverse(List<CanvasPoint> pts)
        {
            var i = 0;
            var j = pts.Count - 1;
            while (i < j)
            {
                var tmp = pts[i];
                pts[i] = pts[j];
                pts[j] = tmp;
                i++;
                j--;
            }
        }

        /// <summary>
        /// Calculates the intersection of two rectangles
        /// </summary>
        private static Rect __isectRects(float ax, float ay, float aw, float ah, float bx, float by, float bw, float bh)
        {
            var minx = Math.Max(ax, bx);
            var miny = Math.Max(ay, by);
            var maxx = Math.Min(ax + aw, bx + bw);
            var maxy = Math.Min(ay + ah, by + bh);

            return new Rect(minx, miny, Math.Max(0.0f, maxx - minx), Math.Max(0.0f, maxy - miny));
        }

        /// <summary>
        /// Calculates the average scale factor from a transform
        /// </summary>
        private static float __getAverageScale(ref Transform t)
        {
            var sx = (float)Math.Sqrt(t.T1 * t.T1 + t.T3 * t.T3);
            var sy = (float)Math.Sqrt(t.T2 * t.T2 + t.T4 * t.T4);
            return (sx + sy) * 0.5f;
        }

        /// <summary>
        /// Calculates the number of divisions needed for an arc
        /// </summary>
        private static int __curveDivs(float r, float arc, float tol)
        {
            var da = MathF.Acos(r / (r + tol)) * 2.0f;
            return Math.Max(2, (int)MathF.Ceiling(arc / da));
        }

        /// <summary>
        /// Chooses bevel points based on flags
        /// </summary>
        private static Rect __chooseBevel(int bevel, CanvasPoint p0, CanvasPoint p1, float w)
        {
            var result = new Rect();
            if (bevel != 0)
            {
                result.Min = new Vector2(p1.X + p0.DeltaY * w, p1.Y - p0.DeltaX * w);
                result.Max = new Vector2(p1.X + p1.DeltaY * w, p1.Y - p1.DeltaX * w);
            }
            else
            {
                result.Min = result.Max = new Vector2(p1.X + p1.Dmx * w, p1.Y + p1.Dmy * w);
            }
            return result;
        }
        #endregion

        #region Cap and Join Methods
        /// <summary>
        /// Creates a round join between two line segments
        /// </summary>
        private void __roundJoin(CanvasPoint p0, CanvasPoint p1, float lw, float rw, float lu, float ru, int ncap, float fringe)
        {
            var dlx0 = p0.DeltaY;
            var dly0 = -p0.DeltaX;
            var dlx1 = p1.DeltaY;
            var dly1 = -p1.DeltaX;

            if ((p1.Flags & (byte)PointFlags.Left) != 0)
            {
                // Left side join
                var bounds = __chooseBevel(p1.Flags & (byte)PointFlags.InnerBevel, p0, p1, lw);
                var a0 = MathF.Atan2(-dly0, -dlx0);
                var a1 = MathF.Atan2(-dly1, -dlx1);

                // Ensure correct angle direction
                if (a1 > a0) a1 -= MathF.PI * 2;

                _renderCache.AddVertex(bounds.Min.X, bounds.Min.Y, lu, 1);
                _renderCache.AddVertex(p1.X - dlx0 * rw, p1.Y - dly0 * rw, ru, 1);

                // Add arc segments
                var n = PaperUtils.ClampI((int)MathF.Ceiling((a0 - a1) / MathF.PI * ncap), 2, ncap);
                for (var i = 0; i < n; i++)
                {
                    var u = i / (float)(n - 1);
                    var a = a0 + u * (a1 - a0);
                    var rx = p1.X + MathF.Cos(a) * rw;
                    var ry = p1.Y + MathF.Sin(a) * rw;
                    _renderCache.AddVertex(p1.X, p1.Y, 0.5f, 1);
                    _renderCache.AddVertex(rx, ry, ru, 1);
                }

                _renderCache.AddVertex(bounds.Max.X, bounds.Max.Y, lu, 1);
                _renderCache.AddVertex(p1.X - dlx1 * rw, p1.Y - dly1 * rw, ru, 1);
            }
            else
            {
                // Right side join
                var bounds = __chooseBevel(p1.Flags & (byte)PointFlags.InnerBevel, p0, p1, -rw);
                var a0 = MathF.Atan2(dly0, dlx0);
                var a1 = MathF.Atan2(dly1, dlx1);

                // Ensure correct angle direction
                if (a1 < a0) a1 += MathF.PI * 2;

                _renderCache.AddVertex(p1.X + dlx0 * rw, p1.Y + dly0 * rw, lu, 1);
                _renderCache.AddVertex(bounds.Min.X, bounds.Min.Y, ru, 1);

                // Add arc segments
                var n = PaperUtils.ClampI((int)MathF.Ceiling((a1 - a0) / MathF.PI * ncap), 2, ncap);
                for (var i = 0; i < n; i++)
                {
                    var u = i / (float)(n - 1);
                    var a = a0 + u * (a1 - a0);
                    var lx = p1.X + MathF.Cos(a) * lw;
                    var ly = p1.Y + MathF.Sin(a) * lw;
                    _renderCache.AddVertex(lx, ly, lu, 1);
                    _renderCache.AddVertex(p1.X, p1.Y, 0.5f, 1);
                }

                _renderCache.AddVertex(p1.X + dlx1 * rw, p1.Y + dly1 * rw, lu, 1);
                _renderCache.AddVertex(bounds.Max.X, bounds.Max.Y, ru, 1);
            }
        }

        /// <summary>
        /// Creates a bevel join between two line segments
        /// </summary>
        private void __bevelJoin(CanvasPoint p0, CanvasPoint p1, float lw, float rw, float lu, float ru, float fringe)
        {
            var dlx0 = p0.DeltaY;
            var dly0 = -p0.DeltaX;
            var dlx1 = p1.DeltaY;
            var dly1 = -p1.DeltaX;

            if ((p1.Flags & (byte)PointFlags.Left) != 0)
            {
                // Left side join
                var bounds = __chooseBevel(p1.Flags & (byte)PointFlags.InnerBevel, p0, p1, lw);

                _renderCache.AddVertex(bounds.Min.X, bounds.Min.Y, lu, 1);
                _renderCache.AddVertex(p1.X - dlx0 * rw, p1.Y - dly0 * rw, ru, 1);

                if ((p1.Flags & (byte)PointFlags.Bevel) != 0)
                {
                    // Add bevel
                    _renderCache.AddVertex(bounds.Min.X, bounds.Min.Y, lu, 1);
                    _renderCache.AddVertex(p1.X - dlx0 * rw, p1.Y - dly0 * rw, ru, 1);
                    _renderCache.AddVertex(bounds.Max.X, bounds.Max.Y, lu, 1);
                    _renderCache.AddVertex(p1.X - dlx1 * rw, p1.Y - dly1 * rw, ru, 1);
                }
                else
                {
                    // Add miter
                    var rx0 = p1.X - p1.Dmx * rw;
                    var ry0 = p1.Y - p1.Dmy * rw;

                    _renderCache.AddVertex(p1.X, p1.Y, 0.5f, 1);
                    _renderCache.AddVertex(p1.X - dlx0 * rw, p1.Y - dly0 * rw, ru, 1);
                    _renderCache.AddVertex(rx0, ry0, ru, 1);
                    _renderCache.AddVertex(rx0, ry0, ru, 1);
                    _renderCache.AddVertex(p1.X, p1.Y, 0.5f, 1);
                    _renderCache.AddVertex(p1.X - dlx1 * rw, p1.Y - dly1 * rw, ru, 1);
                }

                _renderCache.AddVertex(bounds.Max.X, bounds.Max.Y, lu, 1);
                _renderCache.AddVertex(p1.X - dlx1 * rw, p1.Y - dly1 * rw, ru, 1);
            }
            else
            {
                // Right side join
                var bounds = __chooseBevel(p1.Flags & (byte)PointFlags.InnerBevel, p0, p1, -rw);

                _renderCache.AddVertex(p1.X + dlx0 * lw, p1.Y + dly0 * lw, lu, 1);
                _renderCache.AddVertex(bounds.Min.X, bounds.Min.Y, ru, 1);

                if ((p1.Flags & (byte)PointFlags.Bevel) != 0)
                {
                    // Add bevel
                    _renderCache.AddVertex(p1.X + dlx0 * lw, p1.Y + dly0 * lw, lu, 1);
                    _renderCache.AddVertex(bounds.Min.X, bounds.Min.Y, ru, 1);
                    _renderCache.AddVertex(p1.X + dlx1 * lw, p1.Y + dly1 * lw, lu, 1);
                    _renderCache.AddVertex(bounds.Max.X, bounds.Max.Y, ru, 1);
                }
                else
                {
                    // Add miter
                    var lx0 = p1.X + p1.Dmx * lw;
                    var ly0 = p1.Y + p1.Dmy * lw;

                    _renderCache.AddVertex(p1.X + dlx0 * lw, p1.Y + dly0 * lw, lu, 1);
                    _renderCache.AddVertex(p1.X, p1.Y, 0.5f, 1);
                    _renderCache.AddVertex(lx0, ly0, lu, 1);
                    _renderCache.AddVertex(lx0, ly0, lu, 1);
                    _renderCache.AddVertex(p1.X + dlx1 * lw, p1.Y + dly1 * lw, lu, 1);
                    _renderCache.AddVertex(p1.X, p1.Y, 0.5f, 1);
                }

                _renderCache.AddVertex(p1.X + dlx1 * lw, p1.Y + dly1 * lw, lu, 1);
                _renderCache.AddVertex(bounds.Max.X, bounds.Max.Y, ru, 1);
            }
        }

        /// <summary>
        /// Creates a butt cap at the start of a stroke
        /// </summary>
        private void __buttCapStart(CanvasPoint p, float dx, float dy, float w, float d, float aa, float u0, float u1)
        {
            var px = p.X - dx * d;
            var py = p.Y - dy * d;
            var dlx = dy;
            var dly = -dx;

            _renderCache.AddVertex(px + dlx * w - dx * aa, py + dly * w - dy * aa, u0, 0);
            _renderCache.AddVertex(px - dlx * w - dx * aa, py - dly * w - dy * aa, u1, 0);
            _renderCache.AddVertex(px + dlx * w, py + dly * w, u0, 1);
            _renderCache.AddVertex(px - dlx * w, py - dly * w, u1, 1);
        }

        /// <summary>
        /// Creates a butt cap at the end of a stroke
        /// </summary>
        private void __buttCapEnd(CanvasPoint p, float dx, float dy, float w, float d, float aa, float u0, float u1)
        {
            var px = p.X + dx * d;
            var py = p.Y + dy * d;
            var dlx = dy;
            var dly = -dx;

            _renderCache.AddVertex(px + dlx * w, py + dly * w, u0, 1);
            _renderCache.AddVertex(px - dlx * w, py - dly * w, u1, 1);
            _renderCache.AddVertex(px + dlx * w + dx * aa, py + dly * w + dy * aa, u0, 0);
            _renderCache.AddVertex(px - dlx * w + dx * aa, py - dly * w + dy * aa, u1, 0);
        }

        /// <summary>
        /// Creates a round cap at the start of a stroke
        /// </summary>
        private void __roundCapStart(CanvasPoint p, float dx, float dy, float w, int ncap, float aa, float u0, float u1)
        {
            var px = p.X;
            var py = p.Y;
            var dlx = dy;
            var dly = -dx;

            // Add semi-circle segments
            for (var i = 0; i < ncap; i++)
            {
                var a = (i / (float)(ncap - 1) * MathF.PI);
                var ax = MathF.Cos(a) * w;
                var ay = MathF.Sin(a) * w;
                _renderCache.AddVertex(px - dlx * ax - dx * ay, py - dly * ax - dy * ay, u0, 1);
                _renderCache.AddVertex(px, py, 0.5f, 1);
            }

            _renderCache.AddVertex(px + dlx * w, py + dly * w, u0, 1);
            _renderCache.AddVertex(px - dlx * w, py - dly * w, u1, 1);
        }

        /// <summary>
        /// Creates a round cap at the end of a stroke
        /// </summary>
        private void __roundCapEnd(CanvasPoint p, float dx, float dy, float w, int ncap, float aa, float u0, float u1)
        {
            var px = p.X;
            var py = p.Y;
            var dlx = dy;
            var dly = -dx;

            _renderCache.AddVertex(px + dlx * w, py + dly * w, u0, 1);
            _renderCache.AddVertex(px - dlx * w, py - dly * w, u1, 1);

            // Add semi-circle segments
            for (var i = 0; i < ncap; i++)
            {
                var a = (i / (float)(ncap - 1) * MathF.PI);
                var ax = MathF.Cos(a) * w;
                var ay = MathF.Sin(a) * w;
                _renderCache.AddVertex(px, py, 0.5f, 1);
                _renderCache.AddVertex(px - dlx * ax + dx * ay, py - dly * ax + dy * ay, u0, 1);
            }
        }
        #endregion

        #endregion
    }
}