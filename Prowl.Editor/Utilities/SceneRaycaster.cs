// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;
using Prowl.Runtime.RenderPipelines;

using Veldrid;

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
        s_scenePickMaterial ??= new Material(Application.AssetProvider.LoadAsset<Runtime.Shader>("Defaults/ScenePicker.shader"));

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
            renderable.GetRenderingData(out PropertyBlock properties, out IGeometryDrawData drawData, out Matrix4x4 model);

            model.Translation -= cameraPosition;
            model = Graphics.GetGPUModelMatrix(model);

            buffer.SetMatrix("_Matrix_MVP", model.ToFloat() * floatVP);

            buffer.ApplyPropertyState(properties);

            buffer.UpdateBuffer("_PerDraw");

            buffer.SetDrawData(drawData);
            buffer.DrawIndexed((uint)drawData.IndexCount, 0, 1, 0, 0);
        }

        Graphics.SubmitCommandBuffer(buffer, true);

        CommandBufferPool.Release(buffer);

        // ID is packed into 8-bit 4-channel vector
        Color32 id = temporary.ColorBuffers[0].GetPixel<Color32>((uint)rayUV.x, (uint)screenScale.y - (uint)rayUV.y);

        objectID = id.r;
        objectID |= id.g << 8;
        objectID |= id.b << 16;
        objectID |= id.a << 24;

        // TODO : Find out how to PROPERLY get the depth texture, since this does not appear to work.
        // Get depth
        // float depth = GetDepth(temporary.DepthBuffer, (uint)rayUV.x, (uint)screenScale.y - (uint)rayUV.y, camera.NearClip, camera.FarClip);
        // position = ray.Position(depth);

        position = ray.Position(0);

        return objectID > 0;
    }


    static float GetDepth(Texture2D texture, uint x, uint y, double near, double far)
    {
        float depth = 0;

        switch (texture.Format)
        {
            case PixelFormat.D16_UNorm:
            case PixelFormat.D16_UNorm_S8_UInt:
                uint depth16 = texture.GetPixel<Int16>(x, y).ToUInt();
                depth = (float)((double)depth16 / ushort.MaxValue);

                break;

            case PixelFormat.D24_UNorm_S8_UInt:
                uint depth24 = texture.GetPixel<Int24>(x, y).ToUInt();
                depth = (float)((double)depth24 / 16777215);

                break;

            case PixelFormat.D32_Float:
            case PixelFormat.D32_Float_S8_UInt:
                depth = texture.GetPixel<float>(x, y);
                break;
        }

        //depth = (float)((2.0 * near * far) / (far + near - depth * (far - near)));

        return depth;
    }


    private struct Int16
    {
        private readonly byte _l1, _l2;

        public readonly uint ToUInt()
        {
            uint value = _l1;
            value |= (uint)_l2 << 8;

            return value;
        }
    }


    private struct Int24
    {
        private readonly byte _l1, _l2, _l3;

        public readonly uint ToUInt()
        {
            uint value = _l1;
            value |= (uint)_l2 << 8;
            value |= (uint)_l3 << 16;

            return value;
        }
    }
}
