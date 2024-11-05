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
    public float NearClip = 0.01f;
    public float FarClip = 1000f;
    //public Rect Viewrect = new(0, 0, 1, 1); // Not Implemented
    public int Depth = -1;

    public AssetRef<RenderPipeline> Pipeline;
    public AssetRef<RenderTexture> Target;
    public bool HDR = false;
    public float RenderScale = 1.0f;

    public bool IsOrthographic => projectionType == ProjectionType.Orthographic;

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
        Matrix4x4 viewProjectionMatrix = GetViewMatrix() * GetProjectionMatrix(screenScale);
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

    public Matrix4x4 GetProjectionMatrix(Vector2 resolution, bool accomodateGPUCoordinateSystem = false)
    {
        Matrix4x4 proj;

        if (projectionType == ProjectionType.Orthographic)
            proj = Matrix4x4.CreateOrthographicLeftHanded(OrthographicSize, OrthographicSize, NearClip, FarClip);
        else
            proj = Matrix4x4.CreatePerspectiveFieldOfViewLeftHanded(FieldOfView.ToRad(), (float)(resolution.x / resolution.y), NearClip, FarClip);

        if (accomodateGPUCoordinateSystem)
            proj = Graphics.GetGPUProjectionMatrix(proj);

        return proj;
    }

    public BoundingFrustum GetFrustum(Vector2 resolution)
    {
        Matrix4x4 view = GetViewMatrix();
        Matrix4x4 proj = GetProjectionMatrix(resolution, true);

        return new(view * proj);
    }
}
