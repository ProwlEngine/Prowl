using Prowl.Vector;
using System.Drawing;
using Color = Prowl.Vector.Color;

namespace Prowl.Quill;

/// <summary>
/// A wrapper around Canvas that supports 3D rendering operations by projecting 3D points to 2D.
/// </summary>
public class Canvas3D
{
    private readonly Canvas _canvas;
    private Double4x4 _viewMatrix;
    private Double4x4 _projectionMatrix;
    private Double4x4 _worldMatrix;
    private Double4x4 _viewProjectionMatrix;

    private List<Double3> _currentPath = new List<Double3>();
    private double _viewportWidth = 800;
    private double _viewportHeight = 600;
    private bool _isPathOpen = false;

    /// <summary>
    /// The canvas being wrapped
    /// </summary>
    public Canvas Canvas => _canvas;

    /// <summary>
    /// Current view matrix
    /// </summary>
    public Double4x4 ViewMatrix {
        get => _viewMatrix;
        set {
            _viewMatrix = value;
            UpdateViewProjectionMatrix();
        }
    }

    /// <summary>
    /// Current projection matrix
    /// </summary>
    public Double4x4 ProjectionMatrix {
        get => _projectionMatrix;
        set {
            _projectionMatrix = value;
            UpdateViewProjectionMatrix();
        }
    }

    /// <summary>
    /// Current world matrix
    /// </summary>
    public Double4x4 WorldMatrix {
        get => _worldMatrix;
        set {
            _worldMatrix = value;
            UpdateViewProjectionMatrix();
        }
    }

    /// <summary>
    /// Sets or gets the viewport width used for projection
    /// </summary>
    public double ViewportWidth {
        get => _viewportWidth;
        set => _viewportWidth = value;
    }

    /// <summary>
    /// Sets or gets the viewport height used for projection
    /// </summary>
    public double ViewportHeight {
        get => _viewportHeight;
        set => _viewportHeight = value;
    }

    /// <summary>
    /// Creates a new Canvas3D wrapper around an existing Canvas
    /// </summary>
    /// <param name="canvas">The Canvas to wrap</param>
    /// <param name="viewportWidth">Width of the viewport</param>
    /// <param name="viewportHeight">Height of the viewport</param>
    public Canvas3D(Canvas canvas, double viewportWidth = 800, double viewportHeight = 600)
    {
        _canvas = canvas;
        _viewportWidth = viewportWidth;
        _viewportHeight = viewportHeight;

        // Initialize with identity matrices
        _worldMatrix = Double4x4.Identity;
        _viewMatrix = Double4x4.Identity;
        _projectionMatrix = Double4x4.Identity;
        _viewProjectionMatrix = Double4x4.Identity;
    }

    /// <summary>
    /// Updates the combined view-projection matrix
    /// </summary>
    private void UpdateViewProjectionMatrix()
    {
        _viewProjectionMatrix = _projectionMatrix * _viewMatrix * _worldMatrix;
    }

    /// <summary>
    /// Sets up a perspective projection
    /// </summary>
    /// <param name="fieldOfView">Field of view angle in radians</param>
    /// <param name="aspectRatio">Aspect ratio (width/height)</param>
    /// <param name="nearPlane">Distance to near clipping plane</param>
    /// <param name="farPlane">Distance to far clipping plane</param>
    public void SetPerspectiveProjection(double fieldOfView, double aspectRatio, double nearPlane, double farPlane)
    {
        ProjectionMatrix = Double4x4.CreatePerspectiveFov(fieldOfView, aspectRatio, nearPlane, farPlane);
    }

    /// <summary>
    /// Sets up the camera view
    /// </summary>
    /// <param name="cameraPosition">Position of the camera</param>
    /// <param name="targetPosition">Point the camera is looking at</param>
    /// <param name="upVector">Up vector for the camera</param>
    public void SetLookAt(Double3 cameraPosition, Double3 targetPosition, Double3 upVector)
    {
        ViewMatrix = Double4x4.CreateLookAt(cameraPosition, targetPosition, upVector);
    }

