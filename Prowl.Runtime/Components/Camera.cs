// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime;

public abstract class ImageEffect
{
    /// <summary>
    /// When false, the effect is skipped by the render pipeline.
    /// </summary>
    public bool Enabled = true;

    /// <summary>
    /// Defines at which stage of the rendering pipeline this effect should run.
    /// AfterOpaques: runs after opaque geometry, before transparents (GTAO, SSR).
    /// PostProcess: runs after all rendering (tonemapping, bloom, FXAA).
    /// </summary>
    public virtual RenderStage Stage => RenderStage.PostProcess;

    /// <summary>
    /// Whether this effect transforms HDR to LDR. Used for tonemapping effects.
    /// </summary>
    public virtual bool TransformsToLDR { get; } = false;

    /// <summary>
    /// Called during rendering with access to render targets.
    /// </summary>
    public virtual void OnRenderEffect(RenderContext context) { }

    /// <summary>Called after all rendering is complete for this camera.</summary>
    public virtual void OnPostRender(Camera camera) { }

    /// <summary>Called before culling for this camera.</summary>
    public virtual void OnPreCull(Camera camera) { }

    /// <summary>Called before rendering starts for this camera.</summary>
    public virtual void OnPreRender(Camera camera) { }

    /// <summary>
    /// Called when the effect transitions from active to inactive (Enabled = false,
    /// or removed from Camera.Effects). Override to release GPU resources such as
    /// materials, persistent RenderTextures, or shader-program handles.
    /// </summary>
    public virtual void OnDisable() { }

    private readonly object?[] _eyeStates = new object?[3];

    /// <summary>
    /// State kept apart for each eye the camera renders, so temporal history from one eye never feeds the other.
    /// A mono camera only ever uses one.
    /// </summary>
    protected T GetEyeState<T>(Camera camera) where T : class, new()
        => (T)(_eyeStates[(int)camera.ActiveEye] ??= new T());

    /// <summary>Hands every eye state created so far to <paramref name="release"/>, then forgets them.</summary>
    protected void ReleaseEyeStates<T>(Action<T> release) where T : class
    {
        for (int i = 0; i < _eyeStates.Length; i++)
        {
            if (_eyeStates[i] is T state) release(state);
            _eyeStates[i] = null;
        }
    }
}

public enum CameraClearFlags
{
    Nothing,
    SolidColor,
    Depth,
    Skybox,
}

/// <summary>The view a camera is currently rendering. Mono is a normal render, Left and Right are headset eyes.</summary>
public enum StereoEye
{
    Mono,
    Left,
    Right,
}

/// <summary>Which headset eyes a camera renders to while XR is running.</summary>
[Flags]
public enum StereoTargetEyeMask
{
    None = 0,
    Left = 1,
    Right = 2,
    Both = Left | Right,
}

[AddComponentMenu("Rendering/Camera")]
[ComponentIcon("\uf030")] // Camera
public class Camera : MonoBehaviour
{
    public List<ImageEffect> Effects = [];

    public CameraClearFlags ClearFlags = CameraClearFlags.Skybox;
    public Color ClearColor = new(0f, 0f, 0f, 1f);
    public LayerMask CullingMask = LayerMask.Everything;

    public enum ProjectionType { Perspective, Orthographic }
    public ProjectionType ProjectionMode = ProjectionType.Perspective;

    public float FieldOfView = 60f;

    /// <summary>
    /// Half the height of the orthographic view, in world units, so the view spans twice this from
    /// top to bottom and as much across as the target's aspect calls for.
    /// </summary>
    public float OrthographicSize = 5f;
    public float NearClipPlane = 0.1f;
    public float FarClipPlane = 100f;
    //public Rect Viewrect = new(0, 0, 1, 1); // Not Implemented
    public int Depth = -1;

    private static Camera? s_main;
    private static Scene? s_mainScene;
    private static long s_mainFrame = -1;

    /// <summary>
    /// The camera shown on top: the enabled camera with the highest <see cref="Depth"/> in the current scene
    /// that draws to the screen. Cameras drawing into a render texture and editor helper cameras are never main.
    /// Found once per frame, null when there is none.
    /// </summary>
    public static Camera? Main
    {
        get
        {
            Scene scene = Scene.Current;
            if (s_mainFrame != Time.FrameCount || s_mainScene != scene || (s_main != null && s_main.IsNotValid()))
            {
                s_main = scene.FindMainCamera();
                s_mainScene = scene;
                s_mainFrame = Time.FrameCount;
            }
            return s_main;
        }
    }

