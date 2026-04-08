using System;

using Prowl.Runtime;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Editor;

/// <summary>
/// Editor camera controller with orbit, pan, zoom, and FPS navigation modes.
/// Manages a hidden runtime Camera that renders the scene to a RenderTexture.
/// </summary>
public class EditorCamera
{
    private GameObject _cameraObject;
    private Camera _camera;
    private RenderTexture? _renderTarget;

    // Camera state — position + euler angles (no orbit)
    private Float3 _position = new Float3(0, 5, -15);
    private float _yaw = 0f;
    private float _pitch = 15f;
    private float _moveSpeed = 5f;

    // Grid
    public bool ShowGrid { get; set; } = true;
    private readonly EditorGrid _grid = new();

    public Camera Camera => _camera;
    public RenderTexture? RenderTarget => _renderTarget;
    public Float3 Position => _position;
    public float Yaw => _yaw;
    public float Pitch => _pitch;
    public Float3 Forward => _cameraObject.Transform.Forward;

    /// <summary>Set the camera position directly.</summary>
    public void SetPosition(Float3 position)
    {
        _position = position;
        UpdateTransform();
    }

    public EditorCamera()
    {
        _cameraObject = new GameObject("EditorCamera");
        _cameraObject.Enabled = true;
        _cameraObject.HideFlags = HideFlags.HideAndDontSave | HideFlags.NoGizmos;

        _camera = _cameraObject.AddComponent<Camera>();
        _camera.FieldOfView = 60f;
        _camera.NearClipPlane = 0.01f;
        _camera.FarClipPlane = 1000f;
        _camera.ClearFlags = CameraClearFlags.Skybox;

        UpdateTransform();
    }

    /// <summary>
    /// Ensure the render target matches the given size.
    /// </summary>
    public void EnsureRenderTarget(uint width, uint height)
    {
        if (width == 0 || height == 0) return;

        if (_renderTarget == null || _renderTarget.Width != width || _renderTarget.Height != height)
        {
            _renderTarget?.Dispose();
            _renderTarget = new RenderTexture(
                (int)width, (int)height, true,
                new[] { TextureImageFormat.Color4b });

            _camera.Target = _renderTarget;
        }
    }

    /// <summary>
    /// Render the scene from this camera's perspective.
    /// </summary>
    public void Render(Scene scene)
    {
        if (_renderTarget == null) return;

        // Update camera pixel dimensions
        _camera.UpdateRenderData();

        // Add camera object to scene temporarily if needed
        bool wasInScene = _cameraObject.Scene != null;
        if (!wasInScene)
            scene.Add(_cameraObject);

        // Collect renderables/lights from all components
        scene.RenderCollect();

        // Draw editor grid
        if (ShowGrid)
            _grid.Draw(scene, _position);

        // Build rendering data
        var renderData = new RenderingData
        {
            DisplayGizmo = true,
            GridMatrix = Float4x4.Identity,
            GridColor = new Color(0.5f, 0.5f, 0.5f, 0.3f),
            GridSizes = new Float3(1f, 5f, 10f),
        };

        // Render
        var pipeline = _camera.Pipeline ?? DefaultRenderPipeline.Default;
        pipeline.Render(_camera, renderData);

        // Remove from scene if we added it
        if (!wasInScene)
            scene.Remove(_cameraObject);
    }

    // ================================================================
    //  Camera Controls
    // ================================================================

    /// <summary>
    /// Process input for the editor camera. Call each frame with delta time.
    /// Right-click + mouse look, WASD to move, middle mouse to pan, scroll to zoom/speed.
    /// </summary>
    public bool ProcessInput(float dt, bool isHovered, Float2 mousePos, Float2 panelOrigin, Float2 panelSize)
    {
        if (!isHovered) return false;

        bool consumed = false;
        float scroll = Input.MouseWheelDelta;

        // Scroll to move forward/back
        if (scroll != 0 && !Input.GetMouseButton(1))
        {
            Float3 forward = _cameraObject.Transform.Forward;
            _position += forward * scroll * _moveSpeed * 0.3f;
            UpdateTransform();
            consumed = true;
        }

        // Middle mouse = pan
        if (Input.GetMouseButton(2))
        {
            Float2 delta = Input.MouseDelta;
            float panScale = 0.01f * _moveSpeed;
            Float3 right = _cameraObject.Transform.Right;
            Float3 up = _cameraObject.Transform.Up;
            _position -= right * delta.X * panScale;
            _position += up * delta.Y * panScale;
            UpdateTransform();
            consumed = true;
        }

        // Right mouse = look + WASD (with cursor lock)
        if (Input.GetMouseButtonDown(1))
        {
            var center = new Int2((int)(panelOrigin.X + panelSize.X / 2), (int)(panelOrigin.Y + panelSize.Y / 2));
            Input.LockCursor(center);
        }

        if (Input.GetMouseButton(1))
        {
            Float2 delta = Input.MouseDelta;

            // Mouse look
            _yaw += delta.X * 0.2f;
            _pitch += delta.Y * 0.2f;
            _pitch = MathF.Max(-89f, MathF.Min(89f, _pitch));

            // WASD movement
            Float3 forward = _cameraObject.Transform.Forward;
            Float3 right = _cameraObject.Transform.Right;
            Float3 move = Float3.Zero;

            if (Input.GetKey(KeyCode.W)) move += forward;
            if (Input.GetKey(KeyCode.S)) move -= forward;
            if (Input.GetKey(KeyCode.D)) move += right;
            if (Input.GetKey(KeyCode.A)) move -= right;
            if (Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.Space)) move += Float3.UnitY;
            if (Input.GetKey(KeyCode.Q)) move -= Float3.UnitY;

            float speed = _moveSpeed * dt;
            if (Input.IsShiftPressed) speed *= 3f;

            if (Float3.LengthSquared(move) > 0)
            {
                move = Float3.Normalize(move) * speed;
                _position += move;
            }

            // Scroll adjusts move speed while RMB held
            if (scroll != 0)
            {
                _moveSpeed *= 1f + scroll * 0.15f;
                _moveSpeed = MathF.Max(0.5f, MathF.Min(100f, _moveSpeed));
            }

            UpdateTransform();
            consumed = true;
        }

        if (Input.GetMouseButtonUp(1))
        {
            Input.UnlockCursor();
        }

        // F = focus on selection
        if (ShortcutManager.IsPressed("Scene/Focus"))
        {
            FocusSelection();
            consumed = true;
        }

        return consumed;
    }

    public void FocusSelection()
    {
        Float3 center = Float3.Zero;
        int count = 0;
        foreach (var go in Selection.GetSelected<GameObject>())
        {
            center += go.Transform.Position;
            count++;
        }

        if (count > 0)
        {
            Float3 target = center / count;
            Float3 dir = Float3.Normalize(target - _position);
            float dist = Float3.Length(target - _position);
            _position = target - dir * MathF.Min(dist, 10f);
            UpdateTransform();
        }
    }

    public void SetOrientation(float yaw, float pitch)
    {
        _yaw = yaw;
        _pitch = MathF.Max(-89f, MathF.Min(89f, pitch));
        UpdateTransform();
    }

    public Ray ScreenPointToRay(Float2 screenPos, Float2 panelSize)
    {
        return _camera.ScreenPointToRay(screenPos, panelSize);
    }

    private void UpdateTransform()
    {
        _cameraObject.Transform.Position = _position;
        _cameraObject.Transform.LocalEulerAngles = new Float3(_pitch, _yaw, 0);
    }

    public void Dispose()
    {
        _renderTarget?.Dispose();
        _renderTarget = null;
    }
}
