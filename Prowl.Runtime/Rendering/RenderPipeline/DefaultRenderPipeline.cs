// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Veldrid;

namespace Prowl.Runtime.Rendering.Pipelines;

/// <summary>
/// Default rendering pipeline implementation that handles standard forward rendering,
/// post-processing effects, shadows, and debug visualization.
/// </summary>
public class DefaultRenderPipeline : RenderPipeline
{
    const bool CAMERA_RELATIVE = true;
    private const float GRID_SCALE = 1000f;
    private const float GRID_FALLOFF = 15.0f;


    #region Static Resources

    private static Mesh s_quadMesh;
    private static Mesh s_skyDome;
    private static Material s_gridMaterial;
    private static Material s_defaultMaterial;
    private static Material s_skybox;
    private static Material s_gizmo;

    private static RenderTexture? ShadowMap;
    private static GraphicsBuffer? LightBuffer;
    private static int LightCount;

    public static DefaultRenderPipeline Default { get; } = new();

    #endregion

    #region Resource Management

    private static void ValidateDefaults()
    {
        s_quadMesh ??= Mesh.CreateQuad(Vector2.one);
        s_gridMaterial ??= new Material(Application.AssetProvider.LoadAsset<Shader>("Defaults/Grid.shader"));
        s_defaultMaterial ??= new Material(Application.AssetProvider.LoadAsset<Shader>("Defaults/Standard.shader"));
        s_skybox ??= new Material(Application.AssetProvider.LoadAsset<Shader>("Defaults/ProceduralSky.shader"));
        s_gizmo ??= new Material(Application.AssetProvider.LoadAsset<Shader>("Defaults/Gizmo.shader"));

        if (s_skyDome == null)
        {
            GameObject skyDomeModel = Application.AssetProvider.LoadAsset<GameObject>("Defaults/SkyDome.obj").Res;
            MeshRenderer renderer = skyDomeModel.GetComponentInChildren<MeshRenderer>(true, true);

            s_skyDome = renderer.Mesh.Res;
        }
    }

    #endregion

    #region Main Rendering

    public override void Render(Framebuffer target, Camera camera, in RenderingData data)
    {
        ValidateDefaults();

        bool isHDR = data.IsSceneViewCamera ?
            (Camera.Main?.HDR ?? camera.HDR) :
            camera.HDR;

        var (all, opaqueEffects, finalEffects) = GatherImageEffects(camera, data.IsSceneViewCamera);
        var buffer = PrepareCommandBuffer(target, camera, isHDR, out RenderTexture? forwardBuffer);

        try
        {
            // Main rendering with correct order of operations
            RenderScene(buffer, camera, data, forwardBuffer, opaqueEffects, all, ref isHDR);

            // Final post-processing
            if (finalEffects.Count > 0)
            {
                Graphics.SubmitCommandBuffer(buffer);
                CommandBufferPool.Release(buffer);
                buffer = null;

                DrawImageEffects(forwardBuffer, finalEffects, ref isHDR);

                // Blit Result to target, Since the previous commandBuffer has been submitted & released we need a new one
                // Fortunately Graphics.Blit will create and handle this for us
                Graphics.Blit(forwardBuffer, target);
            }
            else
            {
                // Blit Result to target
                buffer.Blit(forwardBuffer, target);
            }
        }
        finally
        {
            if (buffer != null)
            {
                Graphics.SubmitCommandBuffer(buffer);
                CommandBufferPool.Release(buffer);
            }
            RenderTexture.ReleaseTemporaryRT(forwardBuffer);

            foreach (MonoBehaviour effect in all)
                effect.OnPostRender();
        }
    }

    private static (List<MonoBehaviour> all, List<MonoBehaviour> opaque, List<MonoBehaviour> final) GatherImageEffects(Camera camera, bool isSceneView)
    {
        var opaqueEffects = new List<MonoBehaviour>();
        var finalEffects = new List<MonoBehaviour>();

        var components = camera.GetComponents<MonoBehaviour>();
        // If this is the Scene view camera, we need to include the Camera.Main effects
        if(isSceneView && Camera.Main != null)
            components = components.Concat(Camera.Main?.GetComponents<MonoBehaviour>() ?? Array.Empty<MonoBehaviour>());

        foreach (var effect in components)
        {
            if (effect.EnabledInHierarchy == false) continue;
            if (effect is Camera) continue;

            var type = effect.GetType();

            // if this is Scene view camera, then the effect needs the ImageEffectAllowedInSceneView attribute
            if (isSceneView && type.GetCustomAttributes(typeof(ImageEffectAllowedInSceneViewAttribute), false).Length == 0)
                continue;

            // If they have OnRenderImage does not effect if they exist as a valid effect
            all.Add(effect);

            var method = type.GetMethod("OnRenderImage");
            if (method?.DeclaringType == typeof(MonoBehaviour)) continue;


            if (type.GetCustomAttributes(typeof(ImageEffectOpaqueAttribute), false).Length > 0)
                opaqueEffects.Add(effect);
            else
                finalEffects.Add(effect);
        }

        return (all, opaqueEffects, finalEffects);
    }

