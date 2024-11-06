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

    private static Matrix4x4 s_prevViewProjMatrix;
    private static Dictionary<int, Matrix4x4> s_prevModelMatrices = new();
    private static HashSet<int> s_activeObjectIds = new();
    private const int CLEANUP_INTERVAL_FRAMES = 120; // Clean up every 120 frames
    private static int s_framesSinceLastCleanup = 0;

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

    private static void CleanupUnusedModelMatrices()
    {
        // Increment frame counter
        s_framesSinceLastCleanup++;

        // Only perform cleanup at specified interval
        if (s_framesSinceLastCleanup < CLEANUP_INTERVAL_FRAMES)
            return;

        s_framesSinceLastCleanup = 0;

        // Remove all matrices that weren't used in this frame
        var unusedKeys = s_prevModelMatrices.Keys
            .Where(key => !s_activeObjectIds.Contains(key))
            .ToList();

        foreach (var key in unusedKeys)
            s_prevModelMatrices.Remove(key);

        // Clear the active IDs set for next frame
        s_activeObjectIds.Clear();
    }

    private static void TrackModelMatrix(CommandBuffer buffer, int objectId, Matrix4x4 currentModel)
    {
        // Mark this object ID as active this frame
        s_activeObjectIds.Add(objectId);

        // Store current model matrix for next frame
        if (s_prevModelMatrices.TryGetValue(objectId, out Matrix4x4 prevModel))
            buffer.SetMatrix("_PrevObjectToWorld", prevModel.ToFloat());
        else
            buffer.SetMatrix("_PrevObjectToWorld", currentModel.ToFloat()); // First frame, use current matrix

        s_prevModelMatrices[objectId] = currentModel;
    }

    #endregion

    #region Main Rendering

    public override void Render(Camera camera, in RenderingData data)
    {
        ValidateDefaults();

        var target = camera.UpdateRenderData();

        bool isHDR = data.IsSceneViewCamera ?
            (Camera.Main?.HDR ?? camera.HDR) :
            camera.HDR;

        var (all, opaqueEffects, finalEffects) = GatherImageEffects(camera, data.IsSceneViewCamera);
        var buffer = PrepareCommandBuffer(target, camera, isHDR, out RenderTexture? forwardBuffer);

        try
        {
            // Main rendering with correct order of operations
            RenderScene(buffer, camera, data, forwardBuffer, opaqueEffects, all, ref isHDR);

            // Clean up unused matrices after rendering
            CleanupUnusedModelMatrices();

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
                effect.OnPostRender(camera);
        }
    }

    private static (List<MonoBehaviour> all, List<MonoBehaviour> opaque, List<MonoBehaviour> final) GatherImageEffects(Camera camera, bool isSceneView)
    {
        var all = new List<MonoBehaviour>();
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

        forwardBuffer = RenderTexture.GetTemporaryRT(camera.PixelWidth, camera.PixelHeight, [isHDR ? PixelFormat.R16_G16_B16_A16_Float : PixelFormat.R8_G8_B8_A8_UNorm]);
        buffer.SetRenderTarget(forwardBuffer);
        buffer.ClearRenderTarget(clearDepth || drawSkybox, clearColor || drawSkybox, camera.ClearColor);

        // Set View Rect
        //buffer.SetViewports((int)(camera.Viewrect.x * target.Width), (int)(camera.Viewrect.y * target.Height), (int)(camera.Viewrect.width * target.Width), (int)(camera.Viewrect.height * target.Height), 0, 1000);

        // Setup Default Uniforms for this frame
        // Camera
        buffer.SetVector("_WorldSpaceCameraPos", camera.Transform.position);
        buffer.SetVector("_ProjectionParams", new Vector4(1.0f, camera.NearClipPlane, camera.FarClipPlane, 1.0f / camera.FarClipPlane));
        buffer.SetVector("_ScreenParams", new Vector4(camera.PixelWidth, camera.PixelHeight, 1.0f + 1.0f / camera.PixelWidth, 1.0f + 1.0f / camera.PixelHeight));
        // Time
        buffer.SetVector("_Time", new Vector4(Time.time / 20, Time.time, Time.time * 2, Time.time * 3));
        buffer.SetVector("_SinTime", new Vector4(Math.Sin(Time.time / 8), Math.Sin(Time.time / 4), Math.Sin(Time.time / 2), Math.Sin(Time.time)));
        buffer.SetVector("_CosTime", new Vector4(Math.Cos(Time.time / 8), Math.Cos(Time.time / 4), Math.Cos(Time.time / 2), Math.Cos(Time.time)));
        buffer.SetVector("prowl_DeltaTime", new Vector4(Time.deltaTime, 1.0f / Time.deltaTime, Time.smoothDeltaTime, 1.0f / Time.smoothDeltaTime));

        return buffer;
    }

    #endregion

    #region Scene Rendering

    private static void RenderScene(CommandBuffer buffer, Camera camera, in RenderingData data, RenderTexture forwardBuffer, List<MonoBehaviour> effects, List<MonoBehaviour> all, ref bool isHDR)
    {
        // 1. Pre Cull
        foreach (MonoBehaviour effect in all)
            effect.OnPreCull(camera);

        // 2. Take a snapshot of all Camera data
        var cameraPosition = camera.Transform.position;
        var cameraUp = camera.Transform.up;
        var cameraForward = camera.Transform.forward;
        var cullingMask = camera.CullingMask;
        var clearFlags = camera.ClearFlags;
        var farClipPlane = camera.FarClipPlane;
        var pixelWidth = camera.PixelWidth;
        var pixelHeight = camera.PixelHeight;
        var aspect = camera.Aspect;
        var originView = camera.OriginViewMatrix;
        var view = CAMERA_RELATIVE ? originView : camera.ViewMatrix;
        var projection = Graphics.GetGPUProjectionMatrix(camera.ProjectionMatrix);
        var transparentProjection = camera.UseJitteredProjectionMatrixForTransparentRendering ? camera.ProjectionMatrix : camera.NonJitteredProjectionMatrix;
        var previousViewProj = camera.PreviousViewProjectionMatrix;
        var worldFrustum = new BoundingFrustum(camera.ViewMatrix * camera.ProjectionMatrix);
        var depthTextureMode = camera.DepthTextureMode; // Flags, Can be None, Depth, Normals, MotionVectors
        Texture2D? depthTexture = null;

        // 3. Cull Renderables based on Snapshot data
        HashSet<int> culledRenderableIndices = CullRenderables(cullingMask, worldFrustum);

        // 4. Pre Render
        foreach (MonoBehaviour effect in all)
            effect.OnPreRender(camera);

        // 5. Setup Lighting and Shadows
        SetupLightingAndShadows(buffer, forwardBuffer, cameraPosition, cullingMask);

        // 6. Skybox (if enabled) - TODO: Should be done after opaque and after Opaque Post-Processing
        if (clearFlags == CameraClearFlags.Skybox)
            RenderSkybox(buffer, originView, projection);

        // 7. Opaque geometry
        DrawRenderables("RenderOrder", "Opaque", buffer, cameraPosition, view, projection, culledRenderableIndices, false);

        // 7.1. If the camera has depth texture mode enabled, we need to copy the depth texture
        // TODO: Unity re-draws the world to create Depth (also for Normals) textures.
        // Is it for platform-related reasons? Do some platforms not support sampling Depth formats?
        if (depthTextureMode.HasFlag(DepthTextureMode.Depth))
        {
            depthTexture = new Texture2D(pixelWidth, pixelHeight, 1, forwardBuffer.DepthBuffer.Format);
            buffer.CopyTexture(forwardBuffer.DepthBuffer, depthTexture);
            buffer.SetTexture("_CameraDepthTexture", depthTexture);
        }

        // 7.2 Create motion vector buffer if requested by the camera
        RenderTexture? motionVectorBuffer = null;
        if (depthTextureMode.HasFlag(DepthTextureMode.MotionVectors))
        {
            motionVectorBuffer = RenderTexture.GetTemporaryRT(pixelWidth, pixelHeight, [PixelFormat.R16_G16_Float]);
            buffer.SetRenderTarget(motionVectorBuffer);
            buffer.ClearRenderTarget(true, true, new Color(0, 0, 0, 0));

            // Set matrices for motion vector calculation
            buffer.SetMatrix("_PrevViewProj", camera.PreviousViewProjectionMatrix.ToFloat());

            // Draw motion vectors for all visible objects
            DrawRenderables("LightMode", "MotionVectors", buffer, cameraPosition, view, projection, culledRenderableIndices, true);

            // Set the motion vector texture for use in post-processing
            buffer.SetTexture("_CameraMotionVectorsTexture", motionVectorBuffer);

            // Reset render target back to forward buffer
            buffer.SetRenderTarget(forwardBuffer);
        }

        // Reset render target back to forward buffer
        buffer.SetRenderTarget(forwardBuffer);

        // 8. Debug visualization
        if (data.DisplayGrid)
            RenderGrid(buffer, cameraPosition, farClipPlane, data, view, projection);

        if (data.DisplayGizmo)
            RenderGizmos(buffer, cameraPosition, cameraUp, cameraForward, view * projection);

        // 9. Apply opaque post-processing effects
        if (effects.Count > 0)
        {
            Graphics.SubmitCommandBuffer(buffer);
            CommandBufferPool.Release(buffer);

            DrawImageEffects(forwardBuffer, effects, ref isHDR);

            // Get new command buffer for remaining passes
            buffer = CommandBufferPool.Get("Rendering Command Buffer");
            buffer.SetRenderTarget(forwardBuffer);

        }

        // 10. Transparent geometry
        DrawRenderables("RenderOrder", "Transparent", buffer, cameraPosition, view, transparentProjection, culledRenderableIndices, false);

        // Clean up depth texture Buffer
        if (depthTexture != null)
            depthTexture.DestroyLater();

        // Clean up motion vector buffer
        if (motionVectorBuffer != null)
            RenderTexture.ReleaseTemporaryRT(motionVectorBuffer);
    }

    private static HashSet<int> CullRenderables(LayerMask cullingMask, BoundingFrustum? worldFrustum)
    {
        HashSet<int> culledRenderableIndices = [];
        foreach (RenderBatch batch in EnumerateBatches())
        {
            if (batch.material.Shader.IsAvailable == false)
            {
                culledRenderableIndices.UnionWith(batch.renderIndices);
                continue;
            }

            foreach (int renderIndex in batch.renderIndices)
            {
                IRenderable renderable = GetRenderable(renderIndex);

                if (worldFrustum != null && CullRenderable(renderable, worldFrustum))
                {
                    culledRenderableIndices.Add(renderIndex);
                    continue;
                }

                if (cullingMask.HasLayer(renderable.GetLayer()) == false)
                {
                    culledRenderableIndices.Add(renderIndex);
                    continue;
                }
            }
        }
        return culledRenderableIndices;
    }

    private static void SetupLightingAndShadows(CommandBuffer buffer, RenderTexture forwardBuffer, Vector3 cameraPosition, LayerMask cullingMask)
    {
        var lights = GetLights();
        var sunDirection = GetSunDirection(lights);

        PrepareShadowAtlas();
        CreateLightBuffer(buffer, cameraPosition, cullingMask, lights);

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

    private static void CreateLightBuffer(CommandBuffer buffer, Vector3 cameraPosition, LayerMask cullingMask, List<IRenderableLight> lights)
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
            int res = CalculateResolution(Vector3.Distance(cameraPosition, light.GetLightPosition())); // Directional lights are always 1024
            if (light is DirectionalLight dir)
                res = (int)dir.shadowResolution;

            if (light.DoCastShadows())
            {
                var gpu = light.GetGPULight(ShadowAtlas.GetSize(), CAMERA_RELATIVE, cameraPosition);

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

                    HashSet<int> culledRenderableIndices = CullRenderables(cullingMask, frustum);
                    DrawRenderables("LightMode", "ShadowCaster", buffer, light.GetLightPosition(), view, proj, culledRenderableIndices, false);

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
                GPULight gpu = light.GetGPULight(0, CAMERA_RELATIVE, cameraPosition);
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

    private static void RenderSkybox(CommandBuffer buffer, Matrix4x4 originViewMatrix, Matrix4x4 projection)
    {
        buffer.SetMaterial(s_skybox);
        buffer.SetMatrix("_Matrix_VP", (originViewMatrix * projection).ToFloat());
        buffer.DrawSingle(s_skyDome);
    }

    private static void RenderGizmos(CommandBuffer buffer, Vector3 cameraPosition, Vector3 cameraUp, Vector3 cameraForward, Matrix4x4 vp)
    {
        (Mesh? wire, Mesh? solid) = Debug.GetGizmoDrawData(CAMERA_RELATIVE, cameraPosition);

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
                    center -= cameraPosition;
                Matrix4x4 billboard = Matrix4x4.CreateBillboard(center, Vector3.zero, cameraUp, cameraForward);

                buffer.SetMatrix("_Matrix_VP", (billboard * vp).ToFloat());
                buffer.SetTexture("_MainTex", icon.texture);

                buffer.DrawSingle(s_quadMesh);
            }
        }
    }

    private static void RenderGrid(CommandBuffer buffer, Vector3 cameraPosition, float farClipPlane, RenderingData data, Matrix4x4 view, Matrix4x4 projection)
    {
        Matrix4x4 grid = Matrix4x4.CreateScale(GRID_SCALE);

        grid *= data.GridMatrix;

        if (CAMERA_RELATIVE)
            grid.Translation -= cameraPosition;

        Matrix4x4 MV = grid * view;
        Matrix4x4 MVP = grid * view * projection;

        buffer.SetMatrix("_Matrix_MV", MV.ToFloat());
        buffer.SetMatrix("_Matrix_MVP", MVP.ToFloat());

        buffer.SetColor("_GridColor", data.GridColor);
        buffer.SetFloat("_LineWidth", (float)data.GridSizes.z);
        buffer.SetFloat("_PrimaryGridSize", 1 / (float)data.GridSizes.x * GRID_SCALE * 2);
        buffer.SetFloat("_SecondaryGridSize", 1 / (float)data.GridSizes.y * GRID_SCALE * 2);
        buffer.SetFloat("_Falloff", 15.0f);
        buffer.SetFloat("_MaxDist", Math.Min(farClipPlane, GRID_SCALE));

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

    private static void DrawRenderables(string tag, string tagValue, CommandBuffer buffer, Vector3 cameraPosition, Matrix4x4 view, Matrix4x4 proj, HashSet<int> culledRenderableIndices, bool updatePreviousMatrices)
    {
        bool hasRenderOrder = !string.IsNullOrWhiteSpace(tag);
        Matrix4x4 viewProj = (view * proj);

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
                    if (culledRenderableIndices.Contains(renderIndex))
                        continue;

                    IRenderable renderable = GetRenderable(renderIndex);

                    renderable.GetRenderingData(out PropertyState properties, out IGeometryDrawData drawData, out Matrix4x4 model);

                    // Store previous model matrix mainly for motion vectors, however, the user can use it for other things
                    if(updatePreviousMatrices && properties.TryGetInt("_ObjectID", out int instanceId))
                    {
                        TrackModelMatrix(buffer, instanceId, model);
                    }

                    if (CAMERA_RELATIVE)
                        model.Translation -= cameraPosition;

                    // model = Graphics.GetGPUModelMatrix(model);

                    buffer.ApplyPropertyState(properties);

                    buffer.SetMatrix("Mat_V", view.ToFloat());
                    buffer.SetMatrix("Mat_P", proj.ToFloat());
                    buffer.SetMatrix("Mat_ObjectToWorld", model.ToFloat());
                    buffer.SetMatrix("Mat_WorldToObject", model.Invert().ToFloat());
                    buffer.SetMatrix("Mat_MVP", (model * viewProj).ToFloat());

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
