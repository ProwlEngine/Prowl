using Prowl.Vector;
using System.Drawing;

namespace Prowl.Quill;

/// <summary>
/// A wrapper around Canvas that supports 3D rendering operations by projecting 3D points to 2D.
/// </summary>
public class Canvas3D
{
    private readonly Canvas _canvas;
    private Matrix4x4 _viewMatrix;
    private Matrix4x4 _projectionMatrix;
    private Matrix4x4 _worldMatrix;
    private Matrix4x4 _viewProjectionMatrix;

    private List<Vector3> _currentPath = new List<Vector3>();
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
    public Matrix4x4 ViewMatrix {
        get => _viewMatrix;
        set {
            _viewMatrix = value;
            UpdateViewProjectionMatrix();
        }
    }

    /// <summary>
    /// Current projection matrix
    /// </summary>
    public Matrix4x4 ProjectionMatrix {
        get => _projectionMatrix;
        set {
            _projectionMatrix = value;
            UpdateViewProjectionMatrix();
        }
    }

    /// <summary>
    /// Current world matrix
    /// </summary>
    public Matrix4x4 WorldMatrix {
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
        _worldMatrix = Matrix4x4.Identity;
        _viewMatrix = Matrix4x4.Identity;
        _projectionMatrix = Matrix4x4.Identity;
        _viewProjectionMatrix = Matrix4x4.Identity;
    }

    /// <summary>
    /// Updates the combined view-projection matrix
    /// </summary>
    private void UpdateViewProjectionMatrix()
    {
        _viewProjectionMatrix = _worldMatrix * _viewMatrix * _projectionMatrix;
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
        ProjectionMatrix = Matrix4x4.CreatePerspectiveFieldOfView(fieldOfView, aspectRatio, nearPlane, farPlane);
    }

    /// <summary>
    /// Sets up the camera view
    /// </summary>
    /// <param name="cameraPosition">Position of the camera</param>
    /// <param name="targetPosition">Point the camera is looking at</param>
    /// <param name="upVector">Up vector for the camera</param>
    public void SetLookAt(Vector3 cameraPosition, Vector3 targetPosition, Vector3 upVector)
    {
        ViewMatrix = Matrix4x4.CreateLookAt(cameraPosition, targetPosition, upVector);
    }

    /// <summary>
    /// Sets the world transform matrix
    /// </summary>
    /// <param name="position">Position in world space</param>
    /// <param name="rotation">Rotation quaternion</param>
    /// <param name="scale">Scale factor</param>
    public void SetWorldTransform(Vector3 position, Quaternion rotation, Vector3 scale)
    {
        Matrix4x4 translationMatrix = Matrix4x4.CreateTranslation(position);
        Matrix4x4 rotationMatrix = Matrix4x4.CreateFromQuaternion(rotation);
        Matrix4x4 scaleMatrix = Matrix4x4.CreateScale(scale);

        WorldMatrix = scaleMatrix * rotationMatrix * translationMatrix;
    }

    /// <summary>
    /// Projects a 3D point to 2D screen coordinates
    /// </summary>
    /// <param name="point3D">The 3D point to project</param>
    /// <returns>2D screen coordinates</returns>
    public Vector2 Project(Vector3 point3D)
    {
        // Transform the point to clip space
        Vector4 clipSpace = Vector4.Transform(new Vector4(point3D, 1.0f), _viewProjectionMatrix);

        // Skip points behind the camera or outside the frustum
        if (clipSpace.w <= 0 ||
            clipSpace.x < -clipSpace.w || clipSpace.x > clipSpace.w ||
            clipSpace.y < -clipSpace.w || clipSpace.y > clipSpace.w ||
            clipSpace.z < -clipSpace.w || clipSpace.z > clipSpace.w)
        {
            return new Vector2(double.NaN, double.NaN); // Indicate point is not visible
        }

        // Perform perspective division to get NDC coordinates
        double ndcX = clipSpace.x / clipSpace.w;
        double ndcY = clipSpace.y / clipSpace.w;

        // Convert to viewport coordinates
        double screenX = (ndcX + 1.0f) * 0.5f * _viewportWidth;
        double screenY = (1.0f - (ndcY + 1.0f) * 0.5f) * _viewportHeight; // Flip Y for screen coordinates

        return new Vector2(screenX, screenY);
    }

    /// <summary>
    /// Determines if a 3D point would be visible when projected
    /// </summary>
    public bool IsVisible(Vector3 point3D)
    {
        Vector4 clipSpace = Vector4.Transform(new Vector4(point3D, 1.0f), _viewProjectionMatrix);

        return clipSpace.w > 0 &&
               clipSpace.x >= -clipSpace.w && clipSpace.x <= clipSpace.w &&
               clipSpace.y >= -clipSpace.w && clipSpace.y <= clipSpace.w &&
               clipSpace.z >= -clipSpace.w && clipSpace.z <= clipSpace.w;
    }

    /// <summary>
    /// Draws a line between two 3D points
    /// </summary>
    public void DrawLine(Vector3 start, Vector3 end, Color color, double width = 1.0f)
    {
        Vector2 start2D = Project(start);
        Vector2 end2D = Project(end);

        if (double.IsNaN(start2D.x) || double.IsNaN(end2D.x))
            return; // Skip if either point is not visible

        _canvas.SetStrokeColor(color);
        _canvas.SetStrokeWidth(width);
        _canvas.BeginPath();
        _canvas.MoveTo(start2D.x, start2D.y);
        _canvas.LineTo(end2D.x, end2D.y);
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

        _currentPath.Add(new Vector3(x, y, z));
    }

