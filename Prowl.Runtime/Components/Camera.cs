// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Graphite;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime;

public enum CameraClearFlags
{
    Nothing,
    SolidColor,
    Depth,
    Skybox,
}

[AddComponentMenu("Rendering/Camera")]
[ComponentIcon("\uf030")] // Camera
public class Camera : MonoBehaviour
{
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

    public override void DrawGizmos()
    {
        if (GameObject.HideFlags.HasFlag(HideFlags.NoGizmos)) return;

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

    /// <summary>
    /// Returns the matrix needed to normalize a projection matrix built for an OpenGL-style
    /// clip space (Y up) onto the active graphics backend's actual clip space orientation.
    /// Camera.ProjectionMatrix itself is left in the OpenGL-style convention (editor tools like
    /// the transform gizmo do their own CPU-side screen projection assuming that convention);
    /// the render pipeline applies this flip only to the copy it uploads to the GPU.
    /// </summary>
    public static Float4x4 GetCoordinateSystemFlip()
    {
        return Graphics.Device.IsClipSpaceYInverted
            ? Float4x4.Identity
            : Float4x4.CreateScale(1f, -1f, 1f);
    }
}
