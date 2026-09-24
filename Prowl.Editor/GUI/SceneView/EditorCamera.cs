using System;
using System.Collections.Generic;

using Prowl.Editor.Core;
using Prowl.Graphite;
using Prowl.Runtime;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using RenderTexture = Prowl.Runtime.Resources.RenderTexture;

namespace Prowl.Editor.GUI.SceneView;

/// <summary>
/// Editor camera controller with orbit, pan, zoom, and FPS navigation modes.
/// Manages a hidden runtime Camera that renders the scene to a RenderTexture.
/// </summary>
public class EditorCamera
{
    private GameObject _cameraObject;
    private Camera _camera;
    private RenderTexture? _renderTarget;
    private PanelLockContext _lockContext = new();

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

    // Orbit distance and the pivot captured at the start of an orbit gesture.
    private float _orbitDistance = 10f;
    private Float3 _orbitPivot;
    private bool _wasOrbiting;

    // Toggles
    public bool ShowGrid { get; set; } = true;
    public bool ShowGizmos { get; set; } = true;

    public Camera Camera => _camera;
    public RenderTexture? RenderTarget => _renderTarget;
    public Float3 Position => _position;
    public float Yaw => _yaw;
    public float Pitch => _pitch;
    public Float3 Forward => _cameraObject.Transform.Forward;

    /// <summary>World rotation matching the camera's current yaw/pitch.</summary>
    public Quaternion Rotation => _cameraObject.Transform.Rotation;

    /// <summary>
    /// The point the view is centred on: straight ahead of the camera at the current orbit/zoom
    /// distance. "Move to View" drops objects here so they land in front of the camera instead of
    /// inside it (where they'd be clipped away by the near plane).
    /// </summary>
    public Float3 ViewFocusPoint => _position + _cameraObject.Transform.Forward * _orbitDistance;

    public bool IsOrthographic => _camera.IsOrthographic;

    /// <summary>
    /// Swaps between perspective and orthographic without changing what is on screen.
    /// </summary>
    /// <remarks>
    /// The two are tied together through the focus point: an orthographic half-height of
    /// <c>distance * tan(fov/2)</c> frames exactly what the perspective view framed at that distance,
    /// so the switch reads as a change of projection rather than a jump to somewhere else.
    /// </remarks>
    public void ToggleProjection()
    {
        Float3 focus = ViewFocusPoint;
        float halfFov = MathF.Tan(Maths.ToRadians(_camera.FieldOfView) * 0.5f);

        if (_camera.IsOrthographic)
        {
            _camera.ProjectionMode = Camera.ProjectionType.Perspective;
            _camera.FarClipPlane = _perspectiveFarClip;
            _orbitDistance = MathF.Max(0.1f, _camera.OrthographicSize / MathF.Max(1e-4f, halfFov));
            UpdateTransform();
            _position = focus - _cameraObject.Transform.Forward * _orbitDistance;
            UpdateTransform();
        }
        else
        {
            _camera.ProjectionMode = Camera.ProjectionType.Orthographic;
            _camera.OrthographicSize = MathF.Max(MinOrthographicSize, _orbitDistance * halfFov);
            SyncOrthographicDepth();
        }
    }

    private void SetOrthographicSize(float size)
    {
        _camera.OrthographicSize = Math.Clamp(size, MinOrthographicSize, MaxOrthographicSize);
        SyncOrthographicDepth();
    }

    /// <summary>
    /// Keeps the visible slab centred on the focus point and sized with the zoom.
    /// </summary>
    /// <remarks>
    /// Where the camera sits along the view axis decides nothing about what an orthographic view
    /// covers, so the depth range has to come from the zoom instead. Left alone it does not: zooming
    /// out widens the box while the clip planes stay put, and once the camera has been pushed further
    /// back than the far plane reaches, the focus point and everything around it fall outside the
    /// frustum and are culled. Backing off with the box and reaching as far past the focus as behind
    /// it makes the clip planes the only thing that stops the view, which is what an orthographic
    /// camera should feel like.
    /// </remarks>
    private void SyncOrthographicDepth()
    {
        if (!_camera.IsOrthographic) return;

        // Captured first: both terms of it are about to move.
        Float3 focus = ViewFocusPoint;

        float backOff = MathF.Max(_camera.OrthographicSize * 4f, MinOrthographicDepth);
        _orbitDistance = backOff;
        _camera.FarClipPlane = backOff * 2f;
        _position = focus - _cameraObject.Transform.Forward * backOff;
        UpdateTransform();
    }

