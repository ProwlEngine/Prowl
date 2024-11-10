// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Linq;

using Prowl.Icons;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Rendering.Pipelines;

namespace Prowl.Runtime;

public enum CameraClearFlags
{
    None,
    DepthOnly,
    ColorOnly,
    DepthColor,
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

[AddComponentMenu($"{FontAwesome6.Tv}  Rendering/{FontAwesome6.Camera}  Camera")]
public class Camera : MonoBehaviour
{
    public CameraClearFlags ClearFlags = CameraClearFlags.Skybox;
    public Color ClearColor = new(0f, 0f, 0f, 1f);
    public LayerMask CullingMask = LayerMask.Everything;

    public enum ProjectionType { Perspective, Orthographic }
    public ProjectionType projectionType = ProjectionType.Perspective;

    [ShowIf(nameof(IsOrthographic), true)] public float FieldOfView = 60f;
    [ShowIf(nameof(IsOrthographic))] public float OrthographicSize = 0.5f;
    public float NearClipPlane = 0.01f;
    public float FarClipPlane = 1000f;
    //public Rect Viewrect = new(0, 0, 1, 1); // Not Implemented
    public int Depth = -1;

    public AssetRef<RenderPipeline> Pipeline;
    public AssetRef<RenderTexture> Target;
    public bool HDR = false;
    public float RenderScale = 1.0f;

    public bool IsOrthographic => projectionType == ProjectionType.Orthographic;

    [HideInInspector, SerializeIgnore]
    public DepthTextureMode DepthTextureMode = DepthTextureMode.None;

    private static WeakReference<Camera> s_mainCamera = new(null);
    public static Camera? Main
    {
        get
        {
            if (s_mainCamera.TryGetTarget(out Camera? camera) && camera != null)
                return camera;

            camera = GameObject.FindGameObjectWithTag("Main Camera")?.GetComponent<Camera>() ?? GameObject.FindObjectsOfType<Camera>().FirstOrDefault();
            if(camera != null)
                s_mainCamera.SetTarget(camera);
            return camera;
        }
        internal set => s_mainCamera.SetTarget(value);
    }

    private float _aspect;
    private bool _customAspect;
    private Matrix4x4 _projectionMatrix;
    private Matrix4x4 _nonJitteredProjectionMatrix;
    private bool _customProjectionMatrix;

    private Matrix4x4 _previousViewMatrix;
    private Matrix4x4 _previousProjectionMatrix;
    private Matrix4x4 _previousViewProjectionMatrix;
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

    public Matrix4x4 ProjectionMatrix
    {
        get => _projectionMatrix;
        set
        {
            _projectionMatrix = value;
            _customProjectionMatrix = true;
        }
    }

    public Matrix4x4 NonJitteredProjectionMatrix
    {
        get => _nonJitteredProjectionMatrix;
        set
        {
            _nonJitteredProjectionMatrix = value;
            _customProjectionMatrix = true;
        }
    }

    public bool UseJitteredProjectionMatrixForTransparentRendering { get; set; }

    public Matrix4x4 ViewMatrix { get; private set; }
    public Matrix4x4 OriginViewMatrix { get; private set; }

    public Matrix4x4 PreviousViewMatrix => _previousViewMatrix;
    public Matrix4x4 PreviousProjectionMatrix => _previousProjectionMatrix;
    public Matrix4x4 PreviousViewProjectionMatrix => _previousViewProjectionMatrix;

    public override void OnEnable()
    {
        _firstFrame = true;
    }

    public void Render(in RenderingData? data = null)
    {
        RenderPipeline pipeline = Pipeline.Res ?? DefaultRenderPipeline.Default;
        pipeline.Render(this, data ?? new());
    }

