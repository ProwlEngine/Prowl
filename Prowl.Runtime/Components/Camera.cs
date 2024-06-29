using Prowl.Icons;
using Prowl.Runtime.SceneManagement;
using System;
using System.Collections.Generic;

namespace Prowl.Runtime;

[AddComponentMenu($"{FontAwesome6.Tv}  Rendering/{FontAwesome6.Camera}  Camera")]
[ExecuteAlways]
public class Camera : MonoBehaviour
{
    public static Camera? Current;

    public bool DoClear = true;
    public Color ClearColor = new Color(0f, 0f, 0f, 1f);
    public float FieldOfView = 60f;
    public float OrthographicSize = 0.5f;
    public int DrawOrder = 0;
    public float NearClip = 0.01f;
    public float FarClip = 1000f;

    public bool ShowGizmos = false;

    public enum ProjectionType { Perspective, Orthographic }
    public ProjectionType projectionType = ProjectionType.Perspective;

    public AssetRef<RenderTexture> Target;

    public Matrix4x4 GetProjectionMatrix(float width, float height)
    {
        if (projectionType == ProjectionType.Orthographic)
            //return System.Numerics.Matrix4x4.CreateOrthographicLeftHanded(width, height, NearClip, FarClip).ToDouble();
            return System.Numerics.Matrix4x4.CreateOrthographicOffCenterLeftHanded(-OrthographicSize, OrthographicSize, -OrthographicSize, OrthographicSize, NearClip, FarClip).ToDouble();
        else
            return System.Numerics.Matrix4x4.CreatePerspectiveFieldOfViewLeftHanded(FieldOfView.ToRad(), width / height, NearClip, FarClip).ToDouble();
    }

    public void SetupRenderParameters(Vector2 defaultTargetSize)
    {    
        Vector2 size = defaultTargetSize;

        if (Target.IsAvailable) 
            size = new Vector2(Target.Res!.Width, Target.Res!.Height);

        Current = this;

        Matrix4x4 MatView = View;
        Matrix4x4 MatProjection = GetProjectionMatrix((float)size.x, (float)size.y);
        Matrix4x4 OldMatView = oldView ?? MatView;
        Matrix4x4 OldMatProjection = oldProjection ?? MatProjection;

        Matrix4x4.Invert(MatProjection, out Matrix4x4 MatProjectionInverse);

        oldView = View;
        oldProjection = MatProjection;
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

        // Get the view and projection matrices
        Matrix4x4 viewMatrix = Matrix4x4.CreateLookToLeftHanded(GameObject.Transform.position, GameObject.Transform.forward, GameObject.Transform.up);
        Matrix4x4 projectionMatrix = GetProjectionMatrix((int)screenScale.x, (int)screenScale.y);

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
}
