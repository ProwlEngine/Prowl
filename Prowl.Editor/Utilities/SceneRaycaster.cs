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
            return new MeshHitInfo(EngineObject.FindObjectByID<GameObject>(id), pos);

        return null;
    }


    public static GameObject? GetObject(Camera cam, Vector2 rayUV, Vector2 screenScale)
    {
        if (RenderRaycast(cam, rayUV, screenScale, out Vector3 pos, out int id))
            return EngineObject.FindObjectByID<GameObject>(id);

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
        MeshRenderer.ray = ray;

        intersectRenderables.Clear();

        foreach (IRenderable renderable in RenderPipeline.GetRenderables())
        {
            renderable.GetCullingData(out bool isRenderable, out Bounds bounds);

            if (!isRenderable)
                continue;

            intersectRenderables.Add(renderable);
        }

        RenderTexture temporary = RenderTexture.GetTemporaryRT((uint)screenScale.x, (uint)screenScale.y, [PixelFormat.R32_Float, PixelFormat.R32_Float]);

        CommandBuffer buffer = CommandBufferPool.Get("Scene Picking Command Buffer");

        buffer.SetRenderTarget(temporary);
        buffer.ClearRenderTarget(true, true, Color.clear);

        Matrix4x4 view = camera.GetViewMatrix(true);
        Vector3 cameraPosition = camera.Transform.position;

        Matrix4x4 projection = camera.GetProjectionMatrix(screenScale, true);
        Matrix4x4 vp = view * projection;
        System.Numerics.Matrix4x4 floatVP = vp.ToFloat();

        buffer.SetVector("_CameraPosition", camera.Transform.position);
        buffer.SetMaterial(s_scenePickMaterial);

        int a = 0;
        foreach (IRenderable renderable in intersectRenderables)
        {
            a++;
            renderable.GetRenderingData(out PropertyBlock properties, out IGeometryDrawData drawData, out Matrix4x4 model);

            buffer.SetMatrix("_Matrix_M", model.ToFloat());

            model.Translation -= cameraPosition;
            Matrix4x4 gpuModel = Graphics.GetGPUModelMatrix(model);

            buffer.SetMatrix("_Matrix_MVP", gpuModel.ToFloat() * floatVP);

            buffer.ApplyPropertyState(properties);

            buffer.UpdateBuffer("_PerDraw");

            buffer.SetDrawData(drawData);
            buffer.DrawIndexed((uint)drawData.IndexCount, 0, 1, 0, 0);
        }

        Graphics.SubmitCommandBuffer(buffer, true, 10000000);

        CommandBufferPool.Release(buffer);

        float distance = temporary.ColorBuffers[0].GetPixel<float>((uint)rayUV.x, (uint)rayUV.y);
        float id = temporary.ColorBuffers[1].GetPixel<float>((uint)rayUV.x, (uint)rayUV.y);

        objectID = (int)id;
        position = ray.Position(distance);

        // Debug.Log($"ID : {objectID}, Pos : {position}");

        MeshRenderer.pos = position;

        return objectID > 0;
    }
}