    private static CommandBuffer PrepareCommandBuffer(Framebuffer target, Camera camera, bool isHDR, out RenderTexture forwardBuffer)
    {
        var buffer = CommandBufferPool.Get("Rendering Command Buffer");

        bool clearColor = camera.ClearFlags == CameraClearFlags.ColorOnly || camera.ClearFlags == CameraClearFlags.DepthColor;
        bool clearDepth = camera.ClearFlags == CameraClearFlags.DepthOnly || camera.ClearFlags == CameraClearFlags.DepthColor;
        bool drawSkybox = camera.ClearFlags == CameraClearFlags.Skybox;

        float renderScale = Math.Clamp(camera.RenderScale, 0.1f, 2.0f);
        uint scaledWidth = (uint)Math.Max(1, (int)(target.Width * renderScale));
        uint scaledHeight = (uint)Math.Max(1, (int)(target.Height * renderScale));

        forwardBuffer = RenderTexture.GetTemporaryRT(scaledWidth, scaledHeight, [isHDR ? PixelFormat.R16_G16_B16_A16_Float : PixelFormat.R8_G8_B8_A8_UNorm]);
        buffer.SetRenderTarget(forwardBuffer);
        buffer.ClearRenderTarget(clearDepth || drawSkybox, clearColor || drawSkybox, camera.ClearColor);

        // Set View Rect
        //buffer.SetViewports((int)(camera.Viewrect.x * target.Width), (int)(camera.Viewrect.y * target.Height), (int)(camera.Viewrect.width * target.Width), (int)(camera.Viewrect.height * target.Height), 0, 1000);

        return buffer;
    }

    #endregion

    #region Scene Rendering

    private static void RenderScene(CommandBuffer buffer, Camera camera, in RenderingData data, RenderTexture forwardBuffer, List<MonoBehaviour> effects, List<MonoBehaviour> all, ref bool isHDR)
    {
        var view = camera.GetViewMatrix(!CAMERA_RELATIVE);
        var projection = camera.GetProjectionMatrix(data.TargetResolution, true);
        var vp = view * projection;
        var worldFrustum = camera.GetFrustum(data.TargetResolution);

        SetupLightingAndShadows(buffer, camera, forwardBuffer, camera.Transform.position);

        // 1. Skybox (if enabled)
        if (camera.ClearFlags == CameraClearFlags.Skybox)
            RenderSkybox(buffer, camera, projection);
        foreach (MonoBehaviour effect in all)
            effect.OnPreCull();
        foreach (MonoBehaviour effect in all)
            effect.OnPreRender();

        // 2. Opaque geometry
        DrawRenderables("RenderOrder", "Opaque", buffer, camera.Transform.position, vp, view, projection, camera.CullingMask, worldFrustum);

        // 3. Apply opaque post-processing effects
        if (opaqueEffects.Count > 0)
        {
            Graphics.SubmitCommandBuffer(buffer);
            CommandBufferPool.Release(buffer);

            DrawImageEffects(forwardBuffer, opaqueEffects, ref isHDR);

            // Get new command buffer for remaining passes
            buffer = CommandBufferPool.Get("Rendering Command Buffer");
            buffer.SetRenderTarget(forwardBuffer);

        }

        // 4. Transparent geometry
        DrawRenderables("RenderOrder", "Transparent", buffer, camera.Transform.position, vp, view, projection, camera.CullingMask, worldFrustum);

        // 5. Debug visualization
        if (data.DisplayGrid)
            RenderGrid(buffer, camera, data, view, projection);

        if (data.DisplayGizmo)
            RenderGizmos(buffer, camera, vp);
    }