    public RenderPipeline? Pipeline;

    /// <summary>
    /// The render texture asset this camera draws into. None means it draws wherever the render it takes
    /// part in is going - the backbuffer in a player, the Game View or scene view texture in the editor
    /// (see <see cref="RenderingData.FallbackTarget"/>).
    /// </summary>
    public RenderTexture? Target;
    public bool HDR = false;
    public float RenderScale = 1.0f;

    /// <summary>
    /// The headset eyes this camera renders while XR is running. A perspective camera with no <see cref="Target"/>
    /// renders once per eye from its transform, which stands for the head, into the headset, and mirrors the
    /// left eye to wherever it would normally draw.
    /// </summary>
    public StereoTargetEyeMask StereoTargetEye = StereoTargetEyeMask.Both;

    /// <summary>The eye being rendered right now. Mono outside a stereo render.</summary>
    public StereoEye ActiveEye { get; private set; }

    /// <summary>World position the current view is rendered from: the active eye in stereo, otherwise the transform.</summary>
    public Float3 ViewPosition => ActiveEye != StereoEye.Mono ? _eyePosition : Transform.Position;

    /// <summary>World rotation the current view is rendered with: the active eye in stereo, otherwise the transform.</summary>
    public Quaternion ViewRotation => ActiveEye != StereoEye.Mono ? _eyeRotation : Transform.Rotation;

    private XRView _eyeView;
    private Float3 _eyePosition;
    private Quaternion _eyeRotation;
    private bool _renderedStereo;

    // A projection the user set by hand, kept aside while the eyes' own projections are in use.
    private Float4x4 _monoProjection, _monoNonJitteredProjection;
    private bool _monoCustomProjection, _monoCustomNonJitteredProjection;

    public bool IsOrthographic => ProjectionMode == ProjectionType.Orthographic;

    private float _aspect;
    private bool _customAspect;

    // The projection matrix used for rendering. May be jittered by TAA.
    private Float4x4 _projectionMatrix;
    private bool _customProjectionMatrix;

    // The unjittered projection matrix. Always reflects the clean base projection.
    // TAA sets ProjectionMatrix (jittered) while this stays clean.
    private Float4x4 _nonJitteredProjectionMatrix;
    private bool _customNonJitteredProjectionMatrix;

    // Previous frame state per eye, written by the render pipeline at end of frame.
    [SerializeIgnore]
    private readonly Float4x4[] _previousViewProjectionMatrix = new Float4x4[3];
    [SerializeIgnore]
    private readonly bool[] _hasPreviousViewProjectionMatrix = new bool[3];

    // Image effects that were considered active on the previous render tick for this
    // camera. Compared against the current list each frame to fire OnDisable() on
    // anything that's been disabled, removed, or hot-swapped out.
    [SerializeIgnore]
    private readonly HashSet<ImageEffect> _lastActiveEffects = new();

    public uint PixelWidth { get; private set; }
    public uint PixelHeight { get; private set; }

    public float Aspect
    {
        get => _aspect;
        set
        {
            _aspect = value;
            _customAspect = true;
        }
    }

    /// <summary>
    /// The projection matrix used for rendering. May include jitter from TAA.
    /// Setting this overrides the auto-computed projection from FOV/aspect/clip planes.
    /// Also updates NonJitteredProjectionMatrix unless it was explicitly set.
    /// </summary>
    public Float4x4 ProjectionMatrix
    {
        get => _projectionMatrix;
        set
        {
            _projectionMatrix = value;
            _customProjectionMatrix = true;
        }
    }

    /// <summary>
    /// The unjittered projection matrix. Used by the render pipeline for motion vectors
    /// and other operations that need a stable, non-jittered projection.
    /// If not explicitly set, returns the base projection computed from FOV/aspect/clip planes.
    /// TAA effects should set ProjectionMatrix (jittered) while leaving this at the clean value,
    /// or set this explicitly before applying jitter to ProjectionMatrix.
    /// </summary>
    public Float4x4 NonJitteredProjectionMatrix
    {
        get => _nonJitteredProjectionMatrix;
        set
        {
            _nonJitteredProjectionMatrix = value;
            _customNonJitteredProjectionMatrix = true;
        }
    }

