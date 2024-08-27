using Prowl.Icons;
using Prowl.Runtime.RenderPipelines;
using System;

namespace Prowl.Runtime;

[AddComponentMenu($"{FontAwesome6.Tv}  Rendering/{FontAwesome6.Camera}  Camera")]
[ExecuteAlways]
public class Camera : MonoBehaviour
{
    public struct CameraData(int order, Vector3 position, Matrix4x4 view, Matrix4x4 projection, float fieldOfView, float nearClip, float farClip, bool doClear, Color clearColor, Rect viewrect, float renderScale, LayerMask layerMask, AssetRef<RenderTexture> target)
    {
        public int RenderOrder = -order;
        public Vector3 Position = position;
        public Matrix4x4 View = view;
        public Matrix4x4 Projection = projection;
        public float FieldOfView = fieldOfView;
        public float NearClip = nearClip, FarClip = farClip;
        public bool DoClear = doClear;
        public Color ClearColor = clearColor;
        public float RenderScale = renderScale;
        public LayerMask LayerMask = layerMask;
        public Rect Viewrect = viewrect;

        public AssetRef<RenderTexture> Target = target;

        public BoundingFrustum GetFrustrum(float width, float height)
        {
            return new BoundingFrustum(View * Projection);
        }
    }

    public LayerMask LayerMask = LayerMask.Everything;

    public bool DoClear = true;
    public Color ClearColor = new Color(0f, 0f, 0f, 1f);
    public float FieldOfView = 60f;
    public float OrthographicSize = 0.5f;
    public int DrawOrder = 0;
    public Rect Viewrect = new Rect(0, 0, 1, 1);
    public float NearClip = 0.01f;
    public float FarClip = 1000f;

    public float RenderScale = 1.0f;

    public enum ProjectionType { Perspective, Orthographic }
    public ProjectionType projectionType = ProjectionType.Perspective;

    public AssetRef<RenderTexture> Target;
    public AssetRef<RenderPipeline<CameraData>> Pipeline;

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

        // Get the view and projection matrices
        var data = GetData(screenScale);

        // Calculate the inverse view-projection matrix
        Matrix4x4 viewProjectionMatrix = data.View * data.Projection;
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

    public CameraData GetData(Vector2 resolution)
    {
        Matrix4x4 viewMatrix = Matrix4x4.CreateLookToLeftHanded(Transform.position, Transform.forward, Transform.up);
        Matrix4x4 projectionMatrix;
        if (projectionType == ProjectionType.Orthographic)
            projectionMatrix = Matrix4x4.CreateOrthographic(OrthographicSize, OrthographicSize, NearClip, FarClip);
        else
            projectionMatrix = System.Numerics.Matrix4x4.CreatePerspectiveFieldOfViewLeftHanded(FieldOfView.ToRad(), (float)(resolution.x / resolution.y), NearClip, FarClip).ToDouble();

        return new CameraData(DrawOrder, Transform.position, viewMatrix, projectionMatrix, FieldOfView, NearClip, FarClip, DoClear, ClearColor, Viewrect, RenderScale, LayerMask, Target);
    }
}