    private static void SetupLightingAndShadows(CommandBuffer buffer, Camera camera, RenderTexture forwardBuffer, Vector3 cameraPosition)
    {
        var lights = GetLights();
        var sunDirection = GetSunDirection(lights);

        PrepareShadowAtlas();
        CreateLightBuffer(buffer, camera, lights);

        buffer.SetRenderTarget(forwardBuffer);
        buffer.SetTexture("_ShadowAtlas", ShadowMap.DepthBuffer);
        buffer.SetBuffer("_Lights", LightBuffer);
        buffer.SetInt("_LightCount", LightCount);
        buffer.SetVector("_CameraWorldPos", cameraPosition);
        buffer.SetVector("_SunDir", sunDirection);
    }

    private static void PrepareShadowAtlas()
    {
        ShadowAtlas.TryInitialize();

        ShadowAtlas.Clear();
        CommandBuffer atlasClear = CommandBufferPool.Get("Shadow Atlas Clear");
        atlasClear.SetRenderTarget(ShadowAtlas.GetAtlas());
        atlasClear.ClearRenderTarget(true, false, Color.black);

        Graphics.SubmitCommandBuffer(atlasClear);
        CommandBufferPool.Release(atlasClear);
    }

    private static void CreateLightBuffer(CommandBuffer buffer, Camera cam, List<IRenderableLight> lights)
    {
        // We have AtlasWidth slots for shadow maps
        // a single shadow map can consume multiple slots if its larger then 128x128
        // We need to distribute these slots and resolutions out to lights
        // based on their distance from the camera
        int width = ShadowAtlas.GetAtlasWidth();

        List<GPULight> gpuLights = [];
        foreach (var light in lights)
        {
            // Calculate resolution based on distance
            int res = CalculateResolution(Vector3.Distance(cam.Transform.position, light.GetLightPosition())); // Directional lights are always 1024
            if (light is DirectionalLight dir)
                res = (int)dir.shadowResolution;

            if (light.DoCastShadows())
            {
                var gpu = light.GetGPULight(ShadowAtlas.GetSize(), CAMERA_RELATIVE, cam.Transform.position);

                // Find a slot for the shadow map
                var slot = ShadowAtlas.ReserveTiles(res, res, light.GetLightID());

                if (slot != null)
                {
                    gpu.AtlasX = slot.Value.x;
                    gpu.AtlasY = slot.Value.y;
                    gpu.AtlasWidth = res;

                    // Draw the shadow map
                    ShadowMap = ShadowAtlas.GetAtlas();

                    buffer.SetRenderTarget(ShadowMap);
                    buffer.SetViewports(slot.Value.x, slot.Value.y, res, res, 0, 1000);

                    light.GetShadowMatrix(out Matrix4x4 view, out Matrix4x4 proj);
                    BoundingFrustum frustum = new(view * proj);
                    if (CAMERA_RELATIVE)
                        view.Translation = Vector3.zero;
                    Matrix4x4 lightVP = view * proj;


                    DrawRenderables("LightMode", "ShadowCaster", buffer, light.GetLightPosition(), lightVP, view, proj, cam.CullingMask, frustum);

                    buffer.SetFullViewports();
                }
                else
                {
                    gpu.AtlasX = -1;
                    gpu.AtlasY = -1;
                    gpu.AtlasWidth = 0;
                }

                gpuLights.Add(gpu);
            }
            else
            {
                GPULight gpu = light.GetGPULight(0, CAMERA_RELATIVE, cam.Transform.position);
                gpu.AtlasX = -1;
                gpu.AtlasY = -1;
                gpu.AtlasWidth = 0;
                gpuLights.Add(gpu);
            }
        }


        unsafe
        {
            if (LightBuffer == null || gpuLights.Count > LightCount)
            {
                LightBuffer?.Dispose();
                LightBuffer = new((uint)gpuLights.Count, (uint)sizeof(GPULight), false);
            }

            if (gpuLights.Count > 0)
                LightBuffer.SetData<GPULight>(gpuLights.ToArray(), 0);
            //else Dont really need todo this since LightCount will be 0
            //    LightBuffer = GraphicsBuffer.Empty;

            LightCount = lights.Count;
        }
    }