    /// <summary>
    /// Sets the world transform matrix
    /// </summary>
    /// <param name="position">Position in world space</param>
    /// <param name="rotation">Rotation quaternion</param>
    /// <param name="scale">Scale factor</param>
    public void SetWorldTransform(Double3 position, Quaternion rotation, Double3 scale)
    {
        Double4x4 translationMatrix = Double4x4.CreateTranslation(position);
        Double4x4 rotationMatrix = Double4x4.CreateFromQuaternion(rotation);
        Double4x4 scaleMatrix = Double4x4.CreateScale(scale);

        WorldMatrix = scaleMatrix * rotationMatrix * translationMatrix;
    }

    /// <summary>
    /// Projects a 3D point to 2D screen coordinates
    /// </summary>
    /// <param name="point3D">The 3D point to project</param>
    /// <returns>2D screen coordinates</returns>
    public Double2 Project(Double3 point3D)
    {
        // Transform the point to clip space
        Double4 clipSpace = _viewProjectionMatrix * new Double4(point3D, 1.0f);

        // Skip points behind the camera or outside the frustum
        if (clipSpace.W <= 0 ||
            clipSpace.X < -clipSpace.W || clipSpace.X > clipSpace.W ||
            clipSpace.Y < -clipSpace.W || clipSpace.Y > clipSpace.W ||
            clipSpace.Z < -clipSpace.W || clipSpace.Z > clipSpace.W)
        {
            return new Double2(double.NaN, double.NaN); // Indicate point is not visible
        }

        // Perform perspective division to get NDC coordinates
        double ndcX = clipSpace.X / clipSpace.W;
        double ndcY = clipSpace.Y / clipSpace.W;

        // Convert to viewport coordinates
        double screenX = (ndcX + 1.0f) * 0.5f * _viewportWidth;
        double screenY = (1.0f - (ndcY + 1.0f) * 0.5f) * _viewportHeight; // Flip Y for screen coordinates

        return new Double2(screenX, screenY);
    }

    /// <summary>
    /// Determines if a 3D point would be visible when projected
    /// </summary>
    public bool IsVisible(Double3 point3D)
    {
        Double4 clipSpace = _viewProjectionMatrix * new Double4(point3D, 1.0f);

        return clipSpace.W > 0 &&
               clipSpace.X >= -clipSpace.W && clipSpace.X <= clipSpace.W &&
               clipSpace.Y >= -clipSpace.W && clipSpace.Y <= clipSpace.W &&
               clipSpace.Z >= -clipSpace.W && clipSpace.Z <= clipSpace.W;
    }

    /// <summary>
    /// Draws a line between two 3D points
    /// </summary>
    public void DrawLine(Double3 start, Double3 end, Color color, double width = 1.0f)
    {
        Double2 start2D = Project(start);
        Double2 end2D = Project(end);

        if (double.IsNaN(start2D.X) || double.IsNaN(end2D.X))
            return; // Skip if either point is not visible

        _canvas.SetStrokeColor(color);
        _canvas.SetStrokeWidth(width);
        _canvas.BeginPath();
        _canvas.MoveTo(start2D.X, start2D.Y);
        _canvas.LineTo(end2D.X, end2D.Y);
        _canvas.Stroke();
    }

    #region Path API

    /// <summary>
    /// Begins a new path by emptying the list of sub-paths.
    /// </summary>
    public void BeginPath()
    {
        _currentPath.Clear();
        _isPathOpen = true;
    }

    /// <summary>
    /// Moves the current position to the specified 3D point without drawing a line.
    /// </summary>
    /// <param name="x">The x-coordinate in 3D space</param>
    /// <param name="y">The y-coordinate in 3D space</param>
    /// <param name="z">The z-coordinate in 3D space</param>
    public void MoveTo(double x, double y, double z)
    {
        if (!_isPathOpen)
            BeginPath();

        _currentPath.Add(new Double3(x, y, z));
    }

    /// <summary>
    /// Moves the current position to the specified 3D point without drawing a line.
    /// </summary>
    /// <param name="point">The point in 3D space</param>
    public void MoveTo(Double3 point)
    {
        MoveTo(point.X, point.Y, point.Z);
    }

    /// <summary>
    /// Draws a line from the current position to the specified 3D point.
    /// </summary>
    /// <param name="x">The x-coordinate in 3D space</param>
    /// <param name="y">The y-coordinate in 3D space</param>
    /// <param name="z">The z-coordinate in 3D space</param>
    public void LineTo(double x, double y, double z)
    {
        if (!_isPathOpen)
            BeginPath();

        _currentPath.Add(new Double3(x, y, z));
    }

