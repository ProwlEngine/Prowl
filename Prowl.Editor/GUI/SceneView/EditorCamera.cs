using System;
using System.Collections.Generic;

using Prowl.Editor.Core;
using Prowl.Runtime;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Editor.GUI.SceneView;

/// <summary>
/// Editor camera controller with orbit, pan, zoom, and FPS navigation modes.
/// Manages a hidden runtime Camera that renders the scene to a RenderTexture.
/// </summary>
/// <summary>
/// Cursor lock context for the scene view, locks to the center of the scene panel.
/// Paper-logical coordinates now equal window-logical pixels (winSize space), which
/// is also what the OS expects for cursor position on all platforms, so no scaling needed.
/// </summary>
public class SceneViewLockContext : CursorLockContext
{
    /// <summary>Panel origin in Paper-logical (= window-logical) coordinates.</summary>
    public Float2 PanelOrigin;
    /// <summary>Panel size in Paper-logical (= window-logical) coordinates.</summary>
    public Float2 PanelSize;

    public override Int2 GetLockCenter()
    {
        var fb = Window.InternalWindow.FramebufferSize;
        var win = Window.InternalWindow.Size;
        float cs = Window.ContentScale;
        float csFbWin = win.X > 0 ? (float)fb.X / win.X : 1f;
        // Paper coords are in [0, fbSize/cs]; OS cursor expects winSize coords.
        // scale = cs/csFbWin converts paper -> winSize (== 1 on macOS, == cs on DPI-unaware Windows).
        float scale = csFbWin > 0 ? cs / csFbWin : 1f;
        float centerX = (PanelOrigin.X + PanelSize.X / 2) * scale;
        float centerY = (PanelOrigin.Y + PanelSize.Y / 2) * scale;
        return new Int2((int)centerX, (int)centerY);
    }
}

public class EditorCamera
{
    private GameObject _cameraObject;
    private Camera _camera;
    private RenderTexture? _renderTarget;
    private SceneViewLockContext _lockContext = new();

    // Camera state
    private Float3 _position = new Float3(0, 5, -15);
    private float _yaw = 0f;
    private float _pitch = 15f;
    private float _moveSpeed = 5f;
    private double _speedChangedTime;

    /// <summary>Current fly speed.</summary>
    public float MoveSpeed => _moveSpeed;

    /// <summary>Time (UnscaledTotalTime) when move speed last changed via scroll. Used for HUD indicators.</summary>
    public double SpeedChangedTime => _speedChangedTime;

    // Orbit distance (pivot is always _position + forward * _orbitDistance)
    private float _orbitDistance = 10f;

    // Toggles
    public bool ShowGrid { get; set; } = true;
    public bool ShowGizmos { get; set; } = true;

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