    private static int CalculateResolution(double distance)
    {
        double t = MathD.Clamp(distance / 16f, 0, 1);
        var tileSize = ShadowAtlas.GetTileSize();
        int resolution = MathD.RoundToInt(MathD.Lerp(ShadowAtlas.GetMaxShadowSize(), tileSize, t));

        // Round to nearest multiple of tile size
        return MathD.Max(tileSize, (resolution / tileSize) * tileSize);
    }

    private static Vector3 GetSunDirection(List<IRenderableLight> lights)
    {
        if (lights.Count > 0 && lights[0] is IRenderableLight light && light.GetLightType() == LightType.Directional)
            return light.GetLightDirection();
        return Vector3.up;
    }

    private static void RenderSkybox(CommandBuffer buffer, Camera camera, Matrix4x4 projection)
    {
        buffer.SetMaterial(s_skybox);
        buffer.SetMatrix("_Matrix_VP", (camera.GetViewMatrix(false) * projection).ToFloat());
        buffer.DrawSingle(s_skyDome);
    }

    private static void RenderGizmos(CommandBuffer buffer, Camera camera, Matrix4x4 vp)
    {
        (Mesh? wire, Mesh? solid) = Debug.GetGizmoDrawData(CAMERA_RELATIVE, camera.Transform.position);

        if (wire != null || solid != null)
        {
            // The vertices have already been transformed by the gizmo system to be camera relative (if needed) so we just need to draw them
            buffer.SetMatrix("_Matrix_VP", vp.ToFloat());

            buffer.SetTexture("_MainTex", Texture2D.White.Res);
            buffer.SetMaterial(s_gizmo);
            if (wire != null) buffer.DrawSingle(wire);
            if (solid != null) buffer.DrawSingle(solid);
        }

        List<GizmoBuilder.IconDrawCall> icons = Debug.GetGizmoIcons();
        if (icons != null)
        {
            buffer.SetMaterial(s_gizmo);

            foreach (GizmoBuilder.IconDrawCall icon in icons)
            {
                Vector3 center = icon.center;
                if (CAMERA_RELATIVE)
                    center -= camera.Transform.position;
                Matrix4x4 billboard = Matrix4x4.CreateBillboard(center, Vector3.zero, camera.Transform.up, camera.Transform.forward);

                buffer.SetMatrix("_Matrix_VP", (billboard * vp).ToFloat());
                buffer.SetTexture("_MainTex", icon.texture);

                buffer.DrawSingle(s_quadMesh);
            }
        }
    }

    private static void RenderGrid(CommandBuffer buffer, Camera camera, RenderingData data, Matrix4x4 view, Matrix4x4 projection)
    {
        Matrix4x4 grid = Matrix4x4.CreateScale(GRID_SCALE);

        grid *= data.GridMatrix;

        if (CAMERA_RELATIVE)
            grid.Translation -= camera.Transform.position;

        Matrix4x4 MV = grid * view;
        Matrix4x4 MVP = grid * view * projection;

        buffer.SetMatrix("_Matrix_MV", MV.ToFloat());
        buffer.SetMatrix("_Matrix_MVP", MVP.ToFloat());

        buffer.SetColor("_GridColor", data.GridColor);
        buffer.SetFloat("_LineWidth", (float)data.GridSizes.z);
        buffer.SetFloat("_PrimaryGridSize", 1 / (float)data.GridSizes.x * GRID_SCALE * 2);
        buffer.SetFloat("_SecondaryGridSize", 1 / (float)data.GridSizes.y * GRID_SCALE * 2);
        buffer.SetFloat("_Falloff", 15.0f);
        buffer.SetFloat("_MaxDist", Math.Min(camera.FarClip, GRID_SCALE));

        buffer.SetMaterial(s_gridMaterial, 0);
        buffer.DrawSingle(s_quadMesh);
    }

    #endregion