    /// <summary>
    /// Moves the current position to the specified 3D point without drawing a line.
    /// </summary>
    /// <param name="point">The point in 3D space</param>
    public void MoveTo(Vector3 point)
    {
        MoveTo(point.x, point.y, point.z);
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

        _currentPath.Add(new Vector3(x, y, z));
    }

    /// <summary>
    /// Draws a line from the current position to the specified 3D point.
    /// </summary>
    /// <param name="point">The point in 3D space</param>
    public void LineTo(Vector3 point)
    {
        LineTo(point.x, point.y, point.z);
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
        Vector2? lastPoint = null;

        for (int i = 0; i < _currentPath.Count; i++)
        {
            Vector2 point2D = Project(_currentPath[i]);

            if (!double.IsNaN(point2D.x))
            {
                if (firstPoint)
                {
                    _canvas.MoveTo(point2D.x, point2D.y);
                    firstPoint = false;
                }
                else
                {
                    // If we have a valid last point, draw a line
                    if (lastPoint.HasValue)
                    {
                        _canvas.LineTo(point2D.x, point2D.y);
                    }
                    else
                    {
                        // If previous points were invisible but this one is visible,
                        // we need to start a new segment
                        _canvas.MoveTo(point2D.x, point2D.y);
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
    public void DrawCubeStroked(Vector3 center, double size)
    {
        double halfSize = size * 0.5f;

        // Define the 8 vertices of the cube
        Vector3[] vertices = new Vector3[8];
        vertices[0] = new Vector3(center.x - halfSize, center.y - halfSize, center.z - halfSize);
        vertices[1] = new Vector3(center.x + halfSize, center.y - halfSize, center.z - halfSize);
        vertices[2] = new Vector3(center.x + halfSize, center.y + halfSize, center.z - halfSize);
        vertices[3] = new Vector3(center.x - halfSize, center.y + halfSize, center.z - halfSize);
        vertices[4] = new Vector3(center.x - halfSize, center.y - halfSize, center.z + halfSize);
        vertices[5] = new Vector3(center.x + halfSize, center.y - halfSize, center.z + halfSize);
        vertices[6] = new Vector3(center.x + halfSize, center.y + halfSize, center.z + halfSize);
        vertices[7] = new Vector3(center.x - halfSize, center.y + halfSize, center.z + halfSize);

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
    public void DrawSphereStroked(Vector3 center, double radius, int segments = 16)
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

                Vector3 point3D = new Vector3(center.x + x, center.y + y, center.z + z);

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

                Vector3 point3D = new Vector3(center.x + x, center.y + y, center.z + z);

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
    public void Arc(Vector3 center, double radius, Vector3 normal, Vector3 startDir,
                   double angleInRadians, int segments = 16)
    {
        // Normalize vectors
        normal = Vector3.Normalize(normal);
        startDir = Vector3.Normalize(startDir);

        // Calculate perpendicular vector to both normal and startDir
        Vector3 perpVector = Vector3.Normalize(Vector3.Cross(normal, startDir));

        BeginPath();

        for (int i = 0; i <= segments; i++)
        {
            double angle = angleInRadians * i / segments;

            // Rotate startDir around normal by angle
            Vector3 rotatedDir = startDir * (double)Math.Cos(angle) +
                                 perpVector * (double)Math.Sin(angle);

            // Calculate point on arc
            Vector3 point = center + rotatedDir * radius;

            if (i == 0)
                MoveTo(point);
            else
                LineTo(point);
        }
    }

    /// <summary>
    /// Draws a 3D Bezier curve
    /// </summary>
    public void BezierCurve(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, int segments = 16)
    {
        BeginPath();
        MoveTo(p0);

        for (int i = 1; i <= segments; i++)
        {
            double t = i / (double)segments;
            double u = 1.0f - t;

            // Cubic Bezier formula
            Vector3 point = u * u * u * p0 +
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
        SetLookAt(new Vector3(0, 0, -10), Vector3.zero, Vector3.up);

        // Create rotation based on time for animation
        double time = (double)Environment.TickCount / 1000.0f;
        Quaternion rotation = Quaternion.CreateFromYawPitchRoll(time * 0.5f, time * 0.3f, 0);

        _canvas.SetStrokeWidth(2.0f);

        // Draw a rotating cube
        _canvas.SetStrokeColor(Color.Red);
        SetWorldTransform(new Vector3(-3f, 0, 0), rotation, Vector3.one);
        DrawCubeStroked(Vector3.zero, 2.0f);

        // Draw a rotating sphere
        _canvas.SetStrokeColor(Color.Blue);
        SetWorldTransform(new Vector3(3f, 0, 0), rotation, Vector3.one);
        DrawSphereStroked(Vector3.zero, 1.0f, 16);


        SetWorldTransform(Vector3.zero, rotation, Vector3.one);

        // Draw a 3D arc
        _canvas.SetStrokeWidth(6.0f);
        _canvas.SetFillColor(Color.Yellow);
        Vector3 arcCenter = new Vector3(0, 0, 0);
        double arcRadius = 2.0f;
        Vector3 arcNormal = new Vector3(0, 1, 0);
        Vector3 arcStartDir = new Vector3(1, 0, 0);
        double arcAngle = (double)(Math.PI * 2);
        Arc(arcCenter, arcRadius, arcNormal, arcStartDir, arcAngle, 32);
        Fill();
        _canvas.SetStrokeColor(Color.Purple);
        Stroke();
    }
}