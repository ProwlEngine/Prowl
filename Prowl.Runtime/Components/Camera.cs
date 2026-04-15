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

    [SerializeIgnore]
    public DepthTextureMode DepthTextureMode = DepthTextureMode.None;

    private float _aspect;
    private bool _customAspect;
    private Float4x4 _projectionMatrix;
    private bool _customProjectionMatrix;

    private Float4x4 _previousViewMatrix;
    private Float4x4 _previousProjectionMatrix;
    private Float4x4 _previousViewProjectionMatrix;
    private bool _firstFrame = true;

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

    public Float4x4 ProjectionMatrix
    {
        get => _projectionMatrix;
        set
        {
            _projectionMatrix = value;
            _customProjectionMatrix = true;
        }
    }

    public Float4x4 ViewMatrix { get; private set; }

    public Float4x4 PreviousViewMatrix => _previousViewMatrix;
    public Float4x4 PreviousProjectionMatrix => _previousProjectionMatrix;
    public Float4x4 PreviousViewProjectionMatrix => _previousViewProjectionMatrix;

    public Camera() : base() { }

    public override void OnEnable()
    {
        _firstFrame = true;
    }

    public override void DrawGizmos()
    {
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
        if (!_firstFrame)
        {
            _previousViewMatrix = ViewMatrix;
            _previousProjectionMatrix = _projectionMatrix;
            _previousViewProjectionMatrix = _projectionMatrix * ViewMatrix;
        }
        _firstFrame = false;

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

        if (!_customProjectionMatrix)
        {
            _projectionMatrix = GetProjectionMatrix(_aspect);
        }

        ViewMatrix = Float4x4.CreateLookTo(Transform.Position, Transform.Forward, Transform.Up);

        return camTarget;
    }

    public void ResetAspect()
    {
        _aspect = PixelWidth / (float)PixelHeight;
        _customAspect = false;
    }

    public void ResetProjectionMatrix()
    {
        _projectionMatrix = GetProjectionMatrix(_aspect);
        _customProjectionMatrix = false;
    }

    public void ResetMotionHistory()
    {
        _firstFrame = true;
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

        // Calculate the inverse view-projection matrix
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
