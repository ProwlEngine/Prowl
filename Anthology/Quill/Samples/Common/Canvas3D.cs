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
    private Float4x4 _viewMatrix;
    private Float4x4 _projectionMatrix;
    private Float4x4 _worldMatrix;
    private Float4x4 _viewProjectionMatrix;

    private List<Float3> _currentPath = new List<Float3>();
    private float _viewportWidth = 800;
    private float _viewportHeight = 600;
    private bool _isPathOpen = false;

    /// <summary>
    /// The canvas being wrapped
    /// </summary>
    public Canvas Canvas => _canvas;

    /// <summary>
    /// Current view matrix
    /// </summary>
    public Float4x4 ViewMatrix {
        get => _viewMatrix;
        set {
            _viewMatrix = value;
            UpdateViewProjectionMatrix();
        }
    }

    /// <summary>
    /// Current projection matrix
    /// </summary>
    public Float4x4 ProjectionMatrix {
        get => _projectionMatrix;
        set {
            _projectionMatrix = value;
            UpdateViewProjectionMatrix();
        }
    }

    /// <summary>
    /// Current world matrix
    /// </summary>
    public Float4x4 WorldMatrix {
        get => _worldMatrix;
        set {
            _worldMatrix = value;
            UpdateViewProjectionMatrix();
        }
    }

    /// <summary>
    /// Sets or gets the viewport width used for projection
    /// </summary>
    public float ViewportWidth {
        get => _viewportWidth;
        set => _viewportWidth = value;
    }

    /// <summary>
    /// Sets or gets the viewport height used for projection
    /// </summary>
    public float ViewportHeight {
        get => _viewportHeight;
        set => _viewportHeight = value;
    }

    /// <summary>
    /// Creates a new Canvas3D wrapper around an existing Canvas
    /// </summary>
    /// <param name="canvas">The Canvas to wrap</param>
    /// <param name="viewportWidth">Width of the viewport</param>
    /// <param name="viewportHeight">Height of the viewport</param>
    public Canvas3D(Canvas canvas, float viewportWidth = 800, float viewportHeight = 600)
    {
        _canvas = canvas;
        _viewportWidth = viewportWidth;
        _viewportHeight = viewportHeight;

        // Initialize with identity matrices
        _worldMatrix = Float4x4.Identity;
        _viewMatrix = Float4x4.Identity;
        _projectionMatrix = Float4x4.Identity;
        _viewProjectionMatrix = Float4x4.Identity;
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
    public void SetPerspectiveProjection(float fieldOfView, float aspectRatio, float nearPlane, float farPlane)
    {
        ProjectionMatrix = Float4x4.CreatePerspectiveFov(fieldOfView, aspectRatio, nearPlane, farPlane);
    }

    /// <summary>
    /// Sets up the camera view
    /// </summary>
    /// <param name="cameraPosition">Position of the camera</param>
    /// <param name="targetPosition">Point the camera is looking at</param>
    /// <param name="upVector">Up vector for the camera</param>
    public void SetLookAt(Float3 cameraPosition, Float3 targetPosition, Float3 upVector)
    {
        ViewMatrix = Float4x4.CreateLookAt(cameraPosition, targetPosition, upVector);
    }

    /// <summary>
    /// Sets the world transform matrix
    /// </summary>
    /// <param name="position">Position in world space</param>
    /// <param name="rotation">Rotation quaternion</param>
    /// <param name="scale">Scale factor</param>
    public void SetWorldTransform(Float3 position, Quaternion rotation, Float3 scale)
    {
        Float4x4 translationMatrix = Float4x4.CreateTranslation(position);
        Float4x4 rotationMatrix = Float4x4.CreateFromQuaternion(rotation);
        Float4x4 scaleMatrix = Float4x4.CreateScale(scale);

        WorldMatrix = scaleMatrix * rotationMatrix * translationMatrix;
    }

    /// <summary>
    /// Projects a 3D point to 2D screen coordinates
    /// </summary>
    /// <param name="point3D">The 3D point to project</param>
    /// <returns>2D screen coordinates</returns>
    public Float2 Project(Float3 point3D)
    {
        // Transform the point to clip space
        Float4 clipSpace = _viewProjectionMatrix * new Float4(point3D, 1.0f);

        // Skip points behind the camera or outside the frustum
        if (clipSpace.W <= 0 ||
            clipSpace.X < -clipSpace.W || clipSpace.X > clipSpace.W ||
            clipSpace.Y < -clipSpace.W || clipSpace.Y > clipSpace.W ||
            clipSpace.Z < -clipSpace.W || clipSpace.Z > clipSpace.W)
        {
            return new Float2(float.NaN, float.NaN); // Indicate point is not visible
        }

        // Perform perspective division to get NDC coordinates
        float ndcX = clipSpace.X / clipSpace.W;
        float ndcY = clipSpace.Y / clipSpace.W;

        // Convert to viewport coordinates
        float screenX = (ndcX + 1.0f) * 0.5f * _viewportWidth;
        float screenY = (1.0f - (ndcY + 1.0f) * 0.5f) * _viewportHeight; // Flip Y for screen coordinates

        return new Float2(screenX, screenY);
    }

    /// <summary>
    /// Determines if a 3D point would be visible when projected
    /// </summary>
    public bool IsVisible(Float3 point3D)
    {
        Float4 clipSpace = _viewProjectionMatrix * new Float4(point3D, 1.0f);

        return clipSpace.W > 0 &&
               clipSpace.X >= -clipSpace.W && clipSpace.X <= clipSpace.W &&
               clipSpace.Y >= -clipSpace.W && clipSpace.Y <= clipSpace.W &&
               clipSpace.Z >= -clipSpace.W && clipSpace.Z <= clipSpace.W;
    }

    /// <summary>
    /// Draws a line between two 3D points
    /// </summary>
    public void DrawLine(Float3 start, Float3 end, Color color, float width = 1.0f)
    {
        Float2 start2D = Project(start);
        Float2 end2D = Project(end);

        if (float.IsNaN(start2D.X) || float.IsNaN(end2D.X))
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
    public void MoveTo(float x, float y, float z)
    {
        if (!_isPathOpen)
            BeginPath();

        _currentPath.Add(new Float3(x, y, z));
    }

    /// <summary>
    /// Moves the current position to the specified 3D point without drawing a line.
    /// </summary>
    /// <param name="point">The point in 3D space</param>
    public void MoveTo(Float3 point)
    {
        MoveTo(point.X, point.Y, point.Z);
    }

    /// <summary>
    /// Draws a line from the current position to the specified 3D point.
    /// </summary>
    /// <param name="x">The x-coordinate in 3D space</param>
    /// <param name="y">The y-coordinate in 3D space</param>
    /// <param name="z">The z-coordinate in 3D space</param>
    public void LineTo(float x, float y, float z)
    {
        if (!_isPathOpen)
            BeginPath();

        _currentPath.Add(new Float3(x, y, z));
    }

    /// <summary>
    /// Draws a line from the current position to the specified 3D point.
    /// </summary>
    /// <param name="point">The point in 3D space</param>
    public void LineTo(Float3 point)
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
        Float2? lastPoint = null;

        for (int i = 0; i < _currentPath.Count; i++)
        {
            Float2 point2D = Project(_currentPath[i]);

            if (!float.IsNaN(point2D.X))
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
    public void DrawCubeStroked(Float3 center, float size)
    {
        float halfSize = size * 0.5f;

        // Define the 8 vertices of the cube
        Float3[] vertices = new Float3[8];
        vertices[0] = new Float3(center.X - halfSize, center.Y - halfSize, center.Z - halfSize);
        vertices[1] = new Float3(center.X + halfSize, center.Y - halfSize, center.Z - halfSize);
        vertices[2] = new Float3(center.X + halfSize, center.Y + halfSize, center.Z - halfSize);
        vertices[3] = new Float3(center.X - halfSize, center.Y + halfSize, center.Z - halfSize);
        vertices[4] = new Float3(center.X - halfSize, center.Y - halfSize, center.Z + halfSize);
        vertices[5] = new Float3(center.X + halfSize, center.Y - halfSize, center.Z + halfSize);
        vertices[6] = new Float3(center.X + halfSize, center.Y + halfSize, center.Z + halfSize);
        vertices[7] = new Float3(center.X - halfSize, center.Y + halfSize, center.Z + halfSize);

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
    public void DrawSphereStroked(Float3 center, float radius, int segments = 16)
    {
        // Draw longitude lines (vertical circles)
        for (int i = 0; i < segments; i++)
        {
            float angle = (float)(2 * Maths.PI * i / segments);
            BeginPath();

            for (int j = 0; j <= segments; j++)
            {
                float phi = (float)(Maths.PI * j / segments);
                float x = radius * (float)Maths.Sin(phi) * (float)Maths.Cos(angle);
                float y = radius * (float)Maths.Cos(phi);
                float z = radius * (float)Maths.Sin(phi) * (float)Maths.Sin(angle);

                Float3 point3D = new Float3(center.X + x, center.Y + y, center.Z + z);

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
            float phi = (float)(Maths.PI * j / segments);
            float radiusAtLatitude = radius * (float)Maths.Sin(phi);
            float y = radius * (float)Maths.Cos(phi);

            BeginPath();

            for (int i = 0; i <= segments; i++)
            {
                float angle = (float)(2 * Maths.PI * i / segments);
                float x = radiusAtLatitude * (float)Maths.Cos(angle);
                float z = radiusAtLatitude * (float)Maths.Sin(angle);

                Float3 point3D = new Float3(center.X + x, center.Y + y, center.Z + z);

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
    public void Arc(Float3 center, float radius, Float3 normal, Float3 startDir,
                   float angleInRadians, int segments = 16)
    {
        // Normalize vectors
        normal = Float3.Normalize(normal);
        startDir = Float3.Normalize(startDir);

        // Calculate perpendicular vector to both normal and startDir
        Float3 perpVector = Float3.Normalize(Float3.Cross(normal, startDir));

        BeginPath();

        for (int i = 0; i <= segments; i++)
        {
            float angle = angleInRadians * i / segments;

            // Rotate startDir around normal by angle
            Float3 rotatedDir = startDir * (float)Maths.Cos(angle) +
                                 perpVector * (float)Maths.Sin(angle);

            // Calculate point on arc
            Float3 point = center + rotatedDir * radius;

            if (i == 0)
                MoveTo(point);
            else
                LineTo(point);
        }
    }

    /// <summary>
    /// Draws a 3D Bezier curve
    /// </summary>
    public void BezierCurve(Float3 p0, Float3 p1, Float3 p2, Float3 p3, int segments = 16)
    {
        BeginPath();
        MoveTo(p0);

        for (int i = 1; i <= segments; i++)
        {
            float t = i / (float)segments;
            float u = 1.0f - t;

            // Cubic Bezier formula
            Float3 point = u * u * u * p0 +
                           3 * u * u * t * p1 +
                           3 * u * t * t * p2 +
                           t * t * t * p3;

            LineTo(point);
        }
    }

    public void Demo3D()
    {
        // Set up the camera and projection
        float aspectRatio = _viewportWidth / _viewportHeight;
        SetPerspectiveProjection((float)(Maths.PI / 4.0), aspectRatio, 0.1f, 100.0f);
        SetLookAt(new Float3(0, 0, -10), Float3.Zero, Float3.UnitY);

        // Create rotation based on time for animation
        float time = (float)Environment.TickCount / 1000.0f;
        Quaternion rotation = Quaternion.FromEuler(time * 0.5f, time * 0.3f, 0);

        _canvas.SetStrokeWidth(2.0f);

        // Draw a rotating cube
        _canvas.SetStrokeColor(Color.Red);
        SetWorldTransform(new Float3(-3f, 0, 0), rotation, Float3.One);
        DrawCubeStroked(Float3.Zero, 2.0f);

        // Draw a rotating sphere
        _canvas.SetStrokeColor(Color.Blue);
        SetWorldTransform(new Float3(3f, 0, 0), rotation, Float3.One);
        DrawSphereStroked(Float3.Zero, 1.0f, 16);


        SetWorldTransform(Float3.Zero, rotation, Float3.One);

        // Draw a 3D arc
        _canvas.SetStrokeWidth(6.0f);
        _canvas.SetFillColor(Color.Yellow);
        Float3 arcCenter = new Float3(0, 0, 0);
        float arcRadius = 2.0f;
        Float3 arcNormal = new Float3(0, 1, 0);
        Float3 arcStartDir = new Float3(1, 0, 0);
        float arcAngle = (float)(Maths.PI * 2);
        Arc(arcCenter, arcRadius, arcNormal, arcStartDir, arcAngle, 32);
        Fill();
        _canvas.SetStrokeColor(Color.Purple);
        Stroke();
    }
}