    /// <summary>True when <see cref="ProjectionMatrix"/> was set by hand rather than computed from the field of view and clip planes.</summary>
    public bool HasCustomProjectionMatrix => _customProjectionMatrix;

    /// <summary>True when <see cref="NonJitteredProjectionMatrix"/> was set by hand.</summary>
    public bool HasCustomNonJitteredProjectionMatrix => _customNonJitteredProjectionMatrix;

    public Float4x4 ViewMatrix { get; private set; }

    /// <summary>
    /// The previous frame's unjittered view-projection matrix.
    /// Set by the render pipeline at the end of each frame. Used for motion vectors
    /// and temporal reprojection. Returns identity on the first frame.
    /// </summary>
    public Float4x4 PreviousViewProjectionMatrix => _previousViewProjectionMatrix[(int)ActiveEye];

    /// <summary>
    /// Whether a valid previous view-projection matrix exists (false on the first frame).
    /// </summary>
    public bool HasPreviousViewProjectionMatrix => _hasPreviousViewProjectionMatrix[(int)ActiveEye];

    public override void OnEnable()
    {
        ResetMotionHistory();
    }

    /// <summary>
    /// Renders the following views as <paramref name="eye"/>, posed and projected by <paramref name="view"/>,
    /// until <see cref="EndStereoEye"/>. Called by render pipelines around each eye of a stereo render.
    /// </summary>
    public void BeginStereoEye(StereoEye eye, in XRView view)
    {
        if (ActiveEye == StereoEye.Mono)
        {
            _monoProjection = _projectionMatrix;
            _monoNonJitteredProjection = _nonJitteredProjectionMatrix;
            _monoCustomProjection = _customProjectionMatrix;
            _monoCustomNonJitteredProjection = _customNonJitteredProjectionMatrix;
        }

        ActiveEye = eye;
        _eyeView = view;
    }

    public void EndStereoEye()
    {
        if (ActiveEye == StereoEye.Mono) return;
        ActiveEye = StereoEye.Mono;
        _projectionMatrix = _monoProjection;
        _nonJitteredProjectionMatrix = _monoNonJitteredProjection;
        _customProjectionMatrix = _monoCustomProjection;
        _customNonJitteredProjectionMatrix = _monoCustomNonJitteredProjection;
    }

    /// <summary>
    /// Tells the camera whether this render is stereo. Switching between mono and stereo forgets the motion history,
    /// since the last frame of the other kind was rendered from a different view, possibly long ago.
    /// </summary>
    public void SetRenderingStereo(bool stereo)
    {
        if (stereo == _renderedStereo) return;
        _renderedStereo = stereo;
        ResetMotionHistory();
    }

    /// <summary>
    /// Fire OnDisable on every image effect we were rendering, because the camera
    /// itself is going away (disabled or destroyed). Render pipelines never get
    /// another chance to do this for us, so it has to happen here.
    /// </summary>
    public override void OnDisable()
    {
        foreach (var effect in _lastActiveEffects)
        {
            if (effect == null) continue;
            try { effect.OnDisable(); }
            catch (Exception e) { Debug.LogError($"ImageEffect.OnDisable threw: {e}"); }
        }
        _lastActiveEffects.Clear();
    }

    /// <summary>
    /// Called once per render tick by the render pipeline. Fires OnDisable on any
    /// effect that was active last frame but isn't in <paramref name="currentlyActive"/>
    /// this frame covers disabled, removed, and hot-swapped effects. Pipelines
    /// don't need to track this themselves; they just pass in whatever they're about
    /// to render.
    /// </summary>
    public void UpdateImageEffectLifecycle(IEnumerable<ImageEffect> currentlyActive)
    {
        var current = new HashSet<ImageEffect>();
        foreach (var effect in currentlyActive)
            if (effect != null) current.Add(effect);

        foreach (var previous in _lastActiveEffects)
        {
            if (previous == null || current.Contains(previous)) continue;
            try { previous.OnDisable(); }
            catch (Exception e) { Debug.LogError($"ImageEffect.OnDisable threw: {e}"); }
        }

        _lastActiveEffects.Clear();
        foreach (var effect in current)
            _lastActiveEffects.Add(effect);
    }