    /// <summary>
    /// Draws a line from the current position to the specified 3D point.
    /// </summary>
    /// <param name="point">The point in 3D space</param>
    public void LineTo(Double3 point)
    {
        LineTo(point.X, point.Y, point.Z);
    }

    /// <summary>
    /// Closes the current path by drawing a straight line from the current position to the starting point.
    /// </summary>
    public void ClosePath()
    {
        if (_currentPath.Count >= 2)
        {
            // Add the first point again to close the path
            _currentPath.Add(_currentPath[0]);
        }
    }

    /// <summary>
    /// Strokes the current path
    /// </summary>
    public void Stroke()
    {
        if (_currentPath.Count < 2)
            return;

        FlattenPath();

        _canvas.Stroke();
    }

    /// <summary>
    /// Fills the current path
    /// </summary>
    public void Fill()
    {
        if (_currentPath.Count < 2)
            return;

        FlattenPath();

        _canvas.Fill();
    }

    private void FlattenPath()
    {
        _canvas.BeginPath();

        bool firstPoint = true;
        Double2? lastPoint = null;

        for (int i = 0; i < _currentPath.Count; i++)
        {
            Double2 point2D = Project(_currentPath[i]);

            if (!double.IsNaN(point2D.X))
            {
                if (firstPoint)
                {
                    _canvas.MoveTo(point2D.X, point2D.Y);
                    firstPoint = false;
                }
                else
                {
                    // If we have a valid last point, draw a line
                    if (lastPoint.HasValue)
                    {
                        _canvas.LineTo(point2D.X, point2D.Y);
                    }
                    else
                    {
                        // If previous points were invisible but this one is visible,
                        // we need to start a new segment
                        _canvas.MoveTo(point2D.X, point2D.Y);
                    }
                }
                lastPoint = point2D;
            }
            else
            {
                // Point is not visible, mark it
                lastPoint = null;
            }
        }
    }

    #endregion

    /// <summary>
    /// Draws a wireframe cube centered at the specified position
    /// </summary>
    public void DrawCubeStroked(Double3 center, double size)
    {
        double halfSize = size * 0.5f;

        // Define the 8 vertices of the cube
        Double3[] vertices = new Double3[8];
        vertices[0] = new Double3(center.X - halfSize, center.Y - halfSize, center.Z - halfSize);
        vertices[1] = new Double3(center.X + halfSize, center.Y - halfSize, center.Z - halfSize);
        vertices[2] = new Double3(center.X + halfSize, center.Y + halfSize, center.Z - halfSize);
        vertices[3] = new Double3(center.X - halfSize, center.Y + halfSize, center.Z - halfSize);
        vertices[4] = new Double3(center.X - halfSize, center.Y - halfSize, center.Z + halfSize);
        vertices[5] = new Double3(center.X + halfSize, center.Y - halfSize, center.Z + halfSize);
        vertices[6] = new Double3(center.X + halfSize, center.Y + halfSize, center.Z + halfSize);
        vertices[7] = new Double3(center.X - halfSize, center.Y + halfSize, center.Z + halfSize);

        // Draw the bottom face
        BeginPath();
        MoveTo(vertices[0]);
        LineTo(vertices[1]);
        LineTo(vertices[2]);
        LineTo(vertices[3]);
        ClosePath();
        Stroke();

        // Draw the top face
        BeginPath();
        MoveTo(vertices[4]);
        LineTo(vertices[5]);
        LineTo(vertices[6]);
        LineTo(vertices[7]);
        ClosePath();
        Stroke();

        // Draw the connecting edges
        for (int i = 0; i < 4; i++)
        {
            BeginPath();
            MoveTo(vertices[i]);
            LineTo(vertices[i + 4]);
            Stroke();
        }
    }

