using Prowl.Vector;
using System.Drawing;
using System.Runtime.InteropServices;

namespace Prowl.Quill;

public struct ProwlCanvasDrawCall
{
    public int ElementCount;
    public object? Texture;
    internal Transform2D scissor;
    internal Vector2 scissorExtent;

    public void GetScissor(out Matrix4x4 matrix, out Vector2 extent)
    {
        if (scissorExtent.x < -0.5f || scissorExtent.y < -0.5f)
        {
            // Invalid scissor - disable it
            matrix = new();
            extent = new(1, 1);
        }
        else
        {
            // Set up scissor transform and dimensions
            matrix = scissor.Inverse().ToMatrix4x4();
            extent = new(scissorExtent.x, scissorExtent.y);
        }
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ProwlCanvasVertex(Vector2 position, Vector2 UV, Color color)
{
    public static int SizeInBytes => Marshal.SizeOf<ProwlCanvasVertex>();

    public readonly Vector2 Position => new Vector2(x, y);
    public readonly Vector2 UV => new Vector2(u, v);
    public readonly Color Color => Color.FromArgb(a, r, g, b);


    public float x = (float)position.x;
    public float y = (float)position.y;

    public float u = (float)UV.x;
    public float v = (float)UV.y;

    public byte r = color.R;
    public byte g = color.G;
    public byte b = color.B;
    public byte a = color.A;
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

    internal object? texture;
    internal Transform2D scissor;
    internal Vector2 scissorExtent;


    internal Color fillColor;

    public ProwlCanvasState() => Reset();

    internal void Reset()
    {
        transform = Transform2D.Identity;
        strokeColor = Color.FromArgb(255, 0, 0, 0); // Default stroke color (black)
        strokeJoint = JointStyle.Bevel; // Default joint style
        strokeWidth = 1f; // Default stroke width
        strokeScale = 1f; // Default stroke scale
        miterLimit = 4; // Default miter limit
        texture = null;
        scissor.Zero();
        scissorExtent.x = -1.0f;
        scissorExtent.y = -1.0f;
        fillColor = Color.FromArgb(255, 0, 0, 0); // Default fill color (black)
    }
}

public partial class Canvas
{
    internal class SubPath(List<Vector2> points, bool isClosed)
    {
        internal List<Vector2> Points { get; } = points;
        internal bool IsClosed { get; } = isClosed;
    }

    private const double RoundingMinDistance = 5;
    public IReadOnlyList<ProwlCanvasDrawCall> DrawCalls => _drawCalls.Where(d => d.ElementCount != 0).ToList();
    public IReadOnlyList<uint> Indices => _indices.AsReadOnly();
    public IReadOnlyList<ProwlCanvasVertex> Vertices => _vertices.AsReadOnly();

    internal List<ProwlCanvasDrawCall> _drawCalls = new();
    internal Stack<object> _textureStack = new();

    internal List<uint> _indices = new();
    internal List<ProwlCanvasVertex> _vertices = new();

    private readonly List<SubPath> _subPaths = new();
    private SubPath? _currentSubPath = null;
    private bool _isPathOpen = false;

    private readonly Stack<ProwlCanvasState> _savedStates = [];
    private ProwlCanvasState _state;
    private double _globalAlpha;

    public Vector2 TexUvWhitePixel { get; set; } = new Vector2(0, 0);

    public void Clear()
    {
        _drawCalls.Clear();
        _textureStack.Clear();
        AddDrawCmd();

        _indices.Clear();
        _vertices.Clear();

        _savedStates.Clear();
        _state = new();

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
    public void SetStrokeWidth(double width = 1f) => _state.strokeWidth = width;
    public void SetStrokeScale(double scale) => _state.strokeScale = scale;
    public void SetMiterLimit(double limit = 4) => _state.miterLimit = limit;
    public void SetTexture(object? texture) => _state.texture = texture;
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


    #endregion

    #region Draw Calls

    public void AddDrawCmd() => _drawCalls.Add(new ProwlCanvasDrawCall());

    private void AddTriangleCount(int count)
    {
        if (_drawCalls.Count == 0)
            return;

        ProwlCanvasDrawCall lastDrawCall = _drawCalls[_drawCalls.Count - 1];

        bool isDrawStateSame = lastDrawCall.Texture == _state.texture && 
            lastDrawCall.scissorExtent == _state.scissorExtent && 
            lastDrawCall.scissor == _state.scissor;

        if (!isDrawStateSame)
        {
            // If the texture or scissor state has changed, add a new draw call
            AddDrawCmd();
            lastDrawCall = _drawCalls[_drawCalls.Count - 1];
            lastDrawCall.Texture = _state.texture;
            lastDrawCall.scissor = _state.scissor;
            lastDrawCall.scissorExtent = _state.scissorExtent;
        }

        lastDrawCall.ElementCount += count * 3;
        _drawCalls[_drawCalls.Count - 1] = lastDrawCall;
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

        _currentSubPath = new(new List<Vector2>(), false);
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
        int segments = Math.Max(1, (int)Math.Ceiling(distance / RoundingMinDistance));

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

        Vector2 p0 = _currentSubPath!.Points[^1];
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
        Vector2 p1 = _currentSubPath!.Points[^1];
        Vector2 p2 = new Vector2(cp1x, cp1y);
        Vector2 p3 = new Vector2(cp2x, cp2y);
        Vector2 p4 = new Vector2(x, y);

        // Auto-tessellated
        const double tess_tol = 0.5f;
        PathBezierToCasteljau(p1.x, p1.y, p2.x, p2.y, p3.x, p3.y, p4.x, p4.y, tess_tol, 0);
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
            _currentSubPath!.Points.Add(new Vector2(x4, y4));
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

        Vector2 p1 = _currentSubPath!.Points[^1];
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

        // Fill all sub-paths
        foreach (var subPath in _subPaths)
            FillSubPath(subPath);
    }

    private void FillSubPath(SubPath subPath)
    {
        if (subPath.Points.Count < 3)
            return;

        var copy = subPath.Points.ToArray();
        // Transform each point
        for (int i = 0; i < subPath.Points.Count; i++)
            subPath.Points[i] = ModifyPoint(subPath.Points[i]);

        Vector2 center = Vector2.zero;
        for (int i = 0; i < subPath.Points.Count; i++)
            center += subPath.Points[i];
        center /= subPath.Points.Count;

        // Store the starting index to reference _vertices
        uint startVertexIndex = (uint)_vertices.Count;

        var color = ApplyGlobalAlpha(_state.fillColor);

        // Add center vertex with UV at 0.5,0.5 (no AA, Since 0 or 1 in shader is considered edge of shape and get anti aliased)
        _vertices.Add(new ProwlCanvasVertex(center, new(0.5f, 0.5f), color));

        // Generate vertices around the path
        // TODO: May need to push the vertex out by half a pixel or a pixel to counteract the AA's habit of shrinking things
        int segments = subPath.Points.Count;
        for (int i = 0; i < segments; i++) // Edge vertices have UV at 0,0 for anti-aliasing
            _vertices.Add(new(subPath.Points[i], new(0, 0), color));

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

            AddTriangleCount(1);
        }

        // Reset the points to their original values
        for (int i = 0; i < subPath.Points.Count; i++)
            subPath.Points[i] = copy[i];
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
            subPath.Points[i] = ModifyPoint(subPath.Points[i]);

        bool isClosed = subPath.IsClosed;
        var triangles = PolylineMesher.Create(subPath.Points, _state.strokeWidth * _state.strokeScale, _state.strokeColor, _state.strokeJoint, _state.miterLimit, false, _state.strokeStartCap, _state.strokeEndCap);

        // Store the starting index to reference _vertices
        uint startVertexIndex = (uint)_vertices.Count;
        foreach (var triangle in triangles)
        {
            var color = ApplyGlobalAlpha(triangle.Color);
            _vertices.Add(new ProwlCanvasVertex(triangle.V1, triangle.UV1, color));
            _vertices.Add(new ProwlCanvasVertex(triangle.V2, triangle.UV2, color));
            _vertices.Add(new ProwlCanvasVertex(triangle.V3, triangle.UV3, color));
        }

        // Add triangle _indices
        for (uint i = 0; i < triangles.Count; i++)
        {
            _indices.Add(startVertexIndex + (i * 3));
            _indices.Add(startVertexIndex + (i * 3) + 1);
            _indices.Add(startVertexIndex + (i * 3) + 2);
            AddTriangleCount(1);
        }

        // Reset the points to their original values
        for (int i = 0; i < subPath.Points.Count; i++)
            subPath.Points[i] = copy[i];
    }

    public void FillAndStroke()
    {
        Fill();
        Stroke();
    }

    private Color ApplyGlobalAlpha(Color color)
    {
        if (_globalAlpha < 1f)
        {
            byte a = (byte)(color.A * _globalAlpha);
            return Color.FromArgb(a, color.R, color.G, color.B);
        }
        return color;
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
    /// <param name="tlRadii">The radius of the top-left corner.</param>
    /// <param name="trRadii">The radius of the top-right corner.</param>
    /// <param name="brRadii">The radius of the bottom-right corner.</param>
    /// <param name="blRadii">The radius of the bottom-left corner.</param>
    public void RoundedRect(double x, double y, double width, double height, double tlRadii, double trRadii, double brRadii, double blRadii)
    {
        if (width <= 0 || height <= 0)
            return;
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
            segments = Math.Max(1, (int)Math.Ceiling(distance / RoundingMinDistance));
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
        if(segments == -1)
        {
            double distance = CalculateArcLength(radius, startAngle, endAngle);
            segments = Math.Max(1, (int)Math.Ceiling(distance / RoundingMinDistance));
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
        //x -= 0.5;
        //y -= 0.5;
        //width++;
        //height++;

        // Apply transform to the four corners of the rectangle
        Vector2 topLeft = ModifyPoint(new Vector2(x, y));
        Vector2 topRight = ModifyPoint(new Vector2(x, y + height));
        Vector2 bottomRight = ModifyPoint(new Vector2(x + width, y + height));
        Vector2 bottomLeft = ModifyPoint(new Vector2(x + width, y));

        // Store the starting index to reference _vertices
        uint startVertexIndex = (uint)_vertices.Count;

        // Add all vertices with the transformed coordinates
        _vertices.Add(new ProwlCanvasVertex(topLeft, new(0, 0), color));
        _vertices.Add(new ProwlCanvasVertex(topRight, new(1, 0), color));
        _vertices.Add(new ProwlCanvasVertex(bottomRight, new(1, 1), color));
        _vertices.Add(new ProwlCanvasVertex(bottomLeft, new(0, 1), color));

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

        // Adjust for proper AA
        x += 0.5;
        y += 0.5;
        width -= 1;
        height -= 1;

        // Calculate segment counts for each corner based on radius size
        int tlSegments = Math.Max(1, (int)Math.Ceiling(Math.PI * tlRadii / 2 / RoundingMinDistance));
        int trSegments = Math.Max(1, (int)Math.Ceiling(Math.PI * trRadii / 2 / RoundingMinDistance));
        int brSegments = Math.Max(1, (int)Math.Ceiling(Math.PI * brRadii / 2 / RoundingMinDistance));
        int blSegments = Math.Max(1, (int)Math.Ceiling(Math.PI * blRadii / 2 / RoundingMinDistance));

        // Store the starting index to reference _vertices
        uint startVertexIndex = (uint)_vertices.Count;

        // Calculate the center point of the rectangle
        Vector2 center = ModifyPoint(new Vector2(x + width / 2, y + height / 2));

        // Add center vertex with UV at 0.5,0.5 (no AA)
        _vertices.Add(new ProwlCanvasVertex(center, new(0.5f, 0.5f), color));

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
            Vector2 transformedPoint = ModifyPoint(points[i]);
            _vertices.Add(new ProwlCanvasVertex(transformedPoint, new(0, 0), color));
        }

        // Create triangles (fan from center to edges)
        for (int i = 0; i < points.Count; i++)
        {
            uint current = (uint)(startVertexIndex + 1 + i);
            uint next = (uint)(startVertexIndex + 1 + ((i + 1) % points.Count));

            _indices.Add((uint)startVertexIndex);  // Center
            _indices.Add(current);                 // Current edge vertex
            _indices.Add(next);                    // Next edge vertex

            AddTriangleCount(1);
        }
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
        if(segments == -1)
        {
            // Calculate number of segments based on radius size
            double distance = Math.PI * 2 * radius;
            segments = Math.Max(1, (int)Math.Ceiling(distance / RoundingMinDistance));
        }

        if (radius <= 0 || segments < 3)
            return;

        // Center it so it scales and sits properly with AA
        x += 0.5;
        y += 0.5;
        radius++;

        // Store the starting index to reference _vertices
        uint startVertexIndex = (uint)_vertices.Count;

        Vector2 transformedCenter = ModifyPoint(new Vector2(x, y));

        // Add center vertex with UV at 0.5,0.5 (no AA, Since 0 or 1 in shader is considered edge of shape and get anti aliased)
        _vertices.Add(new ProwlCanvasVertex(transformedCenter, new(0.5f, 0.5f), color));

        // Generate vertices around the circle
        for (int i = 0; i <= segments; i++)
        {
            double angle = 2 * Math.PI * i / segments;
            double vx = x + radius * Math.Cos(angle);
            double vy = y + radius * Math.Sin(angle);

            Vector2 transformedPoint = ModifyPoint(new Vector2(vx, vy));

            // Edge vertices have UV at 0,0 for anti-aliasing
            _vertices.Add(new ProwlCanvasVertex(
                transformedPoint,
                new(0, 0),  // UV at edge for AA
                color
            ));
        }

        // Create triangles (fan from center to edges)
        for (int i = 0; i < segments; i++)
        {
            _indices.Add((uint)startVertexIndex);                  // Center
            _indices.Add((uint)(startVertexIndex + 1 + ((i + 1) % segments))); // Next edge vertex
            _indices.Add((uint)(startVertexIndex + 1 + i));          // Current edge vertex

            AddTriangleCount(1);
        }
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
            segments = Math.Max(1, (int)Math.Ceiling(distance / RoundingMinDistance));
        }

        if (radius <= 0 || segments < 1)
            return;

        // Center it so it scales and sits properly with AA
        x += 0.5;
        y += 0.5;
        radius += 1;

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

        Vector2 transformedCenter = ModifyPoint(new Vector2(x, y));
        Vector2 transformedCentroid = ModifyPoint(new Vector2(centroidX, centroidY));

        // Add centroid vertex with UV at 0.5,0.5 (fully opaque, no AA)
        _vertices.Add(new(transformedCentroid, new(0.5f, 0.5f), color));

        // Start path
        _vertices.Add(new(transformedCenter, new(0.0f, 0.0f), color));

        // Generate vertices around the arc plus the two radial endpoints
        for (int i = 0; i <= segments; i++)
        {
            double angle = startAngle + i * segmentAngle;
            double vx = x + radius * Math.Cos(angle);
            double vy = y + radius * Math.Sin(angle);

            Vector2 transformedPoint = ModifyPoint(new Vector2(vx, vy));

            // Edge vertices have UV at 0,0 for anti-aliasing
            _vertices.Add(new(transformedPoint, new(0, 0), color));
        }

        // Close path
        _vertices.Add(new(transformedCenter, new(0.0f, 0.0f), color));

        // Create triangles (fan from centroid to each pair of edge points)
        for (int i = 0; i < segments + 2; i++)
        {
            _indices.Add(startVertexIndex);                  // Centroid
            _indices.Add((uint)(startVertexIndex + 1 + i + 1));      // Next edge vertex
            _indices.Add((uint)(startVertexIndex + 1 + i));          // Current edge vertex

            AddTriangleCount(1);
        }
    }
    #endregion

    #region Helpers

    private double CalculateArcLength(double radius, double startAngle, double endAngle)
    {
        // Make sure end angle is greater than start angle
        if (endAngle < startAngle)
            endAngle += 2 * Math.PI;
        return radius * (endAngle - startAngle);
    }

    private Vector2 ModifyPoint(Vector2 point) => _state.transform.TransformPoint(point);

    #endregion
}