    public Veldrid.Framebuffer UpdateRenderData()
    {
        if (!_firstFrame)
        {
            _previousViewMatrix = ViewMatrix;
            _previousProjectionMatrix = _projectionMatrix;
            _previousViewProjectionMatrix = ViewMatrix * _projectionMatrix;
        }
        _firstFrame = false;

        // Since Scene Updating is guranteed to execute before rendering, we can setup camera data for this frame here
        Veldrid.Framebuffer camTarget = Graphics.ScreenTarget;

        if (Target.Res != null)
            camTarget = Target.Res.Framebuffer;

        float renderScale = Math.Clamp(RenderScale, 0.1f, 2.0f);
        PixelWidth = (uint)Math.Max(1, (int)(camTarget.Width * renderScale));
        PixelHeight = (uint)Math.Max(1, (int)(camTarget.Height * renderScale));

        if (!_customAspect)
            _aspect = PixelWidth / (float)PixelHeight;

        if (!_customProjectionMatrix)
        {
            _projectionMatrix = GetProjectionMatrix(_aspect, true);
            _nonJitteredProjectionMatrix = _projectionMatrix;
        }

        ViewMatrix = Matrix4x4.CreateLookToLeftHanded(Transform.position, Transform.forward, Transform.up);
        OriginViewMatrix = Matrix4x4.CreateLookToLeftHanded(Vector3.zero, Transform.forward, Transform.up);

        return camTarget;
    }

    public void ResetAspect()
    {
        _aspect = PixelWidth / (float)PixelHeight;
        _customAspect = false;
    }

    public void ResetProjectionMatrix()
    {
        _projectionMatrix = _nonJitteredProjectionMatrix;
        _customProjectionMatrix = false;
    }

    public void ResetMotionHistory()
    {
        _firstFrame = true;
    }

    public Ray ScreenPointToRay(Vector2 screenPoint, Vector2 screenScale)
    {
        // Normalize screen coordinates to [-1, 1]
        Vector2 ndc = new Vector2(
            (screenPoint.x / screenScale.x) * 2.0f - 1.0f,
            1.0f - (screenPoint.y / screenScale.y) * 2.0f
        );

        // Create the near and far points in NDC
        Vector4 nearPointNDC = new Vector4(ndc.x, ndc.y, 0.0f, 1.0f);
        Vector4 farPointNDC = new Vector4(ndc.x, ndc.y, 1.0f, 1.0f);

        // Calculate the inverse view-projection matrix
        double aspect = screenScale.x / screenScale.y;
        Matrix4x4 viewProjectionMatrix = GetViewMatrix() * GetProjectionMatrix((float)aspect);
        Matrix4x4.Invert(viewProjectionMatrix, out Matrix4x4 inverseViewProjectionMatrix);

        // Unproject the near and far points to world space
        Vector4 nearPointWorld = Vector4.Transform(nearPointNDC, inverseViewProjectionMatrix);
        Vector4 farPointWorld = Vector4.Transform(farPointNDC, inverseViewProjectionMatrix);

        // Perform perspective divide
        nearPointWorld /= nearPointWorld.w;
        farPointWorld /= farPointWorld.w;

        // Create the ray
        Vector3 rayOrigin = new Vector3(nearPointWorld.x, nearPointWorld.y, nearPointWorld.z);
        Vector3 rayDirection = Vector3.Normalize(new Vector3(farPointWorld.x, farPointWorld.y, farPointWorld.z) - rayOrigin);

        return new Ray(rayOrigin, rayDirection);
    }

    public Matrix4x4 GetViewMatrix(bool applyPosition = true)
    {
        Vector3 position = applyPosition ? Transform.position : Vector3.zero;

        return Matrix4x4.CreateLookToLeftHanded(position, Transform.forward, Transform.up);
    }

    private Matrix4x4 GetProjectionMatrix(float aspect, bool accomodateGPUCoordinateSystem = false)
    {
        Matrix4x4 proj;

        if (projectionType == ProjectionType.Orthographic)
            proj = Matrix4x4.CreateOrthographicLeftHanded(OrthographicSize, OrthographicSize, NearClipPlane, FarClipPlane);
        else
            proj = Matrix4x4.CreatePerspectiveFieldOfViewLeftHanded(FieldOfView.ToRad(), aspect, NearClipPlane, FarClipPlane);

        if (accomodateGPUCoordinateSystem)
            proj = Graphics.GetGPUProjectionMatrix(proj);

        return proj;
    }
}
