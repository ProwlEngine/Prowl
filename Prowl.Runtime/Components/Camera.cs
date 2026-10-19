// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Graphite.Rendering;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>What a camera shows behind the scene.</summary>
public enum CameraBackground
{
    Skybox,
    Color,

    /// <summary>
    /// The cameras drawn before it into the same target, a lower <see cref="Camera.Depth"/>, for cameras stacked over
    /// others such as a first person weapon or world space UI.
    /// </summary>
    None,
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

/// <summary>How one render of a camera goes, beyond the camera's own settings.</summary>
public struct CameraRenderOptions
{
    /// <summary>Where the camera draws when it has no <see cref="Camera.Target"/>, null for the window.</summary>
    public RenderTexture? FallbackTarget;

    /// <summary>Draws the gizmos and Debug.Draw shapes queued this frame.</summary>
    public bool DisplayGizmos;

    /// <summary>What the view is for, which decides the features that run on it.</summary>
    public ViewKind Kind;

    /// <summary>Features drawn on this render only, after the camera's effects.</summary>
    public IReadOnlyList<RenderFeature>? Features;

    /// <summary>Views that must draw before this one, such as ones whose targets it samples.</summary>
    public int[]? DependsOn;

    /// <summary>Global axis values for this render alone, such as a debug view's GRAPHITE_DEBUG.</summary>
    public Graphite.ShaderDef.Keyword[]? Keywords;
}

/// <summary>
/// How cameras become views of a frame. Prowl's own, <see cref="ForwardCameraPipeline"/>, draws each camera as a Graphite
/// view running the world's features and the camera's effects. A pipeline of another design, such as deferred, adds
/// each camera as an external view (<see cref="Camera.BuildView"/> with ViewDesc.External set), which Graphite culls and
/// prepares draws for, and records it in passes of its own added with <see cref="Graphics.AddPipeline"/>, drawing through
/// Renderer.Draws. Set it on <see cref="Graphics.CameraPipeline"/>.
/// </summary>
public abstract class CameraPipeline
{
    /// <summary>Adds a camera's render to this frame. Main thread.</summary>
    public abstract void Render(Camera camera, in CameraRenderOptions options);
}

/// <summary>Prowl's forward pipeline: each camera a Graphite view with the world's features and the camera's effects.</summary>
public sealed class ForwardCameraPipeline : CameraPipeline
{
    public override void Render(Camera camera, in CameraRenderOptions options) => camera.RenderForward(options);
}

[AddComponentMenu("Rendering/Camera")]
[ComponentIcon("")] // Camera
public class Camera : Component
{
    /// <summary>Draws the effects of the volumes the camera listens to and of its base profile.</summary>
    public bool PostProcessing = true;

    /// <summary>The camera's own effects, under every volume: an asset, or a profile embedded in the scene.</summary>
    public VolumeProfile? Profile;

    /// <summary>The layers of the volumes the camera listens to.</summary>
    public LayerMask VolumeMask = LayerMask.Everything;

    /// <summary>Where local volumes are measured from, the camera itself when empty.</summary>
    public GameObject? VolumeTrigger;

    public AntiAliasingMode AntiAliasing = AntiAliasingMode.None;
    [ShowIf(nameof(UsesFxaa))] public FxaaSettings Fxaa = new();
    [ShowIf(nameof(UsesSmaa))] public SmaaSettings Smaa = new();
    [ShowIf(nameof(UsesTaa))] public TaaSettings Taa = new();

    /// <summary>Renders into 16 bit float colour and tonemaps it. Off renders into 8 bit sRGB, where light above 1 clips.</summary>
    public bool HDR = true;
    [ShowIf(nameof(HDR))] public TonemappingMode Tonemapping = TonemappingMode.AgXPunchy;

    private bool UsesFxaa => AntiAliasing == AntiAliasingMode.FXAA;
    private bool UsesSmaa => AntiAliasing == AntiAliasingMode.SMAA;
    private bool UsesTaa => AntiAliasing == AntiAliasingMode.TAA;
    private bool UsesBackgroundColor => Background == CameraBackground.Color;

    public CameraBackground Background = CameraBackground.Skybox;
    [ShowIf(nameof(UsesBackgroundColor))] public Color BackgroundColor = new(0f, 0f, 0f, 1f);
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

    /// <summary>The farthest distance the camera draws and effects such as fog and shadows reach.</summary>
    public float FarClipPlane = 100f;

    /// <summary>The part of the target the camera draws into, as x, y, width and height from 0 to 1, y down from the top.</summary>
    public Float4 ViewportRect = new(0, 0, 1, 1);