    /// <summary>
    /// Draws a wireframe sphere centered at the specified position
    /// </summary>
    public void DrawSphereStroked(Double3 center, double radius, int segments = 16)
    {
        // Draw longitude lines (vertical circles)
        for (int i = 0; i < segments; i++)
        {
            double angle = (double)(2 * Math.PI * i / segments);
            BeginPath();

            for (int j = 0; j <= segments; j++)
            {
                double phi = (double)(Math.PI * j / segments);
                double x = radius * (double)Math.Sin(phi) * (double)Math.Cos(angle);
                double y = radius * (double)Math.Cos(phi);
                double z = radius * (double)Math.Sin(phi) * (double)Math.Sin(angle);

                Double3 point3D = new Double3(center.X + x, center.Y + y, center.Z + z);

                if (j == 0)
                    MoveTo(point3D);
                else
                    LineTo(point3D);
            }
            Stroke();
        }

        // Draw latitude lines (horizontal circles)
        for (int j = 1; j < segments; j++)
        {
            double phi = (double)(Math.PI * j / segments);
            double radiusAtLatitude = radius * (double)Math.Sin(phi);
            double y = radius * (double)Math.Cos(phi);

            BeginPath();

            for (int i = 0; i <= segments; i++)
            {
                double angle = (double)(2 * Math.PI * i / segments);
                double x = radiusAtLatitude * (double)Math.Cos(angle);
                double z = radiusAtLatitude * (double)Math.Sin(angle);

                Double3 point3D = new Double3(center.X + x, center.Y + y, center.Z + z);

                if (i == 0)
                    MoveTo(point3D);
                else
                    LineTo(point3D);
            }
            Stroke();
        }
    }

    /// <summary>
    /// Draws a 3D arc
    /// </summary>
    public void Arc(Double3 center, double radius, Double3 normal, Double3 startDir,
                   double angleInRadians, int segments = 16)
    {
        // Normalize vectors
        normal = Double3.Normalize(normal);
        startDir = Double3.Normalize(startDir);

        // Calculate perpendicular vector to both normal and startDir
        Double3 perpVector = Double3.Normalize(Double3.Cross(normal, startDir));

        BeginPath();

        for (int i = 0; i <= segments; i++)
        {
            double angle = angleInRadians * i / segments;

            // Rotate startDir around normal by angle
            Double3 rotatedDir = startDir * (double)Math.Cos(angle) +
                                 perpVector * (double)Math.Sin(angle);

            // Calculate point on arc
            Double3 point = center + rotatedDir * radius;

            if (i == 0)
                MoveTo(point);
            else
                LineTo(point);
        }
    }

    /// <summary>
    /// Draws a 3D Bezier curve
    /// </summary>
    public void BezierCurve(Double3 p0, Double3 p1, Double3 p2, Double3 p3, int segments = 16)
    {
        BeginPath();
        MoveTo(p0);

        for (int i = 1; i <= segments; i++)
        {
            double t = i / (double)segments;
            double u = 1.0f - t;

            // Cubic Bezier formula
            Double3 point = u * u * u * p0 +
                           3 * u * u * t * p1 +
                           3 * u * t * t * p2 +
                           t * t * t * p3;

            LineTo(point);
        }
    }

    public void Demo3D()
    {
        // Set up the camera and projection
        double aspectRatio = _viewportWidth / _viewportHeight;
        SetPerspectiveProjection((double)(Math.PI / 4.0), aspectRatio, 0.1f, 100.0f);
        SetLookAt(new Double3(0, 0, -10), Double3.Zero, Double3.UnitY);

        // Create rotation based on time for animation
        double time = (double)Environment.TickCount / 1000.0f;
        Quaternion rotation = Quaternion.FromEuler(time * 0.5f, time * 0.3f, 0);

        _canvas.SetStrokeWidth(2.0f);

        // Draw a rotating cube
        _canvas.SetStrokeColor(Color.Red);
        SetWorldTransform(new Double3(-3f, 0, 0), rotation, Double3.One);
        DrawCubeStroked(Double3.Zero, 2.0f);

        // Draw a rotating sphere
        _canvas.SetStrokeColor(Color.Blue);
        SetWorldTransform(new Double3(3f, 0, 0), rotation, Double3.One);
        DrawSphereStroked(Double3.Zero, 1.0f, 16);


        SetWorldTransform(Double3.Zero, rotation, Double3.One);

        // Draw a 3D arc
        _canvas.SetStrokeWidth(6.0f);
        _canvas.SetFillColor(Color.Yellow);
        Double3 arcCenter = new Double3(0, 0, 0);
        double arcRadius = 2.0f;
        Double3 arcNormal = new Double3(0, 1, 0);
        Double3 arcStartDir = new Double3(1, 0, 0);
        double arcAngle = (double)(Math.PI * 2);
        Arc(arcCenter, arcRadius, arcNormal, arcStartDir, arcAngle, 32);
        Fill();
        _canvas.SetStrokeColor(Color.Purple);
        Stroke();
    }
}