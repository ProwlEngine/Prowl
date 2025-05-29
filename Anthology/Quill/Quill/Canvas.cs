using FontStashSharp;
using Prowl.Vector;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using Prowl.Quill.External.LibTessDotNet;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Runtime.CompilerServices;

namespace Prowl.Quill
{
    public enum BrushType
    {
        None = 0,
        Linear = 1,
        Radial = 2,
        Box = 3
    }

    public enum WindingMode
    {
        OddEven,
        NonZero
    }

    public struct DrawCall
    {
        public int ElementCount;
        public object Texture;
        public Brush Brush;
        internal Transform2D scissor;
        internal Vector2 scissorExtent;

        public void GetScissor(out Matrix4x4 matrix, out Vector2 extent)
        {
            if (scissorExtent.x < -0.5f || scissorExtent.y < -0.5f)
            {
                // Invalid scissor - disable it
                matrix = new Matrix4x4();
                extent = new Vector2(1, 1);
            }
            else
            {
                // Set up scissor transform and dimensions
                matrix = scissor.Inverse().ToMatrix4x4();
                extent = new Vector2(scissorExtent.x, scissorExtent.y);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct Vertex
    {
        public static int SizeInBytes => Marshal.SizeOf<Vertex>();

        public Vector2 Position => new Vector2(x, y);
        public Vector2 UV => new Vector2(u, v);
        public Color Color => Color.FromArgb(a, r, g, b);


        public float x;
        public float y;

        public float u;
        public float v;

        public byte r;
        public byte g;
        public byte b;
        public byte a;

        public Vertex(Vector2 position, Vector2 UV, Color color)
        {
            x = (float)position.x;
            y = (float)position.y;
            u = (float)UV.x;
            v = (float)UV.y;
            r = color.R;
            g = color.G;
            b = color.B;
            a = color.A;
        }
    }

    public struct Brush
    {
        public Matrix4x4 BrushMatrix => Transform.Inverse().ToMatrix4x4();

        public Transform2D Transform;

        public BrushType Type;
        public Color Color1;
        public Color Color2;
        public Vector2 Point1;
        public Vector2 Point2; // or radius for radial, half-size for box
        public double CornerRadii;
        public double Feather;

        internal bool EqualsOther(Brush gradient)
        {
            return Type == gradient.Type &&
                   Color1 == gradient.Color1 &&
                   Color2 == gradient.Color2 &&
                   Point1 == gradient.Point1 &&
                   Point2 == gradient.Point2 &&
                   CornerRadii == gradient.CornerRadii &&
                   Feather == gradient.Feather &&
                   Transform == gradient.Transform;
        }
    }

    internal struct ProwlCanvasState
    {
        internal Transform2D transform;

        internal Color strokeColor;
        internal JointStyle strokeJoint;
        internal EndCapStyle strokeStartCap;
        internal EndCapStyle strokeEndCap;
        internal double strokeWidth;
        internal double strokeScale;
        internal double miterLimit;
        internal double tess_tol;
        internal double roundingMinDistance;

        internal object? texture;
        internal Transform2D scissor;
        internal Vector2 scissorExtent;
        internal Brush brush;


        internal Color fillColor;
        internal WindingMode fillMode;

        internal void Reset()
        {
            transform = Transform2D.Identity;
            strokeColor = Color.FromArgb(255, 0, 0, 0); // Default stroke color (black)
            strokeJoint = JointStyle.Bevel; // Default joint style
            strokeStartCap = EndCapStyle.Butt; // Default start cap style
            strokeEndCap = EndCapStyle.Butt; // Default end cap style
            strokeWidth = 1f; // Default stroke width
            strokeScale = 1f; // Default stroke scale
            miterLimit = 4; // Default miter limit
            tess_tol = 0.5; // Default tessellation tolerance
            roundingMinDistance = 3; //Default _state.roundingMinDistance
            texture = null;
            scissor.Zero();
            scissorExtent.x = -1.0f;
            scissorExtent.y = -1.0f;
            brush = new Brush();
            brush.Transform = Transform2D.Identity;
            fillColor = Color.FromArgb(255, 0, 0, 0); // Default fill color (black)
            fillMode = WindingMode.OddEven; // Default winding mode
        }
    }

    public partial class Canvas
    {
        internal class SubPath
        {
            internal List<Vector2> Points { get; }
            internal bool IsClosed { get; }

            public SubPath(List<Vector2> points, bool isClosed)
            {
                Points = points;
                IsClosed = isClosed;
            }
        }

        public IReadOnlyList<DrawCall> DrawCalls => _drawCalls.Where(d => d.ElementCount != 0).ToList();
        public IReadOnlyList<uint> Indices => _indices.AsReadOnly();
        public IReadOnlyList<Vertex> Vertices => _vertices.AsReadOnly();
        public Vector2 CurrentPoint => _currentSubPath != null && _currentSubPath.Points.Count > 0 ? CurrentPointInternal : Vector2.zero;

        internal Vector2 CurrentPointInternal => _currentSubPath.Points[_currentSubPath.Points.Count - 1];
        internal ICanvasRenderer _renderer;

        internal List<DrawCall> _drawCalls = new List<DrawCall>();
        internal Stack<object> _textureStack = new Stack<object>();

        internal List<uint> _indices = new List<uint>();
        internal List<Vertex> _vertices = new List<Vertex>();

        private readonly List<SubPath> _subPaths = new List<SubPath>();
        private SubPath? _currentSubPath = null;
        private bool _isPathOpen = false;

        private readonly Stack<ProwlCanvasState> _savedStates = new Stack<ProwlCanvasState>();
        private ProwlCanvasState _state;
        private double _globalAlpha;

        private TextRenderer _fontStashRenderer;

        private double _devicePixelRatio = 1.0f;
        private double _pixelWidth = 1.0f;
        private double _pixelHalf = 0.5f;
        public double DevicePixelRatio
        {
            get => _devicePixelRatio;
            set
            {
                if (value <= 0)
                    throw new ArgumentOutOfRangeException(nameof(value), "Device pixel ratio must be greater than zero.");
                _devicePixelRatio = value;
                _pixelWidth = 1.0f / value;
                _pixelHalf = _pixelWidth * 0.5f;
            }
        }

        public Canvas(ICanvasRenderer renderer)
        {
            if (renderer == null)
                throw new ArgumentNullException(nameof(renderer), "Renderer cannot be null.");

            _renderer = renderer;
            _fontStashRenderer = new TextRenderer(this);
            Clear();
        }

        public void Clear()
        {
            _drawCalls.Clear();
            _textureStack.Clear();
            AddDrawCmd();

            _indices.Clear();
            _vertices.Clear();

            _savedStates.Clear();
            _state = new ProwlCanvasState();
            _state.Reset();

            _subPaths.Clear();
            _currentSubPath = null;
            _isPathOpen = true;

            _globalAlpha = 1f;
        }


        #region State

        public void SaveState() => _savedStates.Push(_state);
        public void RestoreState() => _state = _savedStates.Pop();
        public void ResetState() => _state.Reset();

        public void SetStrokeColor(Color color) => _state.strokeColor = color;
        public void SetStrokeJoint(JointStyle joint) => _state.strokeJoint = joint;
        public void SetStrokeCap(EndCapStyle cap)
        {
            _state.strokeStartCap = cap;
            _state.strokeEndCap = cap;
        }
        public void SetStrokeStartCap(EndCapStyle cap) => _state.strokeStartCap = cap;
        public void SetStrokeEndCap(EndCapStyle cap) => _state.strokeEndCap = cap;
        public void SetStrokeWidth(double width = 2f) => _state.strokeWidth = width;
        public void SetStrokeScale(double scale) => _state.strokeScale = scale;
        public void SetMiterLimit(double limit = 4) => _state.miterLimit = limit;
        public void SetTessellationTolerance(double tolerance = 0.5) => _state.tess_tol = tolerance;
        public void SetRoundingMinDistance(double distance = 3) => _state.roundingMinDistance = distance;
        public void SetTexture(object texture) => _state.texture = texture;
        public void SetLinearBrush(double x1, double y1, double x2, double y2, Color color1, Color color2)
        {
            // Premultiply
            color1 = Color.FromArgb(
                (byte)(color1.A),
                (byte)(color1.R * (color1.A / 255f)),
                (byte)(color1.G * (color1.A / 255f)),
                (byte)(color1.B * (color1.A / 255f)));
            color2 = Color.FromArgb(
                (byte)(color2.A),
                (byte)(color2.R * (color2.A / 255f)),
                (byte)(color2.G * (color2.A / 255f)),
                (byte)(color2.B * (color2.A / 255f)));

            _state.brush.Type = BrushType.Linear;
            _state.brush.Color1 = color1;
            _state.brush.Color2 = color2;
            _state.brush.Point1 = new Vector2(x1, y1);
            _state.brush.Point2 = new Vector2(x2, y2);

            _state.brush.Transform = _state.transform;
        }
        public void SetRadialBrush(double centerX, double centerY, double innerRadius, double outerRadius, Color innerColor, Color outerColor)
        {
            // Premultiply
            innerColor = Color.FromArgb(
                (byte)(innerColor.A),
                (byte)(innerColor.R * (innerColor.A / 255f)),
                (byte)(innerColor.G * (innerColor.A / 255f)),
                (byte)(innerColor.B * (innerColor.A / 255f)));
            outerColor = Color.FromArgb(
                (byte)(outerColor.A),
                (byte)(outerColor.R * (outerColor.A / 255f)),
                (byte)(outerColor.G * (outerColor.A / 255f)),
                (byte)(outerColor.B * (outerColor.A / 255f)));

            _state.brush.Type = BrushType.Radial;
            _state.brush.Color1 = innerColor;
            _state.brush.Color2 = outerColor;
            _state.brush.Point1 = new Vector2(centerX, centerY);
            _state.brush.Point2 = new Vector2(innerRadius, outerRadius); // Store radius

            _state.brush.Transform = _state.transform;
        }
        public void SetBoxBrush(double centerX, double centerY, double width, double height, float radi, float feather, Color innerColor, Color outerColor)
        {
            // Premultiply
            innerColor = Color.FromArgb(
                (byte)(innerColor.A),
                (byte)(innerColor.R * (innerColor.A / 255f)),
                (byte)(innerColor.G * (innerColor.A / 255f)),
                (byte)(innerColor.B * (innerColor.A / 255f)));
            outerColor = Color.FromArgb(
                (byte)(outerColor.A),
                (byte)(outerColor.R * (outerColor.A / 255f)),
                (byte)(outerColor.G * (outerColor.A / 255f)),
                (byte)(outerColor.B * (outerColor.A / 255f)));

            _state.brush.Type = BrushType.Box;
            _state.brush.Color1 = innerColor;
            _state.brush.Color2 = outerColor;
            _state.brush.Point1 = new Vector2(centerX, centerY);
            _state.brush.Point2 = new Vector2(width / 2, height / 2); // Store half-size
            _state.brush.CornerRadii = radi;
            _state.brush.Feather = feather;

            _state.brush.Transform = _state.transform;
        }
        public void ClearGradient()
        {
            _state.brush.Type = BrushType.None;
        }
        public void SetFillColor(Color color) => _state.fillColor = color;


        #region Scissor Methods
        /// <summary>
        /// Sets the scissor rectangle for clipping
        /// </summary>
        public void Scissor(double x, double y, double w, double h)
        {
            w = Math.Max(0.0, w);
            h = Math.Max(0.0, h);
            _state.scissor = Transform2D.CreateTranslation(x + w * 0.5, y + h * 0.5) * _state.transform;
            _state.scissorExtent.x = w * 0.5;
            _state.scissorExtent.y = h * 0.5;
        }

        /// <summary>
        /// Intersects the current scissor rectangle with another rectangle
        /// </summary>
        public void IntersectScissor(double x, double y, double w, double h)
        {
            if (_state.scissorExtent.x < 0)
            {
                Scissor(x, y, w, h);
                return;
            }

            var pxform = _state.scissor;
            var ex = _state.scissorExtent.x;
            var ey = _state.scissorExtent.y;
            var invxorm = _state.transform.Inverse();
            pxform.Multiply(ref invxorm);

            // Calculate extent in current transform space
            var tex = ex * Math.Abs(pxform.A) + ey * Math.Abs(pxform.C);
            var tey = ex * Math.Abs(pxform.B) + ey * Math.Abs(pxform.D);

            // Find the intersection
            var rect = IntersectionOfRects(pxform.E - tex, pxform.F - tey, tex * 2, tey * 2, x, y, w, h);
            Scissor(rect.x, rect.y, rect.width, rect.height);
        }

        /// <summary>
        /// Calculates the intersection of two rectangles
        /// </summary>
        private static Rect IntersectionOfRects(double ax, double ay, double aw, double ah, double bx, double by, double bw, double bh)
        {
            var minx = Math.Max(ax, bx);
            var miny = Math.Max(ay, by);
            var maxx = Math.Min(ax + aw, bx + bw);
            var maxy = Math.Min(ay + ah, by + bh);

            return new Rect(minx, miny, Math.Max(0.0, maxx - minx), Math.Max(0.0, maxy - miny));
        }

        /// <summary>
        /// Resets the scissor rectangle
        /// </summary>
        public void ResetScissor()
        {
            _state.scissor.Zero();
            _state.scissorExtent.x = -1.0f;
            _state.scissorExtent.y = -1.0f;
        }
        #endregion

        // Globals
        public void SetGlobalAlpha(double alpha) => _globalAlpha = alpha;

        #endregion

        #region Transformation

        public void TransformBy(Transform2D t) => _state.transform.Premultiply(ref t);
        public void ResetTransform() => _state.transform = Transform2D.Identity;
        public void CurrentTransform(Transform2D xform) => _state.transform = xform;
        public Vector2 TransformPoint(Vector2 point) => _state.transform.TransformPoint(point);

        public Transform2D GetTransform() => _state.transform;

        #endregion

        #region Draw Calls

        public void AddDrawCmd() => _drawCalls.Add(new DrawCall());

        public void AddVertex(Vertex vertex)
        {
            if (_drawCalls.Count == 0)
                return;

            if (_globalAlpha != 1.0f)
                vertex.a = (byte)(vertex.a * _globalAlpha);

            // Premultiply
            if (vertex.a != 255)
            {
                vertex.r = (byte)(vertex.r * (vertex.a / 255f));
                vertex.g = (byte)(vertex.g * (vertex.a / 255f));
                vertex.b = (byte)(vertex.b * (vertex.a / 255f));
            }


            // Add the vertex to the list
            _vertices.Add(vertex);
        }

        public void AddTriangle() => AddTriangle(_vertices.Count - 3, _vertices.Count - 2, _vertices.Count - 1);
        public void AddTriangle(int v1, int v2, int v3) => AddTriangle((uint)v1, (uint)v2, (uint)v3);
        public void AddTriangle(uint v1, uint v2, uint v3)
        {
            if (_drawCalls.Count == 0)
                return;

            // Add the triangle indices to the list
            _indices.Add(v1);
            _indices.Add(v2);
            _indices.Add(v3);

            AddTriangleCount(1);
        }

        private void AddTriangleCount(int count)
        {
            if (_drawCalls.Count == 0)
                return;

            DrawCall lastDrawCall = _drawCalls[_drawCalls.Count - 1];

            bool isDrawStateSame = lastDrawCall.Texture == _state.texture &&
                lastDrawCall.scissorExtent == _state.scissorExtent &&
                lastDrawCall.scissor == _state.scissor &&
                lastDrawCall.Brush.EqualsOther(_state.brush);

            if (!isDrawStateSame)
            {
                // If the texture or scissor state has changed, add a new draw call
                AddDrawCmd();
                lastDrawCall = _drawCalls[_drawCalls.Count - 1];
                lastDrawCall.Texture = _state.texture;
                lastDrawCall.scissor = _state.scissor;
                lastDrawCall.scissorExtent = _state.scissorExtent;
                lastDrawCall.Brush = _state.brush;
            }

            lastDrawCall.ElementCount += count * 3;
            _drawCalls[_drawCalls.Count - 1] = lastDrawCall;
        }

        public void Render()
        {
            _renderer.RenderCalls(this, _drawCalls);
        }

        #endregion

        #region Path

        /// <summary>
        /// Begins a new path by emptying the list of sub-paths. Call this method when you want to create a new path.
        /// </summary>
        /// <remarks>
        /// When you call <see cref="BeginPath"/>, all previous paths are cleared and a new path is started.
        /// </remarks>
        public void BeginPath()
        {
            _subPaths.Clear();
            _currentSubPath = null;
            _isPathOpen = true;
        }

        /// <summary>
        /// Moves the current position to the specified point without drawing a line.
        /// </summary>
        /// <param name="x">The x-coordinate of the point to move to.</param>
        /// <param name="y">The y-coordinate of the point to move to.</param>
        /// <remarks>
        /// This method moves the "pen" to the specified point without drawing anything.
        /// It begins a new sub-path if one doesn't already exist. Subsequent calls to
        /// <see cref="LineTo"/> will draw lines from this position.
        /// </remarks>
        public void MoveTo(double x, double y)
        {
            if (!_isPathOpen)
                BeginPath();

            _currentSubPath = new SubPath(new List<Vector2>(), false);
            _currentSubPath.Points.Add(new Vector2(x, y));
            _subPaths.Add(_currentSubPath);
        }

        /// <summary>
        /// Draws a line from the current position to the specified point.
        /// </summary>
        /// <param name="x">The x-coordinate of the ending point.</param>
        /// <param name="y">The y-coordinate of the ending point.</param>
        /// <remarks>
        /// This method draws a straight line from the current position to the specified position.
        /// After the line is drawn, the current position is updated to the ending point.
        /// If no position has been set previously, this method act as <see cref="MoveTo"/> with the specified coordinates.
        /// </remarks>
        public void LineTo(double x, double y)
        {
            if (_currentSubPath == null)
            {
                // HTML Canvas spec: If no current point exists, it's equivalent to a moveTo(x, y)
                MoveTo(x, y);
            }
            else
            {
                _currentSubPath.Points.Add(new Vector2(x, y));
            }
        }

        /// <summary>
        /// Closes the current path by drawing a straight line from the current position to the starting point.
        /// </summary>
        /// <remarks>
        /// This method attempts to draw a line from the current position to the first point in the current path.
        /// If the path contains fewer than two points, no action is taken.
        /// After closing the path, the current position is updated to the starting point of the path.
        /// </remarks>
        public void ClosePath()
        {
            if (_currentSubPath != null && _currentSubPath.Points.Count >= 2)
            {
                // Move to the first point of the current subpath to start a new one
                Vector2 firstPoint = _currentSubPath.Points[0];
                //MoveTo(firstPoint.x, firstPoint.y);
                LineTo(firstPoint.x, firstPoint.y);
            }
        }

        /// <summary>
        /// Sets the solidity order for the currently active path.
        /// </summary>
        public void SetSolidity(WindingMode solidity) => _state.fillMode = solidity;

        /// <summary>
        /// Adds an arc to the current path.
        /// </summary>
        /// <param name="x">The x-coordinate of the center of the arc.</param>
        /// <param name="y">The y-coordinate of the center of the arc.</param>
        /// <param name="radius">The radius of the arc.</param>
        /// <param name="startAngle">The starting angle of the arc, in radians.</param>
        /// <param name="endAngle">The ending angle of the arc, in radians.</param>
        /// <param name="counterclockwise">If true, draws the arc counter-clockwise; otherwise, draws it clockwise.</param>
        /// <remarks>
        /// This method adds an arc to the current path, centered at the specified position with the given radius.
        /// The arc starts at startAngle and ends at endAngle, measured in radians.
        /// By default, the arc is drawn clockwise, but can be drawn counter-clockwise by setting the counterclockwise parameter to true.
        /// If no path has been started, this method will first move to the starting point of the arc.
        /// </remarks>
        public void Arc(double x, double y, double radius, double startAngle, double endAngle, bool counterclockwise = false)
        {
            Vector2 center = new Vector2(x, y);

            // Calculate number of segments based on radius size
            double distance = CalculateArcLength(radius, startAngle, endAngle);
            int segments = Math.Max(1, (int)Math.Ceiling(distance / _state.roundingMinDistance));

            if (counterclockwise && startAngle < endAngle)
            {
                startAngle += Math.PI * 2;
            }
            else if (!counterclockwise && startAngle > endAngle)
            {
                endAngle += Math.PI * 2;
            }

            double step = counterclockwise ?
                (startAngle - endAngle) / segments :
                (endAngle - startAngle) / segments;

            // If no path has started yet, move to the first point of the arc
            if (!_isPathOpen)
            {
                double firstX = x + Math.Cos(startAngle) * radius;
                double firstY = y + Math.Sin(startAngle) * radius;
                MoveTo(firstX, firstY);
            }

            double startX = x + Math.Cos(startAngle) * radius;
            double startY = y + Math.Sin(startAngle) * radius;
            LineTo(startX, startY);

            // Add arc points
            for (int i = 1; i <= segments; i++)
            {
                double angle = counterclockwise ?
                    startAngle - i * step :
                    startAngle + i * step;

                double pointX = x + Math.Cos(angle) * radius;
                double pointY = y + Math.Sin(angle) * radius;

                LineTo(pointX, pointY);
            }
        }

        /// <summary>
        /// Adds an arc to the path with the specified control points and radius.
        /// </summary>
        /// <param name="x1">The x-coordinate of the first control point.</param>
        /// <param name="y1">The y-coordinate of the first control point.</param>
        /// <param name="x2">The x-coordinate of the second control point.</param>
        /// <param name="y2">The y-coordinate of the second control point.</param>
        /// <param name="radius">The radius of the arc.</param>
        /// <remarks>
        /// This method creates an arc that is tangent to both the line from the current position to (x1,y1)
        /// and the line from (x1,y1) to (x2,y2) with the specified radius.
        /// If the path has not been started, this method will move to the position (x1,y1).
        /// </remarks>
        public void ArcTo(double x1, double y1, double x2, double y2, double radius)
        {
            if (!_isPathOpen)
            {
                MoveTo(x1, y1);
                return;
            }

            Vector2 p0 = CurrentPointInternal;
            Vector2 p1 = new Vector2(x1, y1);
            Vector2 p2 = new Vector2(x2, y2);

            // Calculate direction vectors
            Vector2 v1 = p0 - p1;
            Vector2 v2 = p2 - p1;

            // Normalize vectors
            double len1 = Math.Sqrt(v1.x * v1.x + v1.y * v1.y);
            double len2 = Math.Sqrt(v2.x * v2.x + v2.y * v2.y);

            if (len1 < 0.0001 || len2 < 0.0001)
            {
                LineTo(x1, y1);
                return;
            }

            v1 /= len1;
            v2 /= len2;

            // Calculate angle and tangent points
            double angle = Math.Acos(v1.x * v2.x + v1.y * v2.y);
            double tan = radius * Math.Tan(angle / 2);

            if (double.IsNaN(tan) || tan < 0.0001)
            {
                LineTo(x1, y1);
                return;
            }

            // Calculate tangent points
            Vector2 t1 = p1 + v1 * tan;
            Vector2 t2 = p1 + v2 * tan;

            // Draw line to first tangent point
            LineTo(t1.x, t1.y);

            // Calculate arc center and angles
            double d = radius / Math.Sin(angle / 2);
            Vector2 middle = (v1 + v2);
            middle /= Math.Sqrt(middle.x * middle.x + middle.y * middle.y);
            Vector2 center = p1 + middle * d;

            // Calculate angles for the arc
            Vector2 a1 = t1 - center;
            Vector2 a2 = t2 - center;
            double startAngle = Math.Atan2(a1.y, a1.x);
            double endAngle = Math.Atan2(a2.y, a2.x);

            // Draw the arc
            Arc(center.x, center.y, radius, startAngle, endAngle, (v1.x * v2.y - v1.y * v2.x) < 0);
        }

        /// <summary>
        /// Adds an elliptical arc to the path with the specified control points and radius.
        /// </summary>
        /// <param name="rx">The x-axis radius of the ellipse.</param>
        /// <param name="ry">The y-axis radius of the ellipse.</param>
        /// <param name="xAxisRotation">The x-coordinate of the second control point.</param>
        /// <param name="largeArcFlag">If largeArcFlag is '1', then one of the two larger arc sweeps will be chosen; otherwise, if largeArcFlag is '0', one of the smaller arc sweeps will be chosen.</param>
        /// <param name="sweepFlag">If sweepFlag is '1', then the arc will be drawn in a "positive-angle" direction. A value of 0 causes the arc to be drawn in a "negative-angle" direction</param>
        /// <param name="x">The x-coordinate of the endpoint.</param>
        /// <param name="y">The y-coordinate of the endpoint.</param>
        /// <remarks>
        /// This method creates an elliptical arc with radii (rx,ry) from current point to (x_end,y_end)
        /// </remarks>
        public void EllipticalArcTo(double rx, double ry, double xAxisRotationDegrees, bool largeArcFlag, bool sweepFlag, double x_end, double y_end)
        {
            double x = CurrentPointInternal.x;
            double y = CurrentPointInternal.y;

            // Ensure radii are positive
            double rx_abs = Math.Abs(rx);
            double ry_abs = Math.Abs(ry);

            // If rx or ry is zero, or if start and end points are the same, treat as a line segment (or do nothing if start=end)
            if (rx_abs == 0 || ry_abs == 0)
            {
                LineTo(x_end, y_end);
                return;
            }

            if (x == x_end && y == y_end)
            {
                // No arc to draw, points are identical
                return;
            }

            double phi = xAxisRotationDegrees * (Math.PI / 180.0); // Convert degrees to radians
            double cosPhi = Math.Cos(phi);
            double sinPhi = Math.Sin(phi);

            // Step 1: Compute (x1', y1') - coordinates of p1 transformed relative to p_end
            double dx_half = (x - x_end) / 2.0;
            double dy_half = (y - y_end) / 2.0;

            double x1_prime = cosPhi * dx_half + sinPhi * dy_half;
            double y1_prime = -sinPhi * dx_half + cosPhi * dy_half;

            // Step 2: Ensure radii are large enough
            double rx_sq = rx_abs * rx_abs;
            double ry_sq = ry_abs * ry_abs;
            double x1_prime_sq = x1_prime * x1_prime;
            double y1_prime_sq = y1_prime * y1_prime;

            double radii_check = (x1_prime_sq / rx_sq) + (y1_prime_sq / ry_sq);
            if (radii_check > 1.0)
            {
                double scaleFactor = Math.Sqrt(radii_check);
                rx_abs *= scaleFactor;
                ry_abs *= scaleFactor;
                rx_sq = rx_abs * rx_abs; // Update squared radii
                ry_sq = ry_abs * ry_abs;
            }

            // Step 3: Compute (cx', cy') - center of ellipse in transformed (prime) coordinates
            double term_numerator = (rx_sq * ry_sq) - (rx_sq * y1_prime_sq) - (ry_sq * x1_prime_sq);
            double term_denominator = (rx_sq * y1_prime_sq) + (ry_sq * x1_prime_sq);

            double term_sqrt_arg = 0;
            if (term_denominator != 0) // Avoid division by zero
                term_sqrt_arg = term_numerator / term_denominator;

            term_sqrt_arg = Math.Max(0, term_sqrt_arg); // Clamp to avoid issues with floating point inaccuracies

            double sign_coef = (largeArcFlag == sweepFlag) ? -1.0 : 1.0;
            double coef = sign_coef * Math.Sqrt(term_sqrt_arg);

            double cx_prime = coef * ((rx_abs * y1_prime) / ry_abs);
            double cy_prime = coef * -((ry_abs * x1_prime) / rx_abs);

            // Step 4: Compute (cx, cy) - center of ellipse in original coordinates
            double x_mid = (x + x_end) / 2.0;
            double y_mid = (y + y_end) / 2.0;

            double cx = cosPhi * cx_prime - sinPhi * cy_prime + x_mid;
            double cy = sinPhi * cx_prime + cosPhi * cy_prime + y_mid;

            // Step 5: Compute startAngle (theta1) and extentAngle (deltaTheta)
            double vec_start_x = (x1_prime - cx_prime) / rx_abs;
            double vec_start_y = (y1_prime - cy_prime) / ry_abs;
            double vec_end_x = (-x1_prime - cx_prime) / rx_abs;
            double vec_end_y = (-y1_prime - cy_prime) / ry_abs;

            double theta1 = CalculateVectorAngle(1, 0, vec_start_x, vec_start_y);
            double deltaTheta = CalculateVectorAngle(vec_start_x, vec_start_y, vec_end_x, vec_end_y);

            if (!sweepFlag && deltaTheta > 0)
            {
                deltaTheta -= 2 * Math.PI;
            }
            else if (sweepFlag && deltaTheta < 0)
            {
                deltaTheta += 2 * Math.PI;
            }

            // Step 6: Draw the arc using line segments
            double estimatedArcLength = Math.Abs(deltaTheta) * (rx_abs + ry_abs) / 2.0;
            int segments = Math.Max(1, (int)Math.Ceiling(estimatedArcLength / _state.roundingMinDistance));
            if (Math.Abs(deltaTheta) > 1e-9 && segments == 0) segments = 1; // Ensure at least one segment for tiny arcs

            for (int i = 1; i <= segments; i++)
            {
                double t = (double)i / segments;
                double angle = theta1 + deltaTheta * t;

                double cosAngle = Math.Cos(angle);
                double sinAngle = Math.Sin(angle);

                double ellipse_pt_x_prime = rx_abs * cosAngle;
                double ellipse_pt_y_prime = ry_abs * sinAngle;

                double final_x = cosPhi * ellipse_pt_x_prime - sinPhi * ellipse_pt_y_prime + cx;
                double final_y = sinPhi * ellipse_pt_x_prime + cosPhi * ellipse_pt_y_prime + cy;

                if (i == segments)
                {
                    LineTo(x_end, y_end); // Ensure final point is exact
                }
                else
                {
                    LineTo(final_x, final_y);
                }
            }
        }

        /// <summary>
        /// Adds a cubic Bézier curve to the path from the current position to the specified end point.
        /// </summary>
        /// <param name="cp1x">The x-coordinate of the first control point.</param>
        /// <param name="cp1y">The y-coordinate of the first control point.</param>
        /// <param name="cp2x">The x-coordinate of the second control point.</param>
        /// <param name="cp2y">The y-coordinate of the second control point.</param>
        /// <param name="x">The x-coordinate of the end point.</param>
        /// <param name="y">The y-coordinate of the end point.</param>
        /// <remarks>
        /// This method adds a cubic Bézier curve to the current path, using the specified control points.
        /// The curve starts at the current position and ends at (x,y).
        /// If no current position exists, this method will move to the end point without drawing a curve.
        /// </remarks>
        public void BezierCurveTo(double cp1x, double cp1y, double cp2x, double cp2y, double x, double y)
        {
            if (!_isPathOpen)
            {
                MoveTo(x, y);
                return;
            }

            //Vector2 p1 = _currentSubPath!.Points[^1];
            Vector2 p1 = CurrentPointInternal;
            Vector2 p2 = new Vector2(cp1x, cp1y);
            Vector2 p3 = new Vector2(cp2x, cp2y);
            Vector2 p4 = new Vector2(x, y);

            PathBezierToCasteljau(p1.x, p1.y, p2.x, p2.y, p3.x, p3.y, p4.x, p4.y, _state.tess_tol, 0);
        }

        private void PathBezierToCasteljau(double x1, double y1, double x2, double y2, double x3, double y3, double x4, double y4, double tess_tol, int level)
        {
            double dx = x4 - x1;
            double dy = y4 - y1;
            double d2 = (x2 - x4) * dy - (y2 - y4) * dx;
            double d3 = (x3 - x4) * dy - (y3 - y4) * dx;

            d2 = d2 >= 0 ? d2 : -d2;
            d3 = d3 >= 0 ? d3 : -d3;
            if ((d2 + d3) * (d2 + d3) < tess_tol * (dx * dx + dy * dy))
            {
                _currentSubPath.Points.Add(new Vector2(x4, y4));
            }
            else if (level < 10)
            {
                double x12 = (x1 + x2) * 0.5f, y12 = (y1 + y2) * 0.5f;
                double x23 = (x2 + x3) * 0.5f, y23 = (y2 + y3) * 0.5f;
                double x34 = (x3 + x4) * 0.5f, y34 = (y3 + y4) * 0.5f;
                double x123 = (x12 + x23) * 0.5f, y123 = (y12 + y23) * 0.5f;
                double x234 = (x23 + x34) * 0.5f, y234 = (y23 + y34) * 0.5f;
                double x1234 = (x123 + x234) * 0.5f, y1234 = (y123 + y234) * 0.5f;

                PathBezierToCasteljau(x1, y1, x12, y12, x123, y123, x1234, y1234, tess_tol, level + 1);
                PathBezierToCasteljau(x1234, y1234, x234, y234, x34, y34, x4, y4, tess_tol, level + 1);
            }
        }

        /// <summary>
        /// Adds a quadratic Bézier curve to the path from the current position to the specified end point.
        /// </summary>
        /// <param name="cpx">The x-coordinate of the control point.</param>
        /// <param name="cpy">The y-coordinate of the control point.</param>
        /// <param name="x">The x-coordinate of the end point.</param>
        /// <param name="y">The y-coordinate of the end point.</param>
        /// <remarks>
        /// This method adds a quadratic Bézier curve to the current path, using the specified control point.
        /// The curve starts at the current position and ends at (x,y).
        /// If no current position exists, this method will move to the end point without drawing a curve.
        /// Internally, this method converts the quadratic Bézier curve to a cubic Bézier curve.
        /// </remarks>
        public void QuadraticCurveTo(double cpx, double cpy, double x, double y)
        {
            if (!_isPathOpen)
            {
                MoveTo(x, y);
                return;
            }

            Vector2 p1 = CurrentPointInternal;
            Vector2 p2 = new Vector2(cpx, cpy);
            Vector2 p3 = new Vector2(x, y);

            // Convert quadratic curve to cubic bezier
            double cp1x = p1.x + 2.0 / 3.0 * (p2.x - p1.x);
            double cp1y = p1.y + 2.0 / 3.0 * (p2.y - p1.y);
            double cp2x = p3.x + 2.0 / 3.0 * (p2.x - p3.x);
            double cp2y = p3.y + 2.0 / 3.0 * (p2.y - p3.y);

            BezierCurveTo(cp1x, cp1y, cp2x, cp2y, x, y);
        }

        #endregion

        public void Fill()
        {
            if (_subPaths.Count == 0)
                return;

            // Fill all sub-paths individually
            foreach (var subPath in _subPaths)
                FillSubPath(subPath);
        }

        public void FillComplexAA()
        {
            FillComplex();

            // Stroke with same color as Fill
            SaveState();
            SetStrokeColor(_state.fillColor);
            SetStrokeWidth(1);
            SetStrokeScale(1f);
            SetStrokeJoint(JointStyle.Bevel);
            SetStrokeCap(EndCapStyle.Butt);

            Stroke();

            RestoreState();
        }

        public void FillComplex()
        {
            if (_subPaths.Count == 0)
                return;

            var tess = new Tess();
            foreach (var path in _subPaths)
            {
                var copy = path.Points.ToArray();
                for (int i = 0; i < copy.Length; i++)
                    copy[i] = TransformPoint(copy[i]) + new Vector2(0.5, 0.5); // And offset by half a pixel to properly align it with Stroke()
                var points = copy.Select(v => new ContourVertex() { Position = new Vec3() { X = v.x, Y = v.y } }).ToArray();

                tess.AddContour(points, ContourOrientation.Original);
            }
            tess.Tessellate(_state.fillMode == WindingMode.OddEven ? WindingRule.EvenOdd : WindingRule.NonZero, ElementType.Polygons, 3);

            var indices = tess.Elements;
            var vertices = tess.Vertices;

            // Create vertices and triangles
            uint startVertexIndex = (uint)_vertices.Count;
            for (int i = 0; i < vertices.Length; i++)
            {
                var vertex = vertices[i];
                Vector2 pos = new Vector2(vertex.Position.X, vertex.Position.Y);
                AddVertex(new Vertex(pos, new Vector2(0.5, 0.5), _state.fillColor));
            }
            // Create triangles
            for (int i = 0; i < indices.Length; i += 3)
            {
                uint v1 = (uint)(startVertexIndex + indices[i]);
                uint v2 = (uint)(startVertexIndex + indices[i + 1]);
                uint v3 = (uint)(startVertexIndex + indices[i + 2]);
                AddTriangle(v1, v3, v2);
            }
        }


        private void FillSubPath(SubPath subPath)
        {
            if (subPath.Points.Count < 3)
                return;

            // Transform each point
            Vector2 center = Vector2.zero;
            var copy = subPath.Points.ToArray();
            for (int i = 0; i < copy.Length; i++)
            {
                var point = copy[i];
                point = TransformPoint(point) + new Vector2(0.5, 0.5); // And offset by half a pixel to properly center it with Stroke()
                center += point;
                copy[i] = point;
            }
            center /= copy.Length;

            // Store the starting index to reference _vertices
            uint startVertexIndex = (uint)_vertices.Count;

            // Add center vertex with UV at 0.5,0.5 (no AA, Since 0 or 1 in shader is considered edge of shape and get anti aliased)
            AddVertex(new Vertex(center, new Vector2(0.5f, 0.5f), _state.fillColor));

            // Generate vertices around the path
            int segments = copy.Length;
            for (int i = 0; i < segments; i++) // Edge vertices have UV at 0,0 for anti-aliasing
            {
                Vector2 dirToPoint = (copy[i] - center).normalized;
                AddVertex(new Vertex(copy[i] + (dirToPoint * _pixelWidth), new Vector2(0, 0), _state.fillColor));
            }

            // Create triangles (fan from center to edges)
            // Check orientation with just the first triangle
            uint centerIdx = (uint)startVertexIndex;
            uint first = (uint)(startVertexIndex + 1);
            uint second = (uint)(startVertexIndex + 2);

            Vector2 centerPos = _vertices[(int)centerIdx].Position;
            Vector2 firstPos = _vertices[(int)first].Position;
            Vector2 secondPos = _vertices[(int)second].Position;

            double cross = ((firstPos.x - centerPos.x) * (secondPos.y - centerPos.y)) -
                           ((firstPos.y - centerPos.y) * (secondPos.x - centerPos.x));

            bool clockwise = cross <= 0;

            // Use the determined orientation for all triangles
            for (int i = 0; i < segments; i++)
            {
                uint current = (uint)(startVertexIndex + 1 + i);
                uint next = (uint)(startVertexIndex + 1 + ((i + 1) % segments));

                if (clockwise)
                {
                    _indices.Add(centerIdx);
                    _indices.Add(current);
                    _indices.Add(next);
                }
                else
                {
                    _indices.Add(centerIdx);
                    _indices.Add(next);
                    _indices.Add(current);
                }

                //AddTriangleCount(1);
            }

            AddTriangleCount(segments);
        }

        public void Stroke()
        {
            if (_subPaths.Count == 0)
                return;

            // Stroke all sub-paths
            foreach (var subPath in _subPaths)
                StrokeSubPath(subPath);
        }

        private void StrokeSubPath(SubPath subPath)
        {
            if (subPath.Points.Count < 2)
                return;

            var copy = subPath.Points.ToArray();
            // Transform each point
            for (int i = 0; i < subPath.Points.Count; i++)
                subPath.Points[i] = TransformPoint(subPath.Points[i]);

            bool isClosed = subPath.IsClosed;
            var triangles = PolylineMesher.Create(subPath.Points, _state.strokeWidth * _state.strokeScale, _pixelWidth, _state.strokeColor, _state.strokeJoint, _state.miterLimit, false, _state.strokeStartCap, _state.strokeEndCap);

            // Store the starting index to reference _vertices
            uint startVertexIndex = (uint)_vertices.Count;
            foreach (var triangle in triangles)
            {
                var color = triangle.Color;

                // TODO: Calculate Normals and push each vertex out by _pixelWidth / 0.5; for AntiAliasing

                AddVertex(new Vertex(triangle.V1, triangle.UV1, color));
                AddVertex(new Vertex(triangle.V2, triangle.UV2, color));
                AddVertex(new Vertex(triangle.V3, triangle.UV3, color));
            }

            // Add triangle _indices
            for (uint i = 0; i < triangles.Count; i++)
            {
                _indices.Add(startVertexIndex + (i * 3));
                _indices.Add(startVertexIndex + (i * 3) + 1);
                _indices.Add(startVertexIndex + (i * 3) + 2);
                //AddTriangleCount(1);
            }

            AddTriangleCount(triangles.Count);

            // Reset the points to their original values
            for (int i = 0; i < subPath.Points.Count; i++)
                subPath.Points[i] = copy[i];
        }

        public void FillAndStroke()
        {
            Fill();
            Stroke();
        }

        #region Primitives (Path-Based)

        /// <summary>
        /// Creates a Closed Rect Path
        /// </summary>
        /// <param name="x">The x-coordinate of the top-left corner of the rectangle.</param>
        /// <param name="y">The y-coordinate of the top-left corner of the rectangle.</param>
        /// <param name="width">The width of the rectangle.</param>
        /// <param name="height">The height of the rectangle.</param>
        /// <param name="color">The color of the rectangle.</param>
        public void Rect(double x, double y, double width, double height)
        {
            if (width <= 0 || height <= 0)
                return;

            BeginPath();
            MoveTo(x, y);
            LineTo(x + width, y);
            LineTo(x + width, y + height);
            LineTo(x, y + height);
            ClosePath();
        }

        /// <summary>
        /// Creates a Closed Rounded Rect Path
        /// </summary>
        /// <param name="x">The x-coordinate of the top-left corner of the rectangle.</param>
        /// <param name="y">The y-coordinate of the top-left corner of the rectangle.</param>
        /// <param name="width">The width of the rectangle.</param>
        /// <param name="height">The height of the rectangle.</param>
        /// <param name="radius">The radius of the corners.</param>
        public void RoundedRect(double x, double y, double width, double height, double radius)
        {
            RoundedRect(x, y, width, height, radius, radius, radius, radius);
        }

        /// <summary>
        /// Creates a Closed Rounded Rect Path
        /// </summary>
        /// <param name="x">The x-coordinate of the top-left corner of the rectangle.</param>
        /// <param name="y">The y-coordinate of the top-left corner of the rectangle.</param>
        /// <param name="width">The width of the rectangle.</param>
        /// <param name="height">The height of the rectangle.</param>
        /// <param name="tlRadii">The radius of the top-left corner.</param>
        /// <param name="trRadii">The radius of the top-right corner.</param>
        /// <param name="brRadii">The radius of the bottom-right corner.</param>
        /// <param name="blRadii">The radius of the bottom-left corner.</param>
        public void RoundedRect(double x, double y, double width, double height, double tlRadii, double trRadii, double brRadii, double blRadii)
        {
            if (width <= 0 || height <= 0)
                return;

            // Clamp radii to half of the smaller dimension to prevent overlap
            double maxRadius = Math.Min(width, height) / 2;
            tlRadii = Math.Min(tlRadii, maxRadius);
            trRadii = Math.Min(trRadii, maxRadius);
            brRadii = Math.Min(brRadii, maxRadius);
            blRadii = Math.Min(blRadii, maxRadius);

            BeginPath();
            // Top-left corner
            MoveTo(x + tlRadii, y);
            // Top edge and top-right corner
            LineTo(x + width - trRadii, y);
            Arc(x + width - trRadii, y + trRadii, trRadii, -Math.PI / 2, 0, false);
            // Right edge and bottom-right corner
            LineTo(x + width, y + height - brRadii);
            Arc(x + width - brRadii, y + height - brRadii, brRadii, 0, Math.PI / 2, false);
            // Bottom edge and bottom-left corner
            LineTo(x + blRadii, y + height);
            Arc(x + blRadii, y + height - blRadii, blRadii, Math.PI / 2, Math.PI, false);
            // Left edge and top-left corner
            LineTo(x, y + tlRadii);
            Arc(x + tlRadii, y + tlRadii, tlRadii, Math.PI, 3 * Math.PI / 2, false);
            ClosePath();
        }

        /// <summary>
        /// Creates a Closed Circle Path
        /// </summary>
        /// <param name="x">The x-coordinate of the center of the circle.</param>
        /// <param name="y">The y-coordinate of the center of the circle.</param>
        /// <param name="radius">The radius of the circle.</param>
        /// <param name="segments">The number of segments used to approximate the circle. Higher values create smoother circles.</param>
        public void Circle(double x, double y, double radius, int segments = -1)
        {
            if (segments == -1)
            {
                // Calculate number of segments based on radius size
                double distance = Math.PI * 2 * radius;
                segments = Math.Max(1, (int)Math.Ceiling(distance / _state.roundingMinDistance));
            }

            if (radius <= 0 || segments < 3)
                return;

            BeginPath();

            for (int i = 0; i <= segments; i++)
            {
                double angle = 2 * Math.PI * i / segments;
                double vx = x + radius * Math.Cos(angle);
                double vy = y + radius * Math.Sin(angle);

                LineTo(vx, vy);
            }

            ClosePath();
        }

        /// <summary>
        /// Creates a Closed Ellipse Path
        /// </summary>
        /// <param name="x">The x-coordinate of the center of the circle.</param>
        /// <param name="y">The y-coordinate of the center of the circle.</param>
        /// <param name="rx">The x-axis radius of the ellipse.</param>
        /// <param name="ry">The y-axis radius of the ellipse.</param>
        /// <param name="segments">The number of segments used to approximate the circle. Higher values create smoother circles.</param>
        public void Ellipse(double x, double y, double rx, double ry, int segments = -1)
        {
            if (segments == -1)
            {
                // Calculate number of segments based on radius size
                double distance = Math.PI * 2 * Math.Max(rx, ry);
                segments = Math.Max(1, (int)Math.Ceiling(distance / _state.roundingMinDistance));
            }

            if (rx <= 0 || ry <= 0 || segments < 3)
                return;

            BeginPath();

            for (int i = 0; i <= segments; i++)
            {
                double angle = 2 * Math.PI * i / segments;
                double vx = x + rx * Math.Cos(angle);
                double vy = y + ry * Math.Sin(angle);

                LineTo(vx, vy);
            }

            ClosePath();
        }

        /// <summary>
        /// Creates a Closed Pie Path
        /// </summary>
        /// <param name="x">The x-coordinate of the center of the pie.</param>
        /// <param name="y">The y-coordinate of the center of the pie.</param>
        /// <param name="radius">The radius of the pie.</param>
        /// <param name="startAngle">The starting angle in radians.</param>
        /// <param name="endAngle">The ending angle in radians.</param>
        /// <param name="segments">The number of segments used to approximate the curved edge. Higher values create smoother curves.</param>
        public void Pie(double x, double y, double radius, double startAngle, double endAngle, int segments = -1)
        {
            if (segments == -1)
            {
                double distance = CalculateArcLength(radius, startAngle, endAngle);
                segments = Math.Max(1, (int)Math.Ceiling(distance / _state.roundingMinDistance));
            }

            if (radius <= 0 || segments < 1)
                return;

            // Ensure angles are ordered correctly
            if (endAngle < startAngle)
                endAngle += 2 * Math.PI;

            // Calculate angle range
            double angleRange = endAngle - startAngle;
            double segmentAngle = angleRange / segments;

            // Start path
            BeginPath();
            MoveTo(x, y);

            // Generate vertices around the arc plus the two radial endpoints
            for (int i = 0; i <= segments; i++)
            {
                double angle = startAngle + i * segmentAngle;
                double vx = x + radius * Math.Cos(angle);
                double vy = y + radius * Math.Sin(angle);

                LineTo(vx, vy);
            }

            ClosePath();
        }

        #endregion


        #region Primitives (Shader-Based AA)

        /// <summary>
        /// Paints a Hardware-accelerated rectangle on the canvas.
        /// This does not modify or use the current path.
        /// </summary>
        /// <param name="x">The x-coordinate of the top-left corner of the rectangle.</param>
        /// <param name="y">The y-coordinate of the top-left corner of the rectangle.</param>
        /// <param name="width">The width of the rectangle.</param>
        /// <param name="height">The height of the rectangle.</param>
        /// <param name="color">The color of the rectangle.</param>
        /// <remarks>This is significantly faster than using the path API to draw a rectangle.</remarks>
        public void RectFilled(double x, double y, double width, double height, System.Drawing.Color color)
        {
            if (width <= 0 || height <= 0)
                return;

            // Center it so it scales and sits properly with AA
            x -= _pixelHalf;
            y -= _pixelHalf;
            width += _pixelWidth;
            height += _pixelWidth;

            // Apply transform to the four corners of the rectangle
            Vector2 topLeft = TransformPoint(new Vector2(x, y));
            Vector2 topRight = TransformPoint(new Vector2(x, y + height));
            Vector2 bottomRight = TransformPoint(new Vector2(x + width, y + height));
            Vector2 bottomLeft = TransformPoint(new Vector2(x + width, y));

            // Store the starting index to reference _vertices
            uint startVertexIndex = (uint)_vertices.Count;

            // Add all vertices with the transformed coordinates
            AddVertex(new Vertex(topLeft, new Vector2(0, 0), color));
            AddVertex(new Vertex(topRight, new Vector2(1, 0), color));
            AddVertex(new Vertex(bottomRight, new Vector2(1, 1), color));
            AddVertex(new Vertex(bottomLeft, new Vector2(0, 1), color));

            // Add indexes for fill
            _indices.Add(startVertexIndex);
            _indices.Add(startVertexIndex + 1);
            _indices.Add(startVertexIndex + 2);

            _indices.Add(startVertexIndex);
            _indices.Add(startVertexIndex + 2);
            _indices.Add(startVertexIndex + 3);

            AddTriangleCount(2);
        }

        public void Image(object texture, double x, double y, double width, double height, System.Drawing.Color color)
        {
            if (width <= 0 || height <= 0)
                return;

            SetTexture(texture);
            RectFilled(x, y, width, height, color);
            SetTexture(null);
        }

        /// <summary>
        /// Paints a Hardware-accelerated rounded rectangle on the canvas.
        /// This does not modify or use the current path.
        /// </summary>
        /// <param name="x">The x-coordinate of the top-left corner of the rounded rectangle.</param>
        /// <param name="y">The y-coordinate of the top-left corner of the rounded rectangle.</param>
        /// <param name="width">The width of the rounded rectangle.</param>
        /// <param name="height">The height of the rounded rectangle.</param>
        /// <param name="radius">The radius of the corners.</param>
        /// <param name="color">The color of the rounded rectangle.</param>
        /// <remarks>This is significantly faster than using the path API to draw a rounded rectangle.</remarks>
        public void RoundedRectFilled(double x, double y, double width, double height,
                                     double radius, System.Drawing.Color color)
        {
            RoundedRectFilled(x, y, width, height, radius, radius, radius, radius, color);
        }

        /// <summary>
        /// Paints a Hardware-accelerated rounded rectangle on the canvas.
        /// This does not modify or use the current path.
        /// </summary>
        /// <param name="x">The x-coordinate of the top-left corner of the rounded rectangle.</param>
        /// <param name="y">The y-coordinate of the top-left corner of the rounded rectangle.</param>
        /// <param name="width">The width of the rounded rectangle.</param>
        /// <param name="height">The height of the rounded rectangle.</param>
        /// <param name="tlRadii">The radius of the top-left corner.</param>
        /// <param name="trRadii">The radius of the top-right corner.</param>
        /// <param name="brRadii">The radius of the bottom-right corner.</param>
        /// <param name="blRadii">The radius of the bottom-left corner.</param>
        /// <param name="color">The color of the rounded rectangle.</param>
        /// <remarks>This is significantly faster than using the path API to draw a rounded rectangle.</remarks>
        public void RoundedRectFilled(double x, double y, double width, double height,
                                     double tlRadii, double trRadii, double brRadii, double blRadii,
                                     System.Drawing.Color color)
        {
            if (width <= 0 || height <= 0)
                return;

            // Clamp radii to half of the smaller dimension to prevent overlap
            double maxRadius = Math.Min(width, height) / 2;
            tlRadii = Math.Min(tlRadii, maxRadius);
            trRadii = Math.Min(trRadii, maxRadius);
            brRadii = Math.Min(brRadii, maxRadius);
            blRadii = Math.Min(blRadii, maxRadius);

            // Adjust for proper AA
            x -= _pixelHalf;
            y -= _pixelHalf;
            width += _pixelWidth;
            height += _pixelWidth;

            // Calculate segment counts for each corner based on radius size
            int tlSegments = Math.Max(1, (int)Math.Ceiling(Math.PI * tlRadii / 2 / _state.roundingMinDistance));
            int trSegments = Math.Max(1, (int)Math.Ceiling(Math.PI * trRadii / 2 / _state.roundingMinDistance));
            int brSegments = Math.Max(1, (int)Math.Ceiling(Math.PI * brRadii / 2 / _state.roundingMinDistance));
            int blSegments = Math.Max(1, (int)Math.Ceiling(Math.PI * blRadii / 2 / _state.roundingMinDistance));

            // Store the starting index to reference _vertices
            uint startVertexIndex = (uint)_vertices.Count;

            // Calculate the center point of the rectangle
            Vector2 center = TransformPoint(new Vector2(x + width / 2, y + height / 2));

            // Add center vertex with UV at 0.5,0.5 (no AA)
            AddVertex(new Vertex(center, new Vector2(0.5f, 0.5f), color));

            List<Vector2> points = new List<Vector2>();

            // Top-left corner
            if (tlRadii > 0)
            {
                Vector2 tlCenter = new Vector2(x + tlRadii, y + tlRadii);
                for (int i = 0; i <= tlSegments; i++)
                {
                    double angle = Math.PI + (Math.PI / 2) * i / tlSegments;
                    double vx = tlCenter.x + tlRadii * Math.Cos(angle);
                    double vy = tlCenter.y + tlRadii * Math.Sin(angle);
                    points.Add(new Vector2(vx, vy));
                }
            }
            else
            {
                points.Add(new Vector2(x, y));
            }

            // Top-right corner
            if (trRadii > 0)
            {
                Vector2 trCenter = new Vector2(x + width - trRadii, y + trRadii);
                for (int i = 0; i <= trSegments; i++)
                {
                    double angle = Math.PI * 3 / 2 + (Math.PI / 2) * i / trSegments;
                    double vx = trCenter.x + trRadii * Math.Cos(angle);
                    double vy = trCenter.y + trRadii * Math.Sin(angle);
                    points.Add(new Vector2(vx, vy));
                }
            }
            else
            {
                points.Add(new Vector2(x + width, y));
            }

            // Bottom-right corner
            if (brRadii > 0)
            {
                Vector2 brCenter = new Vector2(x + width - brRadii, y + height - brRadii);
                for (int i = 0; i <= brSegments; i++)
                {
                    double angle = 0 + (Math.PI / 2) * i / brSegments;
                    double vx = brCenter.x + brRadii * Math.Cos(angle);
                    double vy = brCenter.y + brRadii * Math.Sin(angle);
                    points.Add(new Vector2(vx, vy));
                }
            }
            else
            {
                points.Add(new Vector2(x + width, y + height));
            }

            // Bottom-left corner
            if (blRadii > 0)
            {
                Vector2 blCenter = new Vector2(x + blRadii, y + height - blRadii);
                for (int i = 0; i <= blSegments; i++)
                {
                    double angle = Math.PI / 2 + (Math.PI / 2) * i / blSegments;
                    double vx = blCenter.x + blRadii * Math.Cos(angle);
                    double vy = blCenter.y + blRadii * Math.Sin(angle);
                    points.Add(new Vector2(vx, vy));
                }
            }
            else
            {
                points.Add(new Vector2(x, y + height));
            }

            // Add all edge vertices
            for (int i = 0; i < points.Count; i++)
            {
                Vector2 transformedPoint = TransformPoint(points[i]);
                AddVertex(new Vertex(transformedPoint, new Vector2(0, 0), color));
            }

            // Create triangles (fan from center to edges)
            for (int i = 0; i < points.Count; i++)
            {
                uint current = (uint)(startVertexIndex + 1 + i);
                uint next = (uint)(startVertexIndex + 1 + ((i + 1) % points.Count));

                _indices.Add((uint)startVertexIndex);  // Center
                _indices.Add(next);                    // Next edge vertex
                _indices.Add(current);                 // Current edge vertex

                //AddTriangleCount(1);
            }
            AddTriangleCount(points.Count);
        }

        /// <summary>
        /// Paints a circle on the canvas.
        /// This does not modify or use the current path.
        /// </summary>
        /// <param name="x">The x-coordinate of the center of the circle.</param>
        /// <param name="y">The y-coordinate of the center of the circle.</param>
        /// <param name="radius">The radius of the circle.</param>
        /// <param name="color">The color of the circle.</param>
        /// <param name="segments">The number of segments used to approximate the circle. Higher values create smoother circles.</param>
        /// <remarks>This is significantly faster than using the path API to draw a circle.</remarks>
        public void CircleFilled(double x, double y, double radius, System.Drawing.Color color, int segments = -1)
        {
            if (segments == -1)
            {
                // Calculate number of segments based on radius size
                double distance = Math.PI * 2 * radius;
                segments = Math.Max(1, (int)Math.Ceiling(distance / _state.roundingMinDistance));
            }

            if (radius <= 0 || segments < 3)
                return;

            // Center it so it scales and sits properly with AA
            radius += _pixelHalf;

            // Store the starting index to reference _vertices
            uint startVertexIndex = (uint)_vertices.Count;

            Vector2 transformedCenter = TransformPoint(new Vector2(x, y));

            // Add center vertex with UV at 0.5,0.5 (no AA, Since 0 or 1 in shader is considered edge of shape and get anti aliased)
            AddVertex(new Vertex(transformedCenter, new Vector2(0.5f, 0.5f), color));

            // Generate vertices around the circle
            for (int i = 0; i <= segments; i++)
            {
                double angle = 2 * Math.PI * i / segments;
                double vx = x + radius * Math.Cos(angle);
                double vy = y + radius * Math.Sin(angle);

                Vector2 transformedPoint = TransformPoint(new Vector2(vx, vy));

                // Edge vertices have UV at 0,0 for anti-aliasing
                AddVertex(new Vertex(
                    transformedPoint,
                    new Vector2(0, 0),  // UV at edge for AA
                    color
                ));
            }

            // Create triangles (fan from center to edges)
            for (int i = 0; i < segments; i++)
            {
                _indices.Add((uint)startVertexIndex);                  // Center
                _indices.Add((uint)(startVertexIndex + 1 + ((i + 1) % segments))); // Next edge vertex
                _indices.Add((uint)(startVertexIndex + 1 + i));          // Current edge vertex

                //AddTriangleCount(1);
            }

            AddTriangleCount(segments);
        }

        /// <summary>
        /// Paints a Hardware-accelerated pie (circle sector) on the canvas.
        /// This does not modify or use the current path.
        /// </summary>
        /// <param name="x">The x-coordinate of the center of the pie.</param>
        /// <param name="y">The y-coordinate of the center of the pie.</param>
        /// <param name="radius">The radius of the pie.</param>
        /// <param name="startAngle">The starting angle in radians.</param>
        /// <param name="endAngle">The ending angle in radians.</param>
        /// <param name="color">The color of the pie.</param>
        /// <param name="segments">The number of segments used to approximate the curved edge. Higher values create smoother curves.</param>
        public void PieFilled(double x, double y, double radius, double startAngle, double endAngle, System.Drawing.Color color, int segments = -1)
        {
            if (segments == -1)
            {
                double distance = CalculateArcLength(radius, startAngle, endAngle);
                segments = Math.Max(1, (int)Math.Ceiling(distance / _state.roundingMinDistance));
            }

            if (radius <= 0 || segments < 1)
                return;

            // Ensure angles are ordered correctly
            if (endAngle < startAngle)
            {
                endAngle += 2 * Math.PI;
            }

            // Calculate angle range and segment size
            double angleRange = endAngle - startAngle;
            double segmentAngle = angleRange / segments;

            // Calculate the centroid of the pie section
            // For a pie section, the centroid is not at the circle center but at
            // a position ~2/3 toward the arc's midpoint
            double midAngle = startAngle + angleRange / 2;
            double centroidDistance = radius * 2 / 3 * Math.Sin(angleRange / 2) / (angleRange / 2);
            double centroidX = x + centroidDistance * Math.Cos(midAngle);
            double centroidY = y + centroidDistance * Math.Sin(midAngle);

            // Store the starting index to reference _vertices
            uint startVertexIndex = (uint)_vertices.Count;

            Vector2 transformedCenter = TransformPoint(new Vector2(x, y));
            Vector2 transformedCentroid = TransformPoint(new Vector2(centroidX, centroidY));

            // Add centroid vertex with UV at 0.5,0.5 (fully opaque, no AA)
            AddVertex(new Vertex(transformedCentroid, new Vector2(0.5f, 0.5f), color));

            // Start path
            AddVertex(new Vertex(transformedCenter, new Vector2(0.0f, 0.0f), color));

            // Generate vertices around the arc plus the two radial endpoints
            for (int i = 0; i <= segments; i++)
            {
                double angle = startAngle + i * segmentAngle;
                double vx = x + radius * Math.Cos(angle);
                double vy = y + radius * Math.Sin(angle);

                Vector2 transformedPoint = TransformPoint(new Vector2(vx, vy));

                // Offset for AA
                var direction = (transformedPoint - transformedCenter).normalized;
                transformedPoint += direction * _pixelWidth;

                // Edge vertices have UV at 0,0 for anti-aliasing
                AddVertex(new Vertex(transformedPoint, new Vector2(0, 0), color));
            }

            // Close path
            AddVertex(new Vertex(transformedCenter, new Vector2(0.0f, 0.0f), color));

            // Create triangles (fan from centroid to each pair of edge points)
            for (int i = 0; i < segments + 2; i++)
            {
                _indices.Add(startVertexIndex);                  // Centroid
                _indices.Add((uint)(startVertexIndex + 1 + i + 1));      // Next edge vertex
                _indices.Add((uint)(startVertexIndex + 1 + i));          // Current edge vertex

                //AddTriangleCount(1);
            }

            AddTriangleCount(segments + 2);
        }
        #endregion

        #region Text

        public void DrawText(SpriteFontBase font, string text, double x, double y, Color color, double rotation = 0f, Vector2 origin = default(Vector2), Vector2? scale = null, double layerDepth = 0f, double characterSpacing = 0f, double lineSpacing = 0f, TextStyle textStyle = TextStyle.None)
        {
            if (origin != default(Vector2))
            {
                // Convert normalized origin to Pixels
                var size = font.MeasureString(text, scale, (float)characterSpacing, (float)lineSpacing);
                origin = new Vector2((origin.x * size.X) - characterSpacing, origin.y * size.Y);
            }

            _fontStashRenderer.Text(font, text, new Vector2(x, y), color, rotation, origin, scale, layerDepth, characterSpacing, lineSpacing, textStyle);
        }
        public void DrawText(SpriteFontBase font, string text, double x, double y, Color[] colors, double rotation = 0f, Vector2 origin = default(Vector2), Vector2? scale = null, double layerDepth = 0f, double characterSpacing = 0f, double lineSpacing = 0f, TextStyle textStyle = TextStyle.None)
        {
            if (origin != default(Vector2))
            {
                // Convert normalized origin to Pixels
                var size = font.MeasureString(text, scale, (float)characterSpacing, (float)lineSpacing);
                origin = new Vector2(origin.x * size.X, origin.y * size.Y);
            }

            _fontStashRenderer.Text(font, text, new Vector2(x, y), colors, rotation, origin, scale, layerDepth, characterSpacing, lineSpacing, textStyle);
        }

        #endregion

        #region Helpers

        internal static double CalculateArcLength(double radius, double startAngle, double endAngle)
        {
            // Make sure end angle is greater than start angle
            if (endAngle < startAngle)
                endAngle += 2 * Math.PI;
            return radius * (endAngle - startAngle);
        }

        // Helper function to calculate the signed angle from vector u to vector v
        internal static double CalculateVectorAngle(double ux, double uy, double vx, double vy)
        {
            double dot = ux * vx + uy * vy;
            double det = ux * vy - uy * vx; // 2D cross product
            return Math.Atan2(det, dot); // Returns angle in radians from -PI to PI
        }

        #endregion

        public void Dispose()
        {
            _renderer?.Dispose();
        }
    }
}