    /// <summary>Cameras draw lowest depth first, each over the ones before.</summary>
    public int Depth = -1;

    private static Camera? s_main;
    private static Scene? s_mainScene;
    private static long s_mainFrame = -1;

    /// <summary>
    /// The camera shown on top: the enabled camera with the highest <see cref="Depth"/> in the current scene
    /// that draws to the screen. Cameras drawing into a render texture, cameras stacked over others and editor helper
    /// cameras are never main.
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

    /// <summary>
    /// The render texture asset this camera draws into. None means it draws wherever the render it takes part in is
    /// going: the window in a player, the Game View or scene view texture in the editor.
    /// </summary>
    public RenderTexture? Target;

    /// <summary>Resolution the camera renders at against its target, presented scaled to fit.</summary>
    public float RenderScale = 1.0f;

    /// <summary>
    /// The headset eyes this camera renders while XR is running. A perspective camera with no <see cref="Target"/>
    /// renders both eyes from its transform, which stands for the head, into the headset.
    /// </summary>
    public StereoTargetEyeMask StereoTargetEye = StereoTargetEyeMask.Both;

    public bool IsOrthographic => ProjectionMode == ProjectionType.Orthographic;

    private float _aspect;
    private bool _customAspect;
    private Float4x4 _projectionMatrix;
    private bool _customProjectionMatrix;

    [SerializeIgnore, NonSerialized, NotHeld] private int _viewId;
    [SerializeIgnore, NonSerialized, NotHeld] private int _stereoViewId;
    [SerializeIgnore, NonSerialized, NotHeld] private bool _cut;
    [SerializeIgnore, NonSerialized, NotHeld] private readonly FeatureSet _features = new();
    [SerializeIgnore, NonSerialized, NotHeld] private readonly List<RenderFeature> _activeFeatures = [];
    [SerializeIgnore, NonSerialized, NotHeld] private VolumeStack? _volumes;
    [SerializeIgnore, NonSerialized, NotHeld] private AntiAliasingFeatures? _antiAliasing;
    [SerializeIgnore, NonSerialized, NotHeld] private TonemappingFeature? _tonemapping;
    [NonSerialized, NotHeld] private readonly Dictionary<Type, object> _renderData = [];

    /// <summary>The id of this camera's view, which keys Graphite's state for it such as temporal history.</summary>
    public int ViewId => _viewId == 0 ? _viewId = Graphics.NewViewId() : _viewId;

    /// <summary>
    /// Data kept for this camera from one render to the next. One value per type, made by <paramref name="create"/>
    /// on first use and disposed with the camera.
    /// </summary>
    public T GetRenderData<T>(Func<T> create) where T : class
    {
        if (_renderData.TryGetValue(typeof(T), out object? data)) return (T)data;
        T made = create();
        _renderData[typeof(T)] = made;
        return made;
    }

    /// <summary>Disposes and forgets the render data of type <typeparamref name="T"/>, made again on its next use.</summary>
    public void ReleaseRenderData<T>() where T : class
    {
        if (_renderData.Remove(typeof(T), out object? data) && data is IDisposable disposable)
            disposable.Dispose();
    }

    protected override void OnDispose()
    {
        foreach (object data in _renderData.Values)
        {
            try { (data as IDisposable)?.Dispose(); }
            catch (Exception e) { Debug.LogError($"Disposing render data of camera '{Name}' threw: {e.Message}"); }
        }
        _renderData.Clear();
        ReleaseVolumes();
        base.OnDispose();
    }

    /// <summary>The width the camera last rendered at, in pixels.</summary>
    public uint PixelWidth { get; private set; }

    /// <summary>The height the camera last rendered at, in pixels.</summary>
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
    /// The projection matrix, depth 0 at the near plane to 1 at the far plane. Setting it overrides the projection
    /// computed from the field of view, aspect and clip planes.
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

    /// <summary>The world to view matrix of the camera's transform.</summary>
    public Float4x4 ViewMatrix => Float4x4.CreateLookTo(Transform.Position, Transform.Forward, Transform.Up);

    /// <summary>The volume settings blended for this camera on its last render.</summary>
    public VolumeStack Volumes => _volumes ??= new VolumeStack();

    /// <summary>Blends the volumes at the camera's trigger into <see cref="Volumes"/>.</summary>
    public void ResolveVolumes()
    {
        Float3 at = VolumeTrigger.IsValid() ? VolumeTrigger.Transform.Position : Transform.Position;
        Volumes.Resolve(GameObject.IsValid() ? GameObject.Scene : null, at, VolumeMask, Profile);
    }

