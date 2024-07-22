using Prowl.Icons;
using System;

namespace Prowl.Runtime;

[AddComponentMenu($"{FontAwesome6.Tv}  Rendering/{FontAwesome6.Camera}  Camera")]
[ExecuteAlways]
public class Camera : MonoBehaviour
{
    public struct CameraData(int order, Vector3 position, Vector3 forward, Vector3 up, float fieldOfView, float nearClip, float farClip, bool doClear, Color clearColor, Rect viewrect, bool isOrthographic, float orthographicSize, float renderScale, LayerMask layerMask, AssetRef<RenderTexture> target)
    {
        public int RenderOrder = -order;
        public Vector3 Position = position;
        public Vector3 Forward = forward;
        public Vector3 Up = up;
        public float FieldOfView = fieldOfView;
        public float NearClip = nearClip, FarClip = farClip;
        public bool DoClear = doClear;
        public Color ClearColor = clearColor;
        public bool IsOrthographic = isOrthographic;
        public float OrthographicSize = orthographicSize;
        public float RenderScale = renderScale;
        public LayerMask LayerMask = layerMask;
        public Rect Viewrect = viewrect;

        public AssetRef<RenderTexture> Target = target;

        public Matrix4x4 View => Matrix4x4.CreateLookToLeftHanded(Position, Forward, Up);

        public static CameraData CreatePerspective(int order, Vector3 position, Vector3 forward, Vector3 up, float fieldOfView, float nearClip, float farClip, bool doClear, Color clearColor, Rect viewrect, LayerMask layerMask, AssetRef<RenderTexture> target)
        {
            return new CameraData(order, position, forward, up, fieldOfView, nearClip, farClip, doClear, clearColor, viewrect, false, 0f, 1f, layerMask, target);
        }

        public static CameraData CreateOrthographic(int order, Vector3 position, Vector3 forward, Vector3 up, float orthographicSize, float nearClip, float farClip, bool doClear, Color clearColor, Rect viewrect, LayerMask layerMask, AssetRef<RenderTexture> target)
        {
            return new CameraData(order, position, forward, up, 0f, nearClip, farClip, doClear, clearColor, viewrect, true, orthographicSize, 1f, layerMask, target);
        }

        public Matrix4x4 GetProjectionMatrix(float width, float height)
        {
            if (IsOrthographic)
                //return System.Numerics.Matrix4x4.CreateOrthographicLeftHanded(width, height, NearClip, FarClip).ToDouble();
                return System.Numerics.Matrix4x4.CreateOrthographicOffCenterLeftHanded(-OrthographicSize, OrthographicSize, -OrthographicSize, OrthographicSize, NearClip, FarClip).ToDouble();
            else
                return System.Numerics.Matrix4x4.CreatePerspectiveFieldOfViewLeftHanded(FieldOfView.ToRad(), width / height, NearClip, FarClip).ToDouble();
        }

        public BoundingFrustum GetFrustrum(float width, float height)
        {
            Matrix4x4 viewMatrix = View;
            Matrix4x4 projectionMatrix = GetProjectionMatrix(width, height);

            return new BoundingFrustum(viewMatrix * projectionMatrix);
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
        Matrix4x4 viewMatrix = Matrix4x4.CreateLookToLeftHanded(GameObject.Transform.position, GameObject.Transform.forward, GameObject.Transform.up);
        Matrix4x4 projectionMatrix = GetData().GetProjectionMatrix((int)screenScale.x, (int)screenScale.y);

        // Calculate the inverse view-projection matrix
        Matrix4x4 viewProjectionMatrix = viewMatrix * projectionMatrix;
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

    public CameraData GetData()
    {
        return new CameraData(DrawOrder, Transform.position, Transform.forward, Transform.up, FieldOfView, NearClip, FarClip, DoClear, ClearColor, Viewrect, projectionType == ProjectionType.Orthographic, OrthographicSize, RenderScale, LayerMask, Target);
    }
}