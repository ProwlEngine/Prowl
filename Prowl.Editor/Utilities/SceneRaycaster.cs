// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.
using Prowl.Runtime;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Rendering.Pipelines;

using Veldrid;

using Shader = Prowl.Runtime.Shader;

namespace Prowl.Editor;

public static class SceneRaycaster
{
    private static Material s_scenePickMaterial;

    public record MeshHitInfo(GameObject? gameObject, Vector3 worldPosition);

    private static List<IRenderable> intersectRenderables = [];


    public static MeshHitInfo? Raycast(Camera cam, Vector2 rayUV, Vector2 screenScale)
    {
        if (RenderRaycast(cam, rayUV, screenScale, out Vector3 pos, out int id))
            return new MeshHitInfo(EngineObject.FindObjectByID<MeshRenderer>(id)?.GameObject, pos);

        return null;
    }


    public static GameObject? GetObject(Camera cam, Vector2 rayUV, Vector2 screenScale)
    {
        if (RenderRaycast(cam, rayUV, screenScale, out Vector3 pos, out int id))
            return EngineObject.FindObjectByID<MeshRenderer>(id)?.GameObject;

        return null;
    }


    public static Vector3? GetPosition(Camera cam, Vector2 rayUV, Vector2 screenScale)
    {
        if (RenderRaycast(cam, rayUV, screenScale, out Vector3 pos, out int id))
            return pos;

        return null;
    }


    public static void RenderTest(Camera camera, Vector2 scale)
    {
        RenderRaycast(camera, new Vector2(scale.x / 2, scale.y / 2), scale, out _, out _);
    }


    private static bool RenderRaycast(Camera camera, Vector2 rayUV, Vector2 screenScale, out Vector3 position, out int objectID)
    {
        s_scenePickMaterial ??= new Material(Application.AssetProvider.LoadAsset<Shader>("Defaults/ScenePicker.shader"));

        Ray ray = camera.ScreenPointToRay(rayUV, screenScale);

        intersectRenderables.Clear();

        foreach (IRenderable renderable in RenderPipeline.GetRenderables())
        {
            renderable.GetCullingData(out bool isRenderable, out Bounds bounds);

            if (!isRenderable)
                continue;

            intersectRenderables.Add(renderable);
        }

        RenderTexture temporary = RenderTexture.GetTemporaryRT((uint)screenScale.x, (uint)screenScale.y, [PixelFormat.R8_G8_B8_A8_UNorm]);

        CommandBuffer buffer = CommandBufferPool.Get("Scene Picking Command Buffer");

        buffer.SetRenderTarget(temporary);
        buffer.ClearRenderTarget(true, true, Color.clear);

        Matrix4x4 view = camera.GetViewMatrix(false);
        Vector3 cameraPosition = camera.Transform.position;

        Matrix4x4 projection = camera.GetProjectionMatrix(screenScale, true);
        Matrix4x4 vp = view * projection;
        System.Numerics.Matrix4x4 floatVP = vp.ToFloat();

        buffer.SetMaterial(s_scenePickMaterial);

        foreach (IRenderable renderable in intersectRenderables)
        {
            renderable.GetRenderingData(out PropertyState properties, out IGeometryDrawData drawData, out Matrix4x4 model);

            model.Translation -= cameraPosition;
            // model = Graphics.GetGPUModelMatrix(model);

            buffer.SetMatrix("_Matrix_MVP", model.ToFloat() * floatVP);

            buffer.ApplyPropertyState(properties);

            buffer.UpdateBuffer("_PerDraw");

            buffer.SetDrawData(drawData);
            buffer.DrawIndexed((uint)drawData.IndexCount, 0, 1, 0, 0);
        }

        using GraphicsFence fence = new();
        Graphics.SubmitCommandBuffer(buffer, fence);
        Graphics.WaitForFence(fence);

        CommandBufferPool.Release(buffer);

        uint x = (uint)rayUV.x;
        uint y = (uint)screenScale.y - (uint)rayUV.y;

        // ID is packed into 8-bit 4-channel vector
        Color32 id = temporary.ColorBuffers[0].GetPixel<Color32>(x, y);
        float depth = GetDepth(temporary.DepthBuffer, x, y, camera.NearClip, camera.FarClip);

        objectID = id.r | id.g << 8 | id.b << 16 | id.a << 24;
        position = ray.Position(depth);

        return objectID > 0;
    }


    static float GetDepth(Texture2D texture, uint x, uint y, double near, double far)
    {
        float depth;

        switch (texture.Format)
        {
            case PixelFormat.D16_UNorm:
                depth = texture.GetPixel<ushort>(x, y) / 65535.0f; // Untested
                break;

            case PixelFormat.D16_UNorm_S8_UInt:
                depth = texture.GetPixel<(ushort, byte)>(x, y).Item1 / 65535.0f; // Untested
                break;

            case PixelFormat.D24_UNorm_S8_UInt:
                (ushort depth1, byte depth2, _) = texture.GetPixel<(ushort, byte, byte)>(x, y);

                // This does not seem correct - depth2 should not be after depth1. However, it seems to work fine.
                depth = ((uint)depth2 << 16 | depth1) / 16777215.0f;
                break;

            case PixelFormat.D32_Float:
            case PixelFormat.D32_Float_S8_UInt:
                depth = texture.GetPixel<float>(x, y);
                break;

            default:
                throw new Exception($"Unsupported depth format: {texture.Format}");
        }

        return (float)(far * near / (far - (far - near) * depth));
    }
}
