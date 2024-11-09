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

        foreach (int key in unusedKeys)
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
            buffer.SetMatrix("prowl_PrevObjectToWorld", prevModel.ToFloat());
        else
            buffer.SetMatrix("prowl_PrevObjectToWorld", currentModel.ToFloat()); // First frame, use current matrix

        s_prevModelMatrices[objectId] = currentModel;
    }

    #endregion

    #region Main Rendering

    public override void Render(Camera camera, in RenderingData data)
    {
        ValidateDefaults();

        Framebuffer target = camera.UpdateRenderData();

        bool isHDR = data.IsSceneViewCamera ?
            (Camera.Main?.HDR ?? camera.HDR) :
            camera.HDR;

        (List<MonoBehaviour> all, List<MonoBehaviour> opaqueEffects, List<MonoBehaviour> finalEffects) = GatherImageEffects(camera, data.IsSceneViewCamera);
        CommandBuffer? buffer = PrepareCommandBuffer(target, camera, isHDR, out RenderTexture? forwardBuffer);
        List<RenderTexture> toRelease = [];

        try
        {
            // Main rendering with correct order of operations
            RenderScene(buffer, camera, data, forwardBuffer, opaqueEffects, all, ref isHDR, toRelease);

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

            foreach (RenderTexture rt in toRelease)
                RenderTexture.ReleaseTemporaryRT(rt);

            // Clear global data, As some values like _CameraDepthTexture and _CameraMotionVectorsTexture are now invalid
            PropertyState.ClearGlobalData();

            foreach (MonoBehaviour effect in all)
                effect.OnPostRender(camera);
        }
    }

    private static (List<MonoBehaviour> all, List<MonoBehaviour> opaque, List<MonoBehaviour> final) GatherImageEffects(Camera camera, bool isSceneView)
    {
        var all = new List<MonoBehaviour>();
        var opaqueEffects = new List<MonoBehaviour>();
        var finalEffects = new List<MonoBehaviour>();

        IEnumerable<MonoBehaviour> components = camera.GetComponents<MonoBehaviour>();
        // If this is the Scene view camera, we need to include the Camera.Main effects
        if(isSceneView && Camera.Main != null)
            components = components.Concat(Camera.Main?.GetComponents<MonoBehaviour>() ?? Array.Empty<MonoBehaviour>());

        foreach (MonoBehaviour effect in components)
        {
            if (effect.EnabledInHierarchy == false) continue;
            if (effect is Camera) continue;

            Type type = effect.GetType();

            // if this is Scene view camera, then the effect needs the ImageEffectAllowedInSceneView attribute
            if (isSceneView && type.GetCustomAttributes(typeof(ImageEffectAllowedInSceneViewAttribute), false).Length == 0)
                continue;

            // If they have OnRenderImage does not effect if they exist as a valid effect
            all.Add(effect);

            MethodInfo? method = type.GetMethod("OnRenderImage");
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

        bool clearColor = camera.ClearFlags == CameraClearFlags.ColorOnly || camera.ClearFlags == CameraClearFlags.DepthColor;
        bool clearDepth = camera.ClearFlags == CameraClearFlags.DepthOnly || camera.ClearFlags == CameraClearFlags.DepthColor;
        bool drawSkybox = camera.ClearFlags == CameraClearFlags.Skybox;

        forwardBuffer = RenderTexture.GetTemporaryRT(camera.PixelWidth, camera.PixelHeight, [isHDR ? PixelFormat.R16_G16_B16_A16_Float : PixelFormat.R8_G8_B8_A8_UNorm]);
        CommandBuffer buffer = CommandBufferPool.Get("Rendering Command Buffer");
        buffer.SetRenderTarget(forwardBuffer);
        buffer.ClearRenderTarget(clearDepth || drawSkybox, clearColor || drawSkybox, camera.ClearColor);

        return buffer;
    }

    private static void SetupGlobalUniforms(CameraSnapshot css)
    {
        // Set View Rect
        //buffer.SetViewports((int)(camera.Viewrect.x * target.Width), (int)(camera.Viewrect.y * target.Height), (int)(camera.Viewrect.width * target.Width), (int)(camera.Viewrect.height * target.Height), 0, 1000);

        // Setup Default Uniforms for this frame
        // Camera
        PropertyState.SetGlobalVector("_WorldSpaceCameraPos", css.cameraPosition);
        bool flippedy = !Graphics.IsOpenGL && !Graphics.IsVulkan;
        PropertyState.SetGlobalVector("_ProjectionParams", new Vector4(flippedy ? -1.0 : 1.0f, css.nearClipPlane, css.farClipPlane, 1.0f / css.farClipPlane));
        PropertyState.SetGlobalVector("_ScreenParams", new Vector4(css.pixelWidth, css.pixelHeight, 1.0f + 1.0f / css.pixelWidth, 1.0f + 1.0f / css.pixelHeight));

        // Its a waste to set these here, since Lighting can overwrite them immediately after anyway
        //SetGlobalCameraMatrices(css.view, css.projection);

        // Time
        PropertyState.SetGlobalVector("_Time", new Vector4(Time.time / 20, Time.time, Time.time * 2, Time.time * 3));
        PropertyState.SetGlobalVector("_SinTime", new Vector4(Math.Sin(Time.time / 8), Math.Sin(Time.time / 4), Math.Sin(Time.time / 2), Math.Sin(Time.time)));
        PropertyState.SetGlobalVector("_CosTime", new Vector4(Math.Cos(Time.time / 8), Math.Cos(Time.time / 4), Math.Cos(Time.time / 2), Math.Cos(Time.time)));
        PropertyState.SetGlobalVector("prowl_DeltaTime", new Vector4(Time.deltaTime, 1.0f / Time.deltaTime, Time.smoothDeltaTime, 1.0f / Time.smoothDeltaTime));
    }

    #endregion

    #region Scene Rendering

    public struct CameraSnapshot(Camera camera)
    {
        public Vector3 cameraPosition = camera.Transform.position;
        public Vector3 cameraUp = camera.Transform.up;
        public Vector3 cameraForward = camera.Transform.forward;
        public LayerMask cullingMask = camera.CullingMask;
        public CameraClearFlags clearFlags = camera.ClearFlags;
        public float nearClipPlane = camera.NearClipPlane;
        public float farClipPlane = camera.FarClipPlane;
        public uint pixelWidth = camera.PixelWidth;
        public uint pixelHeight = camera.PixelHeight;
        public float aspect = camera.Aspect;
        public Matrix4x4 originView = camera.OriginViewMatrix;
        public Matrix4x4 view = CAMERA_RELATIVE ? camera.OriginViewMatrix : camera.ViewMatrix;
        public Matrix4x4 viewInverse = (CAMERA_RELATIVE ? camera.OriginViewMatrix : camera.ViewMatrix).Invert();
        public Matrix4x4 projection = Graphics.GetGPUProjectionMatrix(camera.ProjectionMatrix);
        public Matrix4x4 transparentProjection = camera.UseJitteredProjectionMatrixForTransparentRendering ? camera.ProjectionMatrix : camera.NonJitteredProjectionMatrix;
        public Matrix4x4 previousViewProj = camera.PreviousViewProjectionMatrix;
        public BoundingFrustum worldFrustum = new(camera.ViewMatrix * camera.ProjectionMatrix);
        public DepthTextureMode depthTextureMode = camera.DepthTextureMode; // Flags, Can be None, Normals, MotionVectors
    }

    private static void RenderScene(CommandBuffer buffer, Camera camera, in RenderingData data, RenderTexture forwardBuffer, List<MonoBehaviour> effects, List<MonoBehaviour> all, ref bool isHDR, List<RenderTexture> toRelease)
    {
        // 1. Pre Cull
        foreach (MonoBehaviour effect in all)
            effect.OnPreCull(camera);

        // 2. Take a snapshot of all Camera data
        CameraSnapshot css = new(camera);
        SetupGlobalUniforms(css);

        // 3. Cull Renderables based on Snapshot data
        HashSet<int> culledRenderableIndices = CullRenderables(css.cullingMask, css.worldFrustum);

        // 4. Pre Render
        foreach (MonoBehaviour effect in all)
            effect.OnPreRender(camera);

        // 5. Setup Lighting and Shadows
        SetupLightingAndShadows(buffer, forwardBuffer, css);
        // Setting up shadows sets the camera matrices, so we set them here instead of earlier
        SetGlobalCameraMatrices(css.view, css.projection);

        // 6. Pre-Depth Pass
        PreDepthPass(buffer, forwardBuffer, toRelease, css, culledRenderableIndices);

        // 7. Opaque geometry
        DrawRenderables("RenderOrder", "Opaque", buffer, css.cameraPosition, culledRenderableIndices, false);

        // 7.1. Create motion vector buffer if requested by the camera
        RenderTexture? motionVectorBuffer = null;
        if (css.depthTextureMode.HasFlag(DepthTextureMode.MotionVectors))
        {
            motionVectorBuffer = RenderTexture.GetTemporaryRT(css.pixelWidth, css.pixelHeight, [PixelFormat.R16_G16_Float]);
            toRelease.Add(motionVectorBuffer);
            buffer.SetRenderTarget(motionVectorBuffer);
            buffer.ClearRenderTarget(true, true, new Color(0, 0, 0, 0));

            // Set matrices for motion vector calculation
            buffer.SetMatrix("prowl_PrevViewProj", camera.PreviousViewProjectionMatrix.ToFloat());

            // Draw motion vectors for all visible objects
            DrawRenderables("LightMode", "MotionVectors", buffer, css.cameraPosition, culledRenderableIndices, true);

            // Set the motion vector texture for use in post-processing
            PropertyState.SetGlobalTexture("_CameraMotionVectorsTexture", motionVectorBuffer);

            // Reset render target back to forward buffer
            buffer.SetRenderTarget(forwardBuffer);
        }

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

        // 8. Debug visualization
        if (data.DisplayGrid)
            RenderGrid(buffer, css, data);

        if (data.DisplayGizmo)
            RenderGizmos(buffer, css);

        // 6. Skybox (if enabled)
        if (css.clearFlags == CameraClearFlags.Skybox)
            RenderSkybox(buffer, css);

        // 10. Transparent geometry
        // Setup to use transparent projection matrix if its differant
        if (css.projection != css.transparentProjection)
            SetGlobalCameraMatrices(css.view, css.transparentProjection);
        DrawRenderables("RenderOrder", "Transparent", buffer, css.cameraPosition, culledRenderableIndices, false);
    }

    private static void PreDepthPass(CommandBuffer buffer, RenderTexture forwardBuffer, List<RenderTexture> toRelease, CameraSnapshot css, HashSet<int> culledRenderableIndices)
    {
        // We draw objects to get the DepthBuffer but we also draw it into a ColorBuffer so we upload it as a Sampleable Texture
        RenderTexture depthTexture = RenderTexture.GetTemporaryRT(css.pixelWidth, css.pixelHeight, [PixelFormat.R32_Float]);
        toRelease.Add(depthTexture);
        buffer.SetRenderTarget(depthTexture);
        buffer.ClearRenderTarget(true, true, new Color(0, 0, 0, 0));

        // Draw depth for all visible objects
        DrawRenderables("LightMode", "ShadowCaster", buffer, css.cameraPosition, culledRenderableIndices, false);

        // Set the depth texture for use in post-processing
        PropertyState.SetGlobalTexture("_CameraDepthTexture", depthTexture);

        // Copy the depth buffer to the forward buffer
        buffer.CopyTexture(depthTexture.DepthBuffer, forwardBuffer.DepthBuffer);

        // Reset render target back to forward buffer
        buffer.SetRenderTarget(forwardBuffer);
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

    private static void SetupLightingAndShadows(CommandBuffer buffer, RenderTexture forwardBuffer, CameraSnapshot css)
    {
        List<IRenderableLight> lights = GetLights();
        Vector3 sunDirection = GetSunDirection(lights);

        PrepareShadowAtlas();
        CreateLightBuffer(buffer, css.cameraPosition, css.cullingMask, lights);

        buffer.SetRenderTarget(forwardBuffer);
        buffer.SetTexture("_ShadowAtlas", ShadowMap.ColorBuffers[0]);
        buffer.SetBuffer("_Lights", LightBuffer);
        buffer.SetInt("_LightCount", LightCount);
        buffer.SetVector("_CameraWorldPos", css.cameraPosition);
        buffer.SetVector("_SunDir", sunDirection);
        buffer.SetVector("prowl_ShadowAtlasSize", new Vector2(ShadowAtlas.GetSize(), ShadowAtlas.GetSize()));
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
        foreach (IRenderableLight light in lights)
        {
            // Calculate resolution based on distance
            int res = CalculateResolution(Vector3.Distance(cameraPosition, light.GetLightPosition())); // Directional lights are always 1024
            if (light is DirectionalLight dir)
                res = (int)dir.shadowResolution;

            if (light.DoCastShadows())
            {
                GPULight gpu = light.GetGPULight(ShadowAtlas.GetSize(), CAMERA_RELATIVE, cameraPosition);

                // Find a slot for the shadow map
                Vector2Int? slot = ShadowAtlas.ReserveTiles(res, res, light.GetLightID());

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
                    SetGlobalCameraMatrices(view, proj);
                    DrawRenderables("LightMode", "ShadowCaster", buffer, light.GetLightPosition(), null, false);

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

    private static void SetGlobalCameraMatrices(Matrix4x4 view, Matrix4x4 proj)
    {
        PropertyState.SetGlobalMatrix("prowl_MatV", view.ToFloat());
        PropertyState.SetGlobalMatrix("prowl_MatIV", view.Invert().ToFloat());
        PropertyState.SetGlobalMatrix("prowl_MatP", proj.ToFloat());
        PropertyState.SetGlobalMatrix("prowl_MatVP", (view * proj).ToFloat());
    }

    private static int CalculateResolution(double distance)
    {
        double t = MathD.Clamp(distance / 16f, 0, 1);
        int tileSize = ShadowAtlas.GetTileSize();
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

    private static void RenderSkybox(CommandBuffer buffer, CameraSnapshot css)
    {
        buffer.SetMaterial(s_skybox);
        buffer.SetMatrix("_Matrix_VP", (css.originView * css.projection).ToFloat());
        buffer.DrawSingle(s_skyDome);
    }

    private static void RenderGizmos(CommandBuffer buffer, CameraSnapshot css)
    {
        Matrix4x4 vp = (css.view * css.projection);
        (Mesh? wire, Mesh? solid) = Debug.GetGizmoDrawData(CAMERA_RELATIVE, css.cameraPosition);

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
                    center -= css.cameraPosition;
                Matrix4x4 billboard = Matrix4x4.CreateBillboard(center, Vector3.zero, css.cameraUp, css.cameraForward);

                buffer.SetMatrix("_Matrix_VP", (billboard * vp).ToFloat());
                buffer.SetTexture("_MainTex", icon.texture);

                buffer.DrawSingle(s_quadMesh);
            }
        }
    }

    private static void RenderGrid(CommandBuffer buffer, CameraSnapshot css, RenderingData data)
    {
        Matrix4x4 grid = Matrix4x4.CreateScale(GRID_SCALE);

        grid *= data.GridMatrix;

        if (CAMERA_RELATIVE)
            grid.Translation -= css.cameraPosition;

        buffer.SetMatrix("prowl_ObjectToWorld", grid.ToFloat());
        buffer.UpdateBuffer("_PerDraw");

        buffer.SetColor("_GridColor", data.GridColor);
        buffer.SetFloat("_LineWidth", (float)data.GridSizes.z);
        buffer.SetFloat("_PrimaryGridSize", 1 / (float)data.GridSizes.x * GRID_SCALE * 2);
        buffer.SetFloat("_SecondaryGridSize", 1 / (float)data.GridSizes.y * GRID_SCALE * 2);
        buffer.SetFloat("_Falloff", 15.0f);
        buffer.SetFloat("_MaxDist", Math.Min(css.farClipPlane, GRID_SCALE));

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

    private static void DrawRenderables(string tag, string tagValue, CommandBuffer buffer, Vector3 cameraPosition, HashSet<int> culledRenderableIndices, bool updatePreviousMatrices)
    {
        bool hasRenderOrder = !string.IsNullOrWhiteSpace(tag);

        foreach (RenderBatch batch in EnumerateBatches())
        {
            if (batch.material.Shader.IsAvailable == false) continue;

            buffer.ApplyPropertyState(batch.material._properties);
            foreach (ShaderPass pass in batch.material.Shader.Res.Passes)
            {
                // Skip this pass if it doesn't have the expected tag
                if (hasRenderOrder && !pass.HasTag(tag, tagValue))
                    continue;

                //buffer.SetMaterial(batch.material, pass); Below is the same as this but lets us set by pass instead of pass index
                buffer.SetPass(pass);
                buffer.BindResources();

                foreach (int renderIndex in batch.renderIndices)
                {
                    if (culledRenderableIndices?.Contains(renderIndex) ?? false)
                        continue;

                    IRenderable renderable = GetRenderable(renderIndex);

                    renderable.GetRenderingData(out PropertyState properties, out IGeometryDrawData drawData, out Matrix4x4 model);

                    // Store previous model matrix mainly for motion vectors, however, the user can use it for other things
                    if (updatePreviousMatrices && properties.TryGetInt("_ObjectID", out int instanceId))
                    {
                        TrackModelMatrix(buffer, instanceId, model);
                    }

                    if (CAMERA_RELATIVE)
                        model.Translation -= cameraPosition;

                    buffer.ApplyPropertyState(properties);

                    buffer.SetMatrix("prowl_ObjectToWorld", model.ToFloat());
                    buffer.SetMatrix("prowl_WorldToObject", model.Invert().ToFloat());

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