    public override void DrawGizmos()
    {
        if (GameObject.HideFlags.HasFlag(HideFlags.NoGizmos)) return;

        var icon = Resources.Texture2D.LoadDefault(Resources.DefaultTexture.IconCamera);
        if (icon != null) Debug.DrawIcon(icon, Transform.Position, 0.5f, Color.White);

        // The aspect of the camera's last render, 16:9 before it has rendered.
        float aspect = _aspect > 0 ? _aspect : 16f / 9f;
        Float4x4 viewProjectionMatrix = GetProjectionMatrix(aspect) * GetViewMatrix();

        Frustum frustum = Frustum.FromMatrix(viewProjectionMatrix);
        var corners = frustum.GetCorners();

        // Corner indices from GetCorners():
        // 0: Near-Left-Bottom,  1: Near-Right-Bottom,  2: Near-Left-Top,  3: Near-Right-Top
        // 4: Far-Left-Bottom,   5: Far-Right-Bottom,   6: Far-Left-Top,   7: Far-Right-Top

        Debug.DrawLine(corners[0], corners[1], Color.White);
        Debug.DrawLine(corners[1], corners[3], Color.White);
        Debug.DrawLine(corners[3], corners[2], Color.White);
        Debug.DrawLine(corners[2], corners[0], Color.White);

        Debug.DrawLine(corners[4], corners[5], Color.White);
        Debug.DrawLine(corners[5], corners[7], Color.White);
        Debug.DrawLine(corners[7], corners[6], Color.White);
        Debug.DrawLine(corners[6], corners[4], Color.White);

        Debug.DrawLine(corners[0], corners[4], Color.White);
        Debug.DrawLine(corners[1], corners[5], Color.White);
        Debug.DrawLine(corners[2], corners[6], Color.White);
        Debug.DrawLine(corners[3], corners[7], Color.White);
    }

    public void Render(in RenderingData? data = null)
    {
        RenderPipeline pipeline = Pipeline.IsValid() ? Pipeline : DefaultRenderPipeline.Default;
        pipeline.Render(this, data ?? new());
    }

    /// <param name="fallbackTarget">Where this render goes when the camera has no <see cref="Target"/>
    /// asset of its own. Null means the backbuffer.</param>
    public RenderTexture? UpdateRenderData(RenderTexture? fallbackTarget = null)
    {
        // Since Scene Updating is guranteed to execute before rendering, we can setup camera data for this frame here
        RenderTexture? camTarget = Target;
        if (camTarget.IsNotValid()) camTarget = fallbackTarget;

        int width = camTarget.IsValid() ? camTarget.Width : Window.InternalWindow.FramebufferSize.X;
        int height = camTarget.IsValid() ? camTarget.Height : Window.InternalWindow.FramebufferSize.Y;

        // Screen points use the same space Input.MousePosition reports: target pixels when drawing into a
        // target (the editor's Game view included), window coordinates when drawing to the backbuffer.
        _screenSize = camTarget.IsValid()
            ? new Float2(camTarget.Width, camTarget.Height)
            : new Float2(Window.InternalWindow.Size.X, Window.InternalWindow.Size.Y);

        float renderScale = Maths.Clamp(RenderScale, 0.1f, 2.0f);
        PixelWidth = (uint)Maths.Max(1, (int)(width * renderScale));
        PixelHeight = (uint)Maths.Max(1, (int)(height * renderScale));

        if (ActiveEye != StereoEye.Mono)
        {
            // An eye's frustum is fixed by the headset, so it replaces the projection for this render. The eye's
            // offset goes through the transform, so a scaled play area scales the distance between the eyes too.
            _eyePosition = Transform.TransformPoint(_eyeView.Position);
            _eyeRotation = Transform.Rotation * _eyeView.Rotation;
            _projectionMatrix = CreateEyeProjection(_eyeView, NearClipPlane, FarClipPlane);
            _nonJitteredProjectionMatrix = _projectionMatrix;
        }
        else
        {
            if (!_customAspect)
                _aspect = PixelWidth / (float)PixelHeight;

            // Recompute the base projection unless the user set it manually
            if (!_customProjectionMatrix)
                _projectionMatrix = GetProjectionMatrix(_aspect);

            // Keep the non-jittered projection in sync unless explicitly overridden
            if (!_customNonJitteredProjectionMatrix)
                _nonJitteredProjectionMatrix = GetProjectionMatrix(_aspect);
        }

        ViewMatrix = Float4x4.CreateLookTo(ViewPosition, Quaternion.Forward(ViewRotation), Quaternion.Up(ViewRotation));

        return camTarget;
    }