    /// <summary>Frees the camera's effect features and forgets its blended components, made again on the next render.</summary>
    public void ReleaseVolumes()
    {
        _volumes?.Dispose();
        _volumes = null;
        _antiAliasing?.Dispose();
        _antiAliasing = null;
    }

    public override void DrawGizmos()
    {
        if (GameObject.HideFlags.HasFlag(HideFlags.NoGizmos)) return;

        Texture2D icon = Texture2D.LoadDefault(DefaultTexture.IconCamera);
        if (icon != null) Debug.DrawIcon(icon, Transform.Position, 0.5f, Color.White);

        // The aspect of the camera's last render, 16:9 before it has rendered.
        float aspect = _aspect > 0 ? _aspect : 16f / 9f;
        Float4x4 viewProjectionMatrix = GetProjectionMatrix(aspect) * ViewMatrix;

        Frustum frustum = Frustum.FromMatrix(viewProjectionMatrix);
        Float3[] corners = frustum.GetCorners();

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

    /// <summary>
    /// Adds this camera's view to the frame through <see cref="Graphics.CameraPipeline"/>: the scene seen from the camera,
    /// drawn into its target with its effects. Nothing headless.
    /// </summary>
    public void Render(in CameraRenderOptions options = default)
    {
        using Diagnostics.FrameStatScope stats = Diagnostics.FrameStats.Render.Scope();
        Graphics.CameraPipeline.Render(this, options);
    }

    /// <summary>
    /// Tells the next frame the camera jumped, like a teleport or a scene switch, so motion and every effect's history
    /// start over instead of smearing the old view into the new one.
    /// </summary>
    public void Cut() => _cut = true;

    internal void RenderForward(in CameraRenderOptions options)
    {
        if (Graphics.Renderer == null || !GameObject.IsValid() || !GameObject.Scene.IsValid()) return;
        Scene scene = GameObject.Scene;
        scene.Rendering.Sync();
        RenderWorld? world = scene.Rendering.World;
        if (world == null) return;

        RenderTexture? target = UpdateRenderData(options.FallbackTarget);
        RenderTexture? window = Graphics.WindowTarget;
        Graphite.Framebuffer? framebuffer = target.IsValid() ? target.Framebuffer : window.IsValid() ? window.Framebuffer : null;

        if (options.Kind == ViewKind.Game && XR.ShouldRenderStereo(this))
        {
            if (XR.ClaimEyes(this, out Graphite.Framebuffer eyes))
            {
                ViewDesc stereo = BuildView(world, eyes, options.Kind);
                stereo.Features = BuildFeatures(options.Features);
                stereo.Keywords = options.Keywords;
                Graphics.AddView(stereo);
                _cut = false;
            }
            if (framebuffer != null) XR.MirrorEyes(this, framebuffer);
            return;
        }

        if (framebuffer == null) return;

        ViewDesc view = BuildView(world, framebuffer, options.Kind);
        XR.CoverMirror(framebuffer, view.Viewport);
        view.DependsOn = options.DependsOn;
        view.Features = BuildFeatures(options.Features);
        view.Keywords = options.Keywords;
        if (options.DisplayGizmos)
        {
            Debug.SubmitGizmos(world);
            view.CullingMask |= Debug.GizmoLayerMask;
        }
        Graphics.AddView(view);
        _cut = false;
    }

    /// <summary>
    /// The view this camera would draw into <paramref name="framebuffer"/>, without effects. For hosts that draw a
    /// camera their own way, such as XR or a preview.
    /// </summary>
    public ViewDesc BuildView(RenderWorld world, Graphite.Framebuffer framebuffer, ViewKind kind = ViewKind.Game)
    {
        Float3 position = Transform.Position;
        ViewDesc view = new()
        {
            World = world,
            ViewId = ViewId,
            LayerCount = 1,
            Target = framebuffer,
            CullingMask = (uint)CullingMask.Mask & ~Debug.GizmoLayerMask,
            ClearColor = Background == CameraBackground.Skybox ? GameObject.Scene.Environment.SkyColor.ToFloat4() : BackgroundColor.ToFloat4(),
            Sky = Background == CameraBackground.Skybox && GameObject.Scene.Rendering.HasSky,
            Overlay = Background == CameraBackground.None,
            Near = NearClipPlane,
            FarDistance = FarClipPlane,
            Kind = kind,
            Viewport = ClampViewport(ViewportRect),
            RenderScale = Maths.Clamp(RenderScale, 0.1f, 2.0f),
            Ldr = !HDR,
            Order = Depth,
            Cut = _cut,
        };
        view.Layers[0] = new ViewLayer
        {
            View = Float4x4.CreateLookTo(Float3.Zero, Transform.Forward, Transform.Up),
            Projection = GraphiteProjection(),
            Position = new Double3(position.X, position.Y, position.Z),
        };

        // A two layer target is the headset's eyes, each layer drawn from its eye with the camera as the head
        if (framebuffer.ViewCount == 2)
        {
            view.ViewId = _stereoViewId == 0 ? _stereoViewId = Graphics.NewViewId() : _stereoViewId;
            view.LayerCount = 2;
            view.Viewport = new Float4(0, 0, 1, 1);
            for (int layer = 0; layer < 2; layer++)
                view.Layers[layer] = EyeLayer(XR.GetEyeView(XR.EyeDrawnInLayer(this, layer)));
        }
        return view;
    }

    private ViewLayer EyeLayer(in XRView eye)
    {
        Float3 position = Transform.TransformPoint(eye.Position);
        Quaternion rotation = Transform.Rotation * eye.Rotation;
        float width = eye.TanRight - eye.TanLeft, height = eye.TanUp - eye.TanDown;
        return new ViewLayer
        {
            View = Float4x4.CreateLookTo(Float3.Zero, Quaternion.Forward(rotation), Quaternion.Up(rotation)),
            Projection = new Float4x4(
                new Float4(2f / width, 0, 0, 0),
                new Float4(0, 2f / height, 0, 0),
                new Float4(-(eye.TanRight + eye.TanLeft) / width, -(eye.TanUp + eye.TanDown) / height, 0, 1),
                new Float4(0, 0, Maths.Max(NearClipPlane, 0.0001f), 0)),
            Position = new Double3(position.X, position.Y, position.Z),
        };
    }

    private static Float4 ClampViewport(Float4 rect)
    {
        float x = Maths.Clamp(rect.X, 0, 0.999f), y = Maths.Clamp(rect.Y, 0, 0.999f);
        return new Float4(x, y, Maths.Clamp(rect.Z, 0.001f, 1 - x), Maths.Clamp(rect.W, 0.001f, 1 - y));
    }

    /// <summary>
    /// The projection Graphite draws with: reversed depth, 1 at the near plane falling toward 0 into the distance, with
    /// no far plane for a perspective camera. A hand set projection is turned around to match.
    /// </summary>
    private Float4x4 GraphiteProjection()
    {
        if (_customProjectionMatrix)
        {
            Float4x4 p = _projectionMatrix;
            p.c0.Z = p.c0.W - p.c0.Z;
            p.c1.Z = p.c1.W - p.c1.Z;
            p.c2.Z = p.c2.W - p.c2.Z;
            p.c3.Z = p.c3.W - p.c3.Z;
            return p;
        }
        return ReversedProjection(_aspect);
    }

    private Float4x4 ReversedProjection(float aspect)
    {
        float near = Maths.Max(NearClipPlane, 0.0001f);
        if (IsOrthographic)
        {
            float far = Maths.Max(FarClipPlane, near + 0.001f);
            float height = Maths.Max(OrthographicSize, 0.0001f);
            float range = 1f / (far - near);
            return new Float4x4(
                new Float4(1f / (height * aspect), 0, 0, 0),
                new Float4(0, 1f / height, 0, 0),
                new Float4(0, 0, -range, 0),
                new Float4(0, 0, far * range, 1));
        }

        float fov = Maths.Clamp(FieldOfView, 1f, 179f);
        float y = 1f / Maths.Tan(Maths.ToRadians(fov) * 0.5f);
        return new Float4x4(
            new Float4(y / aspect, 0, 0, 0),
            new Float4(0, y, 0, 0),
            new Float4(0, 0, 0, 1),
            new Float4(0, 0, near, 0));
    }

    // The features of the blended volumes, the tonemapper and the anti aliasing, then the render's own
    private FeatureSet BuildFeatures(IReadOnlyList<RenderFeature>? extra)
    {
        Renderer renderer = Graphics.Renderer!;
        _activeFeatures.Clear();
        if (PostProcessing)
        {
            ResolveVolumes();
            Volumes.CollectFeatures(renderer, this, _activeFeatures);
        }
        if (HDR)
        {
            _tonemapping ??= new TonemappingFeature();
            _activeFeatures.Add(_tonemapping.Get(renderer, Tonemapping));
        }
        _antiAliasing ??= new AntiAliasingFeatures();
        RenderFeature? antiAliasing = _antiAliasing.Get(renderer, AntiAliasing, Fxaa, Smaa, Taa);
        if (antiAliasing != null) _activeFeatures.Add(antiAliasing);
        if (extra != null) _activeFeatures.AddRange(extra);

        List<RenderFeature> stale = [];
        foreach (RenderFeature feature in _features)
            if (!_activeFeatures.Contains(feature)) stale.Add(feature);
        foreach (RenderFeature feature in stale) _features.Remove(feature);
        foreach (RenderFeature feature in _activeFeatures)
            if (!_features.Contains(feature)) _features.Add(feature);
        return _features;
    }

    /// <param name="fallbackTarget">Where this render goes when the camera has no <see cref="Target"/> of its own. Null means the window.</param>
    public RenderTexture? UpdateRenderData(RenderTexture? fallbackTarget = null)
    {
        RenderTexture? camTarget = Target;
        if (camTarget.IsNotValid()) camTarget = fallbackTarget;

        int width = camTarget.IsValid() ? camTarget.Width : Window.InternalWindow != null ? Window.InternalWindow.FramebufferSize.X : 1;
        int height = camTarget.IsValid() ? camTarget.Height : Window.InternalWindow != null ? Window.InternalWindow.FramebufferSize.Y : 1;
        Float4 rect = ClampViewport(ViewportRect);
        width = Maths.Max(1, (int)(width * rect.Z));
        height = Maths.Max(1, (int)(height * rect.W));

        // Screen points use the same space Input.MousePosition reports: target pixels when drawing into a
        // target (the editor's Game view included), window coordinates when drawing to the window.
        _screenSize = camTarget.IsValid()
            ? new Float2(camTarget.Width, camTarget.Height)
            : Window.InternalWindow != null ? new Float2(Window.InternalWindow.Size.X, Window.InternalWindow.Size.Y) : Float2.One;

        float renderScale = Maths.Clamp(RenderScale, 0.1f, 2.0f);
        PixelWidth = (uint)Maths.Max(1, (int)(width * renderScale));
        PixelHeight = (uint)Maths.Max(1, (int)(height * renderScale));

        if (!_customAspect)
            _aspect = width / (float)height;
        if (!_customProjectionMatrix)
            _projectionMatrix = GetProjectionMatrix(_aspect);

        return camTarget;
    }

    public void ResetAspect()
    {
        _aspect = PixelWidth / (float)Maths.Max(1u, PixelHeight);
        _customAspect = false;
    }

    /// <summary>Resets the projection matrix to the one computed from the field of view, aspect and clip planes.</summary>
    public void ResetProjectionMatrix()
    {
        _projectionMatrix = GetProjectionMatrix(_aspect);
        _customProjectionMatrix = false;
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

    private Float4x4 ScreenViewProjection(Float2 screenSize) => GetProjectionMatrix(screenSize.X / screenSize.Y) * ViewMatrix;

    public Ray ScreenPointToRay(Float2 screenPoint, Float2 screenSize)
    {
        Float2 ndc = new(
            (screenPoint.X / screenSize.X) * 2.0f - 1.0f,
            1.0f - (screenPoint.Y / screenSize.Y) * 2.0f
        );

        Float4 nearPointNDC = new(ndc.X, ndc.Y, 0.0f, 1.0f);
        Float4 farPointNDC = new(ndc.X, ndc.Y, 1.0f, 1.0f);

        Float4x4 inverseViewProjectionMatrix = ScreenViewProjection(screenSize).Invert();

        Float4 nearPointWorld = Float4x4.TransformPoint(nearPointNDC, inverseViewProjectionMatrix);
        Float4 farPointWorld = Float4x4.TransformPoint(farPointNDC, inverseViewProjectionMatrix);

        nearPointWorld /= nearPointWorld.W;
        farPointWorld /= farPointWorld.W;

        Float3 rayOrigin = new(nearPointWorld.X, nearPointWorld.Y, nearPointWorld.Z);
        Float3 rayDirection = Float3.Normalize(new Float3(farPointWorld.X, farPointWorld.Y, farPointWorld.Z) - rayOrigin);

        return new Ray(rayOrigin, rayDirection);
    }

    // The projection from the field of view, or orthographic size, and clip planes, depth 0 near to 1 far
    private Float4x4 GetProjectionMatrix(float aspect)
    {
        if (FieldOfView <= 0)
            FieldOfView = 1f;
        if (FieldOfView >= 180)
            FieldOfView = 179f;

        float near = Maths.Max(NearClipPlane, 0.0001f);
        float far = Maths.Max(FarClipPlane, near + 0.001f);
        if (ProjectionMode == ProjectionType.Orthographic)
            return Float4x4.CreateOrtho(OrthographicSize * 2f * aspect, OrthographicSize * 2f, near, far);
        return Float4x4.CreatePerspectiveFov(Maths.ToRadians(FieldOfView), aspect, near, far);
    }
}
