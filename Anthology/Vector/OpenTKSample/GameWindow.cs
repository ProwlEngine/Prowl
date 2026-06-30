// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using OpenTK.Graphics.OpenGL4;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

using Prowl.Vector;

namespace OpenTKSample;

public class GameWindow : OpenTK.Windowing.Desktop.GameWindow
{
    // Camera properties using your math library
    private Float3 _cameraPosition = new Float3(0.0f, 0.0f, -3.0f); // Start behind the object
    private Float3 _cameraForward = new Float3(0.0f, 0.0f, 1.0f);   // Looking forward (+Z)
    private Float3 _worldUp = Float3.UnitY;
    private float _fov = 70.0f;

    // Input state
    private bool _cursorLocked = true;
    private Float2 _lastMousePosition;
    private bool _firstMouse = true;
    private float _yaw = 0.0f;
    private float _pitch = 0.0f;

    private float _time = 0.0f;

    public GameWindow(GameWindowSettings gameWindowSettings, NativeWindowSettings nativeWindowSettings)
        : base(gameWindowSettings, nativeWindowSettings)
    {
    }

    protected override void OnLoad()
    {
        base.OnLoad();

        GL.ClearColor(0.1f, 0.1f, 0.1f, 1.0f);
        GL.Enable(EnableCap.DepthTest);

        // Lock cursor at startup
        CursorState = CursorState.Grabbed;

        // Update camera forward vector based on initial yaw/pitch
        UpdateCameraVectors();

        Gizmo.Initialize();

        Console.WriteLine("OpenTK Demo with Prowl Math Library");
        Console.WriteLine("Controls:");
        Console.WriteLine("- W/S: Move forward/backward");
        Console.WriteLine("- A/D: Strafe left/right");
        Console.WriteLine("- Mouse: Look around");
        Console.WriteLine("- Tab: Toggle cursor lock");
        Console.WriteLine("- ESC: Exit");
    }

    private void UpdateCameraVectors()
    {
        // Calculate the new front vector from yaw and pitch
        Float3 direction = new Float3(
            Maths.Sin(Maths.ToRadians(_yaw)) * Maths.Cos(Maths.ToRadians(_pitch)),  // X component
            Maths.Sin(Maths.ToRadians(_pitch)),                                    // Y component  
            Maths.Cos(Maths.ToRadians(_yaw)) * Maths.Cos(Maths.ToRadians(_pitch))   // Z component
        );

        _cameraForward = Float3.Normalize(direction);
    }

    private Float3 GetCameraRight()
    {
        Float3 right = Float3.Normalize(Float3.Cross(_worldUp, _cameraForward));
        return right;
    }

    protected override void OnUpdateFrame(FrameEventArgs args)
    {
        base.OnUpdateFrame(args);

        if (KeyboardState.IsKeyDown(Keys.Escape))
        {
            Close();
        }

        // Toggle cursor lock with Tab key
        if (KeyboardState.IsKeyPressed(Keys.Tab))
        {
            _cursorLocked = !_cursorLocked;
            CursorState = _cursorLocked ? CursorState.Grabbed : CursorState.Normal;

            // Reset first mouse when toggling to prevent camera jumping
            _firstMouse = true;
        }

        // Camera movement using proper camera-relative directions
        float cameraSpeed = 2.5f * (float)args.Time;

        // W/S: Move forward/backward relative to camera
        if (KeyboardState.IsKeyDown(Keys.W))
            _cameraPosition += _cameraForward * cameraSpeed;
        if (KeyboardState.IsKeyDown(Keys.S))
            _cameraPosition -= _cameraForward * cameraSpeed;

        // A/D: Strafe left/right relative to camera
        Float3 cameraRight = GetCameraRight();
        if (KeyboardState.IsKeyDown(Keys.A))
            _cameraPosition -= cameraRight * cameraSpeed;
        if (KeyboardState.IsKeyDown(Keys.D))
            _cameraPosition += cameraRight * cameraSpeed;

        // Q/E: Move up/down relative to world up
        if (KeyboardState.IsKeyDown(Keys.Q))
            _cameraPosition -= _worldUp * cameraSpeed;
        if (KeyboardState.IsKeyDown(Keys.E))
            _cameraPosition += _worldUp * cameraSpeed;

        _time += (float)args.Time;
    }

    protected override void OnMouseMove(MouseMoveEventArgs e)
    {
        base.OnMouseMove(e);

        Float2 mousePosition = new Float2(e.X, e.Y);

        if (_firstMouse)
        {
            _lastMousePosition = mousePosition;
            _firstMouse = false;
        }

        Float2 offset = mousePosition - _lastMousePosition;
        _lastMousePosition = mousePosition;

        float sensitivity = 0.1f;
        offset *= sensitivity;

        _yaw += offset.X;
        _pitch -= offset.Y;

        // Constrain pitch to prevent flipping
        _pitch = Maths.Clamp(_pitch, -89.0f, 89.0f);

        // Update camera vectors
        UpdateCameraVectors();
    }

    protected override void OnRenderFrame(FrameEventArgs args)
    {
        base.OnRenderFrame(args);

        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        GL.Enable(EnableCap.CullFace);
        GL.Enable(EnableCap.LineSmooth);
        GL.CullFace(TriangleFace.Back);

        GeometryDemo.DrawGeometryDemo(_time);

        // View matrix - construct target from position + forward
        Float3 cameraTarget = _cameraPosition + _cameraForward;
        Float4x4 viewMatrix = Float4x4.CreateLookAt(_cameraPosition, cameraTarget, _worldUp);

        // Projection matrix
        float aspectRatio = (float)Size.X / Size.Y;
        Float4x4 projectionMatrix = Float4x4.CreatePerspectiveFov(
            Maths.ToRadians(_fov),
            aspectRatio,
            0.1f,
            100.0f
        );

        Float4x4 viewProjection = projectionMatrix * viewMatrix;
        Gizmo.Render(viewProjection);

        SwapBuffers();

        Gizmo.Clear();
    }

    protected override void OnResize(ResizeEventArgs e)
    {
        base.OnResize(e);
        GL.Viewport(0, 0, Size.X, Size.Y);
    }

    protected override void OnUnload()
    {
        base.OnUnload();

        Gizmo.Dispose();
    }
}