    private const float MinOrthographicSize = 0.01f;
    private const float MaxOrthographicSize = 10000f;
    private const float MinOrthographicDepth = 10f;

    /// <summary>Far plane to put back when leaving orthographic, which moves it to suit the zoom.</summary>
    private float _perspectiveFarClip;

    /// <summary>Vertical field of view in degrees, used by perspective views and by focus framing.</summary>
    public float FieldOfView
    {
        get => _camera.FieldOfView;
        set => _camera.FieldOfView = Math.Clamp(value, 5f, 170f);
    }

    /// <summary>Distance to the near clip plane.</summary>
    public float NearClip
    {
        get => _camera.NearClipPlane;
        set => _camera.NearClipPlane = Math.Clamp(value, 0.001f, MathF.Max(0.002f, _camera.FarClipPlane - 0.001f));
    }

    /// <summary>Distance to the far clip plane. Orthographic views drive this from the zoom instead.</summary>
    public float FarClip
    {
        get => _camera.IsOrthographic ? _perspectiveFarClip : _camera.FarClipPlane;
        set
        {
            _perspectiveFarClip = MathF.Max(_camera.NearClipPlane + 0.001f, value);
            if (!_camera.IsOrthographic) _camera.FarClipPlane = _perspectiveFarClip;
        }
    }

    /// <summary>Half the height the orthographic view covers, in world units.</summary>
    public float OrthographicSize
    {
        get => _camera.OrthographicSize;
        set => SetOrthographicSize(value);
    }

    /// <summary>Fly speed in units per second.</summary>
    public void SetMoveSpeed(float speed) => _moveSpeed = Math.Clamp(speed, 0.1f, 1000f);

    /// <summary>Restores the lens to the defaults a fresh editor camera starts with.</summary>
    public void ResetLens()
    {
        if (_camera.IsOrthographic) ToggleProjection();
        _camera.FieldOfView = 60f;
        _camera.NearClipPlane = 0.01f;
        _perspectiveFarClip = 1000f;
        _camera.FarClipPlane = _perspectiveFarClip;
        _moveSpeed = 5f;
    }

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

