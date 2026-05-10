// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Vector.Geometry;

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
    /// Depth texture modes this effect requires. The pipeline gathers these from all
    /// active effects to determine which passes to run (e.g. motion vectors).
    /// Override to declare requirements instead of setting camera.DepthTextureMode manually.
    /// </summary>
    public virtual DepthTextureMode RequiredDepthTextureMode => DepthTextureMode.None;

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
}

public enum CameraClearFlags
{
    Nothing,
    SolidColor,
    Depth,
    Skybox,
}

[Flags]
public enum DepthTextureMode
{
    None = 0,
    /// <summary>
    /// When enabled rendering will draw a Pre-Depth pass before the main rendering pass.
    /// This can improve overdrawing and sorting issues, but can be slower on some hardware and some cases.
    /// This also enables the _CameraDepthTexture shader property.
    /// </summary>
    Depth = 1, // _CameraDepthTexture
    //Normal = 2, // _CameraNormalsTexture
    MotionVectors = 4, // _CameraMotionVectorsTexture
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
    public float OrthographicSize = 0.5f;
    public float NearClipPlane = 0.1f;
    public float FarClipPlane = 100f;
    //public Rect Viewrect = new(0, 0, 1, 1); // Not Implemented
    public int Depth = -1;

    public RenderPipeline? Pipeline;
    public RenderTexture? Target;
    public bool HDR = false;
    public float RenderScale = 1.0f;

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

    // Previous frame state, written by the render pipeline at end of frame.
    private Float4x4 _previousViewProjectionMatrix;
    private bool _hasPreviousViewProjectionMatrix;

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

    public Float4x4 ViewMatrix { get; private set; }

    /// <summary>
    /// The previous frame's unjittered view-projection matrix.
    /// Set by the render pipeline at the end of each frame. Used for motion vectors
    /// and temporal reprojection. Returns identity on the first frame.
    /// </summary>
    public Float4x4 PreviousViewProjectionMatrix => _previousViewProjectionMatrix;

    /// <summary>
    /// Whether a valid previous view-projection matrix exists (false on the first frame).
    /// </summary>
    public bool HasPreviousViewProjectionMatrix => _hasPreviousViewProjectionMatrix;

    public Camera() : base() { }

    public override void OnEnable()
    {
        _hasPreviousViewProjectionMatrix = false;
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
        var icon = Resources.Texture2D.LoadDefault(Resources.DefaultTexture.IconCamera);
        if (icon != null) Debug.DrawIcon(icon, Transform.Position, 0.5f, Color.White);

        float aspect = 1280 / 720;
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
        RenderPipeline pipeline = Pipeline ?? DefaultRenderPipeline.Default;
        pipeline.Render(this, data ?? new());
    }

    public RenderTexture? UpdateRenderData()
    {
        // Since Scene Updating is guranteed to execute before rendering, we can setup camera data for this frame here
        RenderTexture? camTarget = null;

        if (Target.IsValid())
            camTarget = Target;

        int width = camTarget?.Width ?? Window.InternalWindow.FramebufferSize.X;
        int height = camTarget?.Height ?? Window.InternalWindow.FramebufferSize.Y;

        float renderScale = Maths.Clamp(RenderScale, 0.1f, 2.0f);
        PixelWidth = (uint)Maths.Max(1, (int)(width * renderScale));
        PixelHeight = (uint)Maths.Max(1, (int)(height * renderScale));

        if (!_customAspect)
            _aspect = PixelWidth / (float)PixelHeight;

        // Recompute the base projection unless the user set it manually
        if (!_customProjectionMatrix)
            _projectionMatrix = GetProjectionMatrix(_aspect);

        // Keep the non-jittered projection in sync unless explicitly overridden
        if (!_customNonJitteredProjectionMatrix)
            _nonJitteredProjectionMatrix = GetProjectionMatrix(_aspect);

        ViewMatrix = Float4x4.CreateLookTo(Transform.Position, Transform.Forward, Transform.Up);

        return camTarget;
    }

    /// <summary>
    /// Called by the render pipeline at end of frame to store the current unjittered
    /// view-projection matrix as the previous frame's matrix for next frame's motion vectors.
    /// </summary>
    public void SavePreviousViewProjectionMatrix()
    {
        _previousViewProjectionMatrix = _nonJitteredProjectionMatrix * ViewMatrix;
        _hasPreviousViewProjectionMatrix = true;
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
        _hasPreviousViewProjectionMatrix = false;
    }

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

        // Calculate the inverse view-projection matrix using unjittered projection
        float aspect = screenSize.X / screenSize.Y;
        Float4x4 viewProjectionMatrix = GetProjectionMatrix(aspect) * GetViewMatrix();
        Float4x4 inverseViewProjectionMatrix = viewProjectionMatrix.Invert();

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
            proj = Float4x4.CreateOrtho(OrthographicSize, OrthographicSize, NearClipPlane, FarClipPlane);
        else
            proj = Float4x4.CreatePerspectiveFov(Maths.ToRadians(FieldOfView), aspect, NearClipPlane, FarClipPlane);

        return proj;
    }
}