    /// <summary>Restore full navigation state (position + yaw/pitch). Pitch is clamped.</summary>
    public void SetPose(Float3 position, float yaw, float pitch)
    {
        _position = position;
        _yaw = yaw;
        _pitch = MathF.Max(-89f, MathF.Min(89f, pitch));
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
    /// Whether to copy image effects from the scene's main camera onto the editor camera.
    /// </summary>
    public bool UseSceneEffects { get; set; } = true;

    /// <summary>
    /// Render the scene from this camera's perspective.
    /// </summary>
    public void Render(Scene scene, bool drawUI = true)
    {
        if (_renderTarget == null) return;

        // Copy image effects from the scene's main camera (if enabled)
        if (UseSceneEffects)
            CopySceneEffects(scene);
        else
            _camera.Effects.Clear();

        // Update camera pixel dimensions
        _camera.UpdateRenderData();

        // Add camera object to scene temporarily if needed
        bool wasInScene = _cameraObject.Scene != null;
        if (!wasInScene)
            scene.Add(_cameraObject);

        // Build rendering data
        var renderData = new RenderingData
        {
            DisplayGizmos = ShowGizmos,
            DisplayGrid = ShowGrid,
            IsSceneView = true,
            SkipUI = !drawUI
        };

        // Render
        var pipeline = _camera.Pipeline ?? DefaultRenderPipeline.Default;
        pipeline.Render(_camera, renderData);

        // Remove from scene if we added it
        if (!wasInScene)
            scene.Remove(_cameraObject);
    }

    // Cached cloned effects for the editor camera. Persistent across frames so
    // temporal effects (TAA, motion blur) keep their history buffers intact.
    private readonly List<ImageEffect> _clonedEffects = new();
    private Camera? _lastSceneCamera;

    /// <summary>
    /// Drop references to scene-derived objects: the cached scene <see cref="Camera"/> and the
    /// cloned <see cref="ImageEffect"/> instances (which can be user-script types). These persist
    /// across frames, so they must be released before a script hot-reload unloads the ALC.
    /// </summary>
    public void ReleaseSceneReferences()
    {
        DisposeClonedEffects();
        _camera.Effects.Clear();
        _lastSceneCamera = null;
    }

    /// <summary>
    /// Sync image effects from the scene's main camera. Clones effect instances on first
    /// use or when the effect list changes, then uses DeserializeInto each frame to copy
    /// settings without destroying internal state (TAA history, etc.).
    /// </summary>
    private void CopySceneEffects(Scene scene)
    {
        Camera? sceneCamera = FindSceneCamera(scene);
        if (sceneCamera == null || sceneCamera == _camera)
        {
            _camera.Effects.Clear();
            DisposeClonedEffects();
            _lastSceneCamera = null;
            return;
        }

        // Build a filtered list of non-null effects from the scene camera so null
        // entries don't cause index misalignment between the source and clone lists.
        var sourceEffects = new List<ImageEffect>();
        foreach (var effect in sceneCamera.Effects)
        {
            if (effect != null)
                sourceEffects.Add(effect);
        }

        // Check if the effect list structure changed (different types, count, or camera)
        bool needsReclone = sceneCamera != _lastSceneCamera
            || sourceEffects.Count != _clonedEffects.Count;

        if (!needsReclone)
        {
            for (int i = 0; i < sourceEffects.Count; i++)
            {
                if (sourceEffects[i].GetType() != _clonedEffects[i].GetType())
                {
                    needsReclone = true;
                    break;
                }
            }
        }

        if (needsReclone)
        {
            // Dispose old clones first so persistent GPU resources (TAA history,
            // adaptation buffers, etc.) are freed before creating new instances.
            DisposeClonedEffects();

            foreach (var effect in sourceEffects)
            {
                try
                {
                    var echo = Echo.Serializer.Serialize(effect);
                    var clone = Echo.Serializer.Deserialize(echo, effect.GetType()) as ImageEffect;
                    if (clone != null)
                        _clonedEffects.Add(clone);
                }
                catch
                {
                    // Fallback: create a blank instance
                    if (Activator.CreateInstance(effect.GetType()) is ImageEffect blank)
                        _clonedEffects.Add(blank);
                }
            }
            _lastSceneCamera = sceneCamera;
        }
        else
        {
            // Sync settings into existing clones (preserves internal state like TAA history)
            for (int i = 0; i < sourceEffects.Count; i++)
            {
                try
                {
                    var echo = Echo.Serializer.Serialize(sourceEffects[i]);
                    Echo.Serializer.DeserializeInto(echo, _clonedEffects[i]);
                }
                catch { }
            }
        }

        // Apply cloned effects to editor camera
        _camera.Effects.Clear();
        _camera.Effects.AddRange(_clonedEffects);
        _camera.HDR = sceneCamera.HDR;
    }

    /// <summary>
    /// Dispose all cloned effects so persistent GPU resources (history buffers, adaptation
    /// RTs, etc.) are properly freed.
    /// </summary>
    private void DisposeClonedEffects()
    {
        foreach (var effect in _clonedEffects)
        {
            try { effect.OnDisable(); }
            catch { }
        }
        _clonedEffects.Clear();
    }

    private static Camera? FindSceneCamera(Scene scene)
    {
        // First try to find a camera tagged "Main Camera"
        foreach (var go in scene.AllObjects)
        {
            if (go.HideFlags.HasFlag(HideFlags.HideAndDontSave)) continue;
            if (!go.CompareTag("Main Camera")) continue;

            var cam = go.GetComponent<Camera>();
            if (cam != null) return cam;
        }

        // Fallback: first visible Camera in the scene
        foreach (var go in scene.AllObjects)
        {
            if (go.HideFlags.HasFlag(HideFlags.HideAndDontSave)) continue;

            var cam = go.GetComponent<Camera>();
            if (cam != null) return cam;
        }

        return null;
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

        // Scroll to dolly forward/back (also adjusts orbit distance)
        if (scroll != 0 && !Input.GetMouseButton(1))
        {
            Float3 forward = GetForwardFromAngles();
            float dolly = scroll * _orbitDistance * 0.1f;
            _position += forward * dolly;
            _orbitDistance = MathF.Max(0.1f, _orbitDistance - dolly);
            UpdateTransform();
            consumed = true;
        }

        // Alt + Left mouse = orbit around pivot (pivot = position + forward * distance)
        if (Input.IsAltPressed && Input.GetMouseButton(0))
        {
            Float2 delta = Input.MouseDelta;
            Float3 pivot = _position + GetForwardFromAngles() * _orbitDistance;

            _yaw += delta.X * 0.3f;
            _pitch += delta.Y * 0.3f;
            _pitch = MathF.Max(-89f, MathF.Min(89f, _pitch));

            _position = pivot - GetForwardFromAngles() * _orbitDistance;
            UpdateTransform();
            consumed = true;
        }

        // Alt + Right mouse = dolly zoom
        if (Input.IsAltPressed && Input.GetMouseButton(1))
        {
            Float2 delta = Input.MouseDelta;
            float zoomDelta = (delta.X + delta.Y) * 0.02f * _orbitDistance;
            Float3 forward = GetForwardFromAngles();
            _position += forward * zoomDelta;
            _orbitDistance = MathF.Max(0.1f, _orbitDistance - zoomDelta);
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
            _lockContext.PanelOrigin = panelOrigin;
            _lockContext.PanelSize = panelSize;
            Input.PushLockContext(_lockContext);
            Input.LockCursor();
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
                _speedChangedTime = Runtime.Time.UnscaledTotalTime;
            }

            UpdateTransform();
            consumed = true;
        }

        if (Input.GetMouseButtonUp(1))
        {
            Input.UnlockCursor();
            Input.PopLockContext();
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
        Float3 min = new(float.MaxValue);
        Float3 max = new(float.MinValue);
        bool anyBounds = false;
        int goCount = 0;
        Float3 positionSum = Float3.Zero;

        foreach (var go in Selection.GetSelected<GameObject>())
        {
            goCount++;
            positionSum += go.Transform.Position;
            AccumulateRendererBounds(go, ref min, ref max, ref anyBounds);
        }
        if (goCount == 0) return;

        // Frame size is driven by renderer bounds when any are present; otherwise fall
        // back to the Transform position(s) so even empty GOs (lights, cameras) focus.
        Float3 target;
        float radius;
        if (anyBounds)
        {
            target = (min + max) * 0.5f;
            Float3 size = max - min;
            radius = MathF.Max(0.5f, Float3.Length(size) * 0.5f);
        }
        else
        {
            target = positionSum / goCount;
            radius = 0.5f;
        }

        // Distance that fits the object in the vertical FOV, with a little padding.
        float fovRad = _camera.FieldOfView * MathF.PI / 180f;
        float dist = radius / MathF.Tan(fovRad * 0.5f) + radius;
        dist = MathF.Max(dist, 0.5f);

        // Set orbit distance and position camera to look at target
        _orbitDistance = dist;
        _position = target - GetForwardFromAngles() * dist;
        UpdateTransform();
    }

    private static void AccumulateRendererBounds(GameObject go, ref Float3 min, ref Float3 max, ref bool any)
    {
        var mr = go.GetComponent<MeshRenderer>();
        if (mr != null && mr.Mesh.Res != null)
        {
            var wb = mr.Mesh.Res.bounds.TransformBy(go.Transform.LocalToWorldMatrix);
            min = new Float3(MathF.Min(min.X, wb.Min.X), MathF.Min(min.Y, wb.Min.Y), MathF.Min(min.Z, wb.Min.Z));
            max = new Float3(MathF.Max(max.X, wb.Max.X), MathF.Max(max.Y, wb.Max.Y), MathF.Max(max.Z, wb.Max.Z));
            any = true;
        }
        var smr = go.GetComponent<SkinnedMeshRenderer>();
        if (smr != null && smr.SharedMesh.Res != null)
        {
            var wb = smr.SharedMesh.Res.bounds.TransformBy(go.Transform.LocalToWorldMatrix);
            min = new Float3(MathF.Min(min.X, wb.Min.X), MathF.Min(min.Y, wb.Min.Y), MathF.Min(min.Z, wb.Min.Z));
            max = new Float3(MathF.Max(max.X, wb.Max.X), MathF.Max(max.Y, wb.Max.Y), MathF.Max(max.Z, wb.Max.Z));
            any = true;
        }
        foreach (var child in go.Children)
            AccumulateRendererBounds(child, ref min, ref max, ref any);
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

    /// <summary>Compute forward direction from yaw/pitch angles without reading the transform.</summary>
    private Float3 GetForwardFromAngles()
    {
        float yawRad = _yaw * MathF.PI / 180f;
        float pitchRad = _pitch * MathF.PI / 180f;
        float cosPitch = MathF.Cos(pitchRad);
        return new Float3(
            MathF.Sin(yawRad) * cosPitch,
            -MathF.Sin(pitchRad),
            MathF.Cos(yawRad) * cosPitch
        );
    }

    private void UpdateTransform()
    {
        _cameraObject.Transform.Position = _position;
        _cameraObject.Transform.LocalEulerAngles = new Float3(_pitch, _yaw, 0);
    }

    public void Dispose()
    {
        DisposeClonedEffects();
        _renderTarget?.Dispose();
        _renderTarget = null;
    }
}