    private static void DrawImageEffects(RenderTexture forwardBuffer, List<MonoBehaviour> effects, ref bool isHDR)
    {
        // Create two buffers for ping-pong rendering
        RenderTexture sourceBuffer = forwardBuffer;
        // If the first effect transforms to LDR, we need to start in LDR otherwise we would waste a buffer
        bool firstEffectIsLDR = effects.Count > 0 && effects[0].GetType().GetCustomAttributes(typeof(ImageEffectTransformsToLDRAttribute), false).Length > 0;
        RenderTexture destBuffer = RenderTexture.GetTemporaryRT(forwardBuffer.Width, forwardBuffer.Height, [isHDR && !firstEffectIsLDR ? PixelFormat.R16_G16_B16_A16_Float : PixelFormat.R8_G8_B8_A8_UNorm]);
        if (firstEffectIsLDR)
            isHDR = false;

        try
        {
            foreach (MonoBehaviour effect in effects)
            {
                // If the effect has ImageEffectTransformsToLDR and we're currently in HDR, we need to switch to LDR
                if (isHDR && effect.GetType().GetCustomAttributes(typeof(ImageEffectTransformsToLDRAttribute), false).Length > 0)
                {
                    isHDR = false;
                    RenderTexture.ReleaseTemporaryRT(destBuffer);
                    destBuffer = RenderTexture.GetTemporaryRT(forwardBuffer.Width, forwardBuffer.Height, [PixelFormat.R8_G8_B8_A8_UNorm]);
                }

                // Apply the effect
                effect.OnRenderImage(sourceBuffer, destBuffer);

                // Swap buffers for next iteration
                (sourceBuffer, destBuffer) = (destBuffer, sourceBuffer);
            }

            // After all effects, sourceBuffer contains the final result
            // If it's not the original forwardBuffer, we need to copy and clean up
            if (sourceBuffer != forwardBuffer)
            {
                Graphics.Blit(sourceBuffer, forwardBuffer);
                RenderTexture.ReleaseTemporaryRT(sourceBuffer);
            }
            else
            {
                // If sourceBuffer is forwardBuffer, we just need to release destBuffer
                RenderTexture.ReleaseTemporaryRT(destBuffer);
            }
        }
        catch (Exception e)
        {
            // Ensure we clean up resources even if an effect throws an exception
            if (sourceBuffer != forwardBuffer)
                RenderTexture.ReleaseTemporaryRT(sourceBuffer);
            RenderTexture.ReleaseTemporaryRT(destBuffer);
            throw; // Re-throw the exception after cleanup
        }
    }

    private static void DrawRenderables(string tag, string tagValue, CommandBuffer buffer, Vector3 cameraPosition, Matrix4x4 vp, Matrix4x4 view, Matrix4x4 proj, LayerMask cullingMask, BoundingFrustum? worldFrustum = null)
    {
        bool hasRenderOrder = !string.IsNullOrWhiteSpace(tag);

        foreach (RenderBatch batch in EnumerateBatches())
        {
            if (batch.material.Shader.IsAvailable == false) continue;

            foreach (ShaderPass pass in batch.material.Shader.Res.Passes)
            {
                // Skip this pass if it doesn't have the expected tag
                if (hasRenderOrder && !pass.HasTag(tag, tagValue))
                    continue;

                //buffer.SetMaterial(batch.material, pass); Below is the same as this but lets us set by pass instead of pass index
                buffer.ApplyPropertyState(batch.material._properties);
                buffer.SetPass(pass);
                buffer.BindResources();

                foreach (int renderIndex in batch.renderIndices)
                {
                    IRenderable renderable = GetRenderable(renderIndex);

                    if (worldFrustum != null && CullRenderable(renderable, worldFrustum))
                        continue;

                    if (cullingMask.HasLayer(renderable.GetLayer()) == false)
                        continue;

                    renderable.GetRenderingData(out PropertyState properties, out IGeometryDrawData drawData, out Matrix4x4 model);

                    if (CAMERA_RELATIVE)
                        model.Translation -= cameraPosition;

                    // model = Graphics.GetGPUModelMatrix(model);

                    buffer.ApplyPropertyState(properties);

                    buffer.SetMatrix("Mat_V", view.ToFloat());
                    buffer.SetMatrix("Mat_P", proj.ToFloat());
                    buffer.SetMatrix("Mat_ObjectToWorld", model.ToFloat());
                    buffer.SetMatrix("Mat_WorldToObject", model.Invert().ToFloat());
                    buffer.SetMatrix("Mat_MVP", (model * vp).ToFloat());

                    buffer.SetColor("_MainColor", Color.white);

                    buffer.UpdateBuffer("_PerDraw");


                    buffer.SetDrawData(drawData);
                    buffer.DrawIndexed((uint)drawData.IndexCount, 0, 1, 0, 0);
                }
            }
        }
    }

    private static bool CullRenderable(IRenderable renderable, BoundingFrustum cameraFrustum)
    {
        renderable.GetCullingData(out bool isRenderable, out Bounds bounds);

        return !isRenderable || !cameraFrustum.Intersects(bounds);
    }
}