    /// <summary>An off center perspective projection from an eye's frustum, in the same depth convention as <see cref="Float4x4.CreatePerspectiveFov"/>.</summary>
    private static Float4x4 CreateEyeProjection(in XRView view, float nearPlane, float farPlane)
    {
        float width = view.TanRight - view.TanLeft;
        float height = view.TanUp - view.TanDown;
        float range = farPlane / (farPlane - nearPlane);

        return new Float4x4(
            new Float4(2f / width, 0, 0, 0),
            new Float4(0, 2f / height, 0, 0),
            new Float4(-(view.TanRight + view.TanLeft) / width, -(view.TanUp + view.TanDown) / height, range, 1f),
            new Float4(0, 0, -range * nearPlane, 0));
    }

    /// <summary>
    /// Called by the render pipeline at end of frame to store the current unjittered
    /// view-projection matrix as the previous frame's matrix for next frame's motion vectors.
    /// </summary>
    public void SavePreviousViewProjectionMatrix()
    {
        _previousViewProjectionMatrix[(int)ActiveEye] = _nonJitteredProjectionMatrix * ViewMatrix;
        _hasPreviousViewProjectionMatrix[(int)ActiveEye] = true;
    }

    public void ResetAspect()
    {
        _aspect = PixelWidth / (float)PixelHeight;
        _customAspect = false;
    }

    /// <summary>
    /// Resets the projection matrix to the auto-computed value from FOV/aspect/clip planes.
    /// Also resets the non-jittered projection matrix.
    /// </summary>
    public void ResetProjectionMatrix()
    {
        _projectionMatrix = GetProjectionMatrix(_aspect);
        _customProjectionMatrix = false;
        _nonJitteredProjectionMatrix = _projectionMatrix;
        _customNonJitteredProjectionMatrix = false;
    }

    /// <summary>
    /// Resets the non-jittered projection matrix to the auto-computed value.
    /// </summary>
    public void ResetNonJitteredProjectionMatrix()
    {
        _nonJitteredProjectionMatrix = GetProjectionMatrix(_aspect);
        _customNonJitteredProjectionMatrix = false;
    }

    /// <summary>
    /// Clears the stored previous view-projection matrix, forcing the next frame
    /// to treat itself as the first frame (no temporal history).
    /// </summary>
    public void ResetMotionHistory()
    {
        Array.Clear(_hasPreviousViewProjectionMatrix);
    }

    private Float2 _screenSize;

    /// <summary>
    /// The size of the space screen points are measured in, from the top left: the camera's target in pixels
    /// when it draws into one (including the editor's Game view), otherwise the window in window coordinates.
    /// Matches <see cref="Input.MousePosition"/>. Taken from the camera's last render, or the window before then.
    /// </summary>
    public Float2 ScreenSize
    {
        get
        {
            if (_screenSize.X > 0 && _screenSize.Y > 0) return _screenSize;
            if (Window.InternalWindow != null) return new Float2(Window.InternalWindow.Size.X, Window.InternalWindow.Size.Y);
            return Float2.One;
        }
    }

    /// <summary>The ray from the camera through <paramref name="screenPoint"/>, in <see cref="ScreenSize"/> space.</summary>
    public Ray ScreenPointToRay(Float2 screenPoint) => ScreenPointToRay(screenPoint, ScreenSize);

    /// <summary>
    /// Converts a world point to a screen point. X and Y are pixels from the top left of <see cref="ScreenSize"/>,
    /// Z is the distance in front of the camera along its forward axis, negative when the point is behind it.
    /// </summary>
    public Float3 WorldToScreenPoint(Float3 worldPoint) => WorldToScreenPoint(worldPoint, ScreenSize);