    /// <summary> Creates a hidden GameObject with a Camera component configured for editor scene rendering. </summary>
    public EditorCamera()
    {
        _cameraObject = new GameObject("EditorCamera");
        _cameraObject.Enabled = true;
        _cameraObject.HideFlags = HideFlags.HideAndDontSave | HideFlags.NoGizmos;

        _camera = _cameraObject.AddComponent<Camera>();
        _camera.FieldOfView = 60f;
        _camera.NearClipPlane = 0.01f;
        _camera.FarClipPlane = 1000f;
        _perspectiveFarClip = _camera.FarClipPlane;
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
            if (_renderTarget.IsValid()) _renderTarget.Dispose();

            _renderTarget = new RenderTexture(
                (int)width, (int)height, true,
                [PixelFormat.R8_G8_B8_A8_UNorm]);

            _camera.Target = _renderTarget;
        }
    }

    /// <summary>
    /// Render the scene from this camera's perspective.
    /// </summary>
    public void Render(Scene scene, bool drawUI = true)
    {
        if (_renderTarget == null) return;

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

        // Render - native Graphite invocation: collect the scene, build this camera's view, dispatch.
        scene.CollectRenderables();
        CameraView view = CameraView.From(_camera, renderData);
        view.Name = "Scene";
        Graphics.Device.DispatchGraph(RenderPipelineManager.Current, new[] { view });
        _camera.SavePreviousViewProjectionMatrix();

        // Remove from scene if we added it
        if (!wasInScene)
            scene.Remove(_cameraObject);
    }

    /// <summary>
    /// Drop references to scene-derived objects held across frames, before a script
    /// hot-reload unloads the ALC.
    /// </summary>
    public void ReleaseSceneReferences()
    {
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
            if (_camera.IsOrthographic)
            {
                // Moving along the view direction changes nothing an orthographic projection can
                // show, so the wheel has to resize the box instead of dollying.
                SetOrthographicSize(_camera.OrthographicSize * MathF.Pow(0.9f, scroll));
            }
            else
            {
                Float3 forward = GetForwardFromAngles();
                float dolly = scroll * _orbitDistance * 0.1f;
                _position += forward * dolly;
                _orbitDistance = MathF.Max(0.1f, _orbitDistance - dolly);
                UpdateTransform();
            }
            consumed = true;
        }

        // Alt + Left mouse = orbit around a pivot captured when the gesture starts. Orbiting the
        // selection (when present) keeps the thing you care about centered, otherwise a point ahead.
        bool orbiting = Input.IsAltPressed && Input.GetMouseButton(0);
        if (orbiting && !_wasOrbiting)
        {
            _orbitPivot = ComputeOrbitPivot();
            _orbitDistance = MathF.Max(0.1f, Float3.Length(_orbitPivot - _position));
        }
        if (orbiting)
        {
            Float2 delta = Input.MouseDelta;

            _yaw += delta.X * 0.3f;
            _pitch += delta.Y * 0.3f;
            _pitch = MathF.Max(-89f, MathF.Min(89f, _pitch));

            // Reposition so the pivot stays fixed and centered, using the transform's actual forward.
            UpdateTransform();
            _position = _orbitPivot - _cameraObject.Transform.Forward * _orbitDistance;
            UpdateTransform();
            consumed = true;
        }
        _wasOrbiting = orbiting;

        // Alt + Right mouse = dolly zoom
        if (Input.IsAltPressed && Input.GetMouseButton(1))
        {
            Float2 delta = Input.MouseDelta;
            if (_camera.IsOrthographic)
            {
                // Same reason as the wheel: there is nothing to dolly toward in an orthographic view.
                SetOrthographicSize(_camera.OrthographicSize * (1f - (delta.X + delta.Y) * 0.02f));
            }
            else
            {
                float zoomDelta = (delta.X + delta.Y) * 0.02f * _orbitDistance;
                Float3 forward = GetForwardFromAngles();
                _position += forward * zoomDelta;
                _orbitDistance = MathF.Max(0.1f, _orbitDistance - zoomDelta);
                UpdateTransform();
            }
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

    /// <summary>
    /// Pivot for an orbit gesture: the selection's bounds/position center when something is selected,
    /// otherwise a point ahead of the camera at the current orbit distance.
    /// </summary>
    private Float3 ComputeOrbitPivot()
    {
        Float3 min = new(float.MaxValue);
        Float3 max = new(float.MinValue);
        bool anyBounds = false;
        int count = 0;
        Float3 positionSum = Float3.Zero;

        Float3 pivotNormal = Float3.Zero;

        foreach (var go in Selection.GetSelected<GameObject>())
        {
            count++;
            positionSum += go.Transform.Position;
            AccumulateRendererBounds(go, ref min, ref max, ref anyBounds);
            AccumulateUIBounds(go, ref min, ref max, ref anyBounds, ref pivotNormal);
        }

        if (anyBounds) return (min + max) * 0.5f;
        if (count > 0) return positionSum / count;
        return _position + _cameraObject.Transform.Forward * _orbitDistance;
    }

    /// <summary> Moves the camera to frame the currently selected objects, centering on their combined bounds or average position. </summary>
    public void FocusSelection()
    {
        Float3 min = new(float.MaxValue);
        Float3 max = new(float.MinValue);
        bool anyBounds = false;
        int goCount = 0;
        Float3 positionSum = Float3.Zero;

        bool anyRenderer = false;
        Float3 uiNormal = Float3.Zero;
        bool anyUI = false;

        foreach (var go in Selection.GetSelected<GameObject>())
        {
            goCount++;
            positionSum += go.Transform.Position;
            AccumulateRendererBounds(go, ref min, ref max, ref anyRenderer);
            AccumulateUIBounds(go, ref min, ref max, ref anyUI, ref uiNormal);
        }
        if (goCount == 0) return;
        anyBounds = anyRenderer || anyUI;

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

        // A UI-only selection is a flat rect, so the view swings around to face it from whichever side
        // the camera is already on. Anything with geometry keeps the angle it was viewed from.
        if (anyUI && !anyRenderer && Float3.LengthSquared(uiNormal) > 1e-8f)
        {
            Float3 facing = Float3.Normalize(uiNormal);
            if (Float3.Dot(facing, _position - target) < 0f) facing = -facing;
            FaceDirection(-facing);
        }

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

    /// <summary>
    /// Grows the bounds by the world rect of any canvas or UI element on this object or below it, and
    /// reports the facing of the last rect seen so the view can be squared up to it.
    /// </summary>
    private static void AccumulateUIBounds(GameObject go, ref Float3 min, ref Float3 max, ref bool any, ref Float3 normal)
    {
        var canvas = go.GetComponent<GameCanvas>();
        if (canvas.IsValid())
        {
            Rect root = canvas.RootRect;
            Float4x4 toWorld = canvas.CanvasToWorld;
            Include(Float4x4.TransformPoint(new Float3((float)root.Min.X, (float)root.Min.Y, 0f), toWorld), ref min, ref max, ref any);
            Include(Float4x4.TransformPoint(new Float3((float)root.Min.X, (float)root.Max.Y, 0f), toWorld), ref min, ref max, ref any);
            Include(Float4x4.TransformPoint(new Float3((float)root.Max.X, (float)root.Max.Y, 0f), toWorld), ref min, ref max, ref any);
            Include(Float4x4.TransformPoint(new Float3((float)root.Max.X, (float)root.Min.Y, 0f), toWorld), ref min, ref max, ref any);
            normal = new Float3(toWorld.c2.X, toWorld.c2.Y, toWorld.c2.Z);
        }

        if (go.RectTransform is { } rect)
        {
            rect.ForceUpdateRectTransforms();
            Float3[] corners = new Float3[4];
            rect.GetWorldCorners(corners);
            if (corners[0] != corners[2])
            {
                foreach (Float3 corner in corners)
                    Include(corner, ref min, ref max, ref any);

                Float3 edgeX = corners[3] - corners[0];
                Float3 edgeY = corners[1] - corners[0];
                normal = Float3.Cross(edgeX, edgeY);
            }
        }

        foreach (var child in go.Children)
            AccumulateUIBounds(child, ref min, ref max, ref any, ref normal);
    }

    private static void Include(Float3 point, ref Float3 min, ref Float3 max, ref bool any)
    {
        min = new Float3(MathF.Min(min.X, point.X), MathF.Min(min.Y, point.Y), MathF.Min(min.Z, point.Z));
        max = new Float3(MathF.Max(max.X, point.X), MathF.Max(max.Y, point.Y), MathF.Max(max.Z, point.Z));
        any = true;
    }

    /// <summary>Points the camera along <paramref name="forward"/> without moving it.</summary>
    private void FaceDirection(Float3 forward)
    {
        if (Float3.LengthSquared(forward) < 1e-8f) return;

        forward = Float3.Normalize(forward);
        _yaw = Maths.ToDegrees(MathF.Atan2(forward.X, forward.Z));
        _pitch = Math.Clamp(Maths.ToDegrees(MathF.Asin(-forward.Y)), -89f, 89f);
        UpdateTransform();
    }

    /// <summary> Sets the camera's yaw and pitch without changing its position. Pitch is clamped to +/-89 degrees. </summary>
    public void SetOrientation(float yaw, float pitch)
    {
        _yaw = yaw;
        _pitch = MathF.Max(-89f, MathF.Min(89f, pitch));
        UpdateTransform();
    }

    /// <summary> Converts a screen-space coordinate into a world-space ray using the editor camera's projection. </summary>
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

    /// <summary> Releases all cloned image effects and disposes the render target. </summary>
    public void Dispose()
    {
        if (_renderTarget.IsValid()) _renderTarget.Dispose();
        _renderTarget = null;
    }
}