    /// <inheritdoc cref="WorldToScreenPoint(Float3)"/>
    public Float3 WorldToScreenPoint(Float3 worldPoint, Float2 screenSize)
    {
        Float4 clip = Float4x4.TransformPoint(new Float4(worldPoint, 1f), ScreenViewProjection(screenSize));
        float w = Maths.Abs(clip.W) < 1e-6f ? 1e-6f : clip.W;
        float depth = Float3.Dot(worldPoint - Transform.Position, Transform.Forward);

        return new Float3(
            (clip.X / w + 1f) * 0.5f * screenSize.X,
            (1f - clip.Y / w) * 0.5f * screenSize.Y,
            depth);
    }

    /// <summary>
    /// Converts a screen point to a world point. X and Y are pixels from the top left of <see cref="ScreenSize"/>,
    /// Z is how far in front of the camera along its forward axis the world point lies.
    /// </summary>
    public Float3 ScreenToWorldPoint(Float3 screenPoint) => ScreenToWorldPoint(screenPoint, ScreenSize);

    /// <inheritdoc cref="ScreenToWorldPoint(Float3)"/>
    public Float3 ScreenToWorldPoint(Float3 screenPoint, Float2 screenSize)
    {
        Ray ray = ScreenPointToRay(new Float2(screenPoint.X, screenPoint.Y), screenSize);
        Float3 forward = Transform.Forward;
        float startDepth = Float3.Dot(ray.Origin - Transform.Position, forward);
        float along = Float3.Dot(ray.Direction, forward);
        return ray.Origin + ray.Direction * ((screenPoint.Z - startDepth) / along);
    }

    // Unjittered, from the transform, so it matches what the player sees rather than a TAA sample.
    private Float4x4 ScreenViewProjection(Float2 screenSize) => GetProjectionMatrix(screenSize.X / screenSize.Y) * GetViewMatrix();

    public Ray ScreenPointToRay(Float2 screenPoint, Float2 screenSize)
    {
        // Normalize screen coordinates to [-1, 1]
        Float2 ndc = new(
            (screenPoint.X / screenSize.X) * 2.0f - 1.0f,
            1.0f - (screenPoint.Y / screenSize.Y) * 2.0f
        );

        // Create the near and far points in NDC
        Float4 nearPointNDC = new(ndc.X, ndc.Y, 0.0f, 1.0f);
        Float4 farPointNDC = new(ndc.X, ndc.Y, 1.0f, 1.0f);

        Float4x4 inverseViewProjectionMatrix = ScreenViewProjection(screenSize).Invert();

        // Unproject the near and far points to world space
        Float4 nearPointWorld = Float4x4.TransformPoint(nearPointNDC, inverseViewProjectionMatrix);
        Float4 farPointWorld = Float4x4.TransformPoint(farPointNDC, inverseViewProjectionMatrix);

        // Perform perspective divide
        nearPointWorld /= nearPointWorld.W;
        farPointWorld /= farPointWorld.W;

        // Create the ray
        Float3 rayOrigin = new(nearPointWorld.X, nearPointWorld.Y, nearPointWorld.Z);
        Float3 rayDirection = Float3.Normalize(new Float3(farPointWorld.X, farPointWorld.Y, farPointWorld.Z) - rayOrigin);

        return new Ray(rayOrigin, rayDirection);
    }

    public Float4x4 GetViewMatrix(bool applyPosition = true)
    {
        Float3 position = applyPosition ? Transform.Position : Float3.Zero;

        return Float4x4.CreateLookTo(position, Transform.Forward, Transform.Up);
    }

    private Float4x4 GetProjectionMatrix(float aspect)
    {
        if (FieldOfView <= 0)
            FieldOfView = 1f;
        if (FieldOfView >= 180)
            FieldOfView = 179f;

        Float4x4 proj;

        if (ProjectionMode == ProjectionType.Orthographic)
            // CreateOrtho takes the full extents, and the width has to follow the aspect: passing the
            // size for both squares the view, so anything but a square target came out squashed.
            proj = Float4x4.CreateOrtho(OrthographicSize * 2f * aspect, OrthographicSize * 2f, NearClipPlane, FarClipPlane);
        else
            proj = Float4x4.CreatePerspectiveFov(Maths.ToRadians(FieldOfView), aspect, NearClipPlane, FarClipPlane);

        return proj;
    }
}
