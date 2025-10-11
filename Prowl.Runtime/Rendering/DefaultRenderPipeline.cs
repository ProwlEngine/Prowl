using Prowl.Runtime.Resources;
using Prowl.Runtime.GraphicsBackend;
using Prowl.Runtime.GraphicsBackend.Primitives;
using Prowl.Runtime.Rendering.Shaders;
using Material = Prowl.Runtime.Resources.Material;
using Mesh = Prowl.Runtime.Resources.Mesh;
using Shader = Prowl.Runtime.Resources.Shader;
using System.Collections.Generic;
using System;
using System.Linq;
using Prowl.Vector;
using Prowl.Vector.Geometry;

// Room for Optomizations:
// 1. Uniform Buffer for all Global shared data being rendered in a frame like Camera matrices, Time, etc

// TODO:
// 1. Image Effects need a Dispose method to clean up their resources, Camera needs to call it too

namespace Prowl.Runtime.Rendering
{
    public sealed class TonemapperEffect : ImageEffect
    {
        public override bool TransformsToLDR => true;

        public float Contrast = 1.1f;
        public float Saturation = 1.1f;

        Material mat;

        public override void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            mat ??= new Material(Shader.LoadDefault(DefaultShader.Tonemapper));
            mat.SetFloat("Contrast", Contrast);
            mat.SetFloat("Saturation", Saturation);
            Graphics.Blit(source, destination, mat, 0);
        }
    }

    public sealed class KawaseBloomEffect : ImageEffect
    {
        public float Intensity = 1.5f;
        public float Threshold = 0.8f;
        public int Iterations = 6;
        public float Spread = 1f;

        private Material bloomMaterial;
        private RenderTexture[] pingPongBuffers = new RenderTexture[2];

        public override void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            // Create material if it doesn't exist
            bloomMaterial ??= new Material(Shader.LoadDefault(DefaultShader.Bloom));

            int width = source.Width / 4;
            int height = source.Height / 4;

            // Create ping-pong buffers if they don't exist
            for (int i = 0; i < 2; i++)
            {
                if (pingPongBuffers[i] == null || pingPongBuffers[i].Width != width || pingPongBuffers[i].Height != height)
                {
                    pingPongBuffers[i]?.Destroy();
                    pingPongBuffers[i] = new RenderTexture(width, height, false, [destination.MainTexture.ImageFormat]);
                }
            }

            // 1. Extract bright areas (threshold pass)
            bloomMaterial.SetFloat("_Threshold", Threshold);
            Graphics.Blit(source, pingPongBuffers[0], bloomMaterial, 0);

            // 2. Apply Kawase blur ping-pong (multiple iterations with increasing radius)
            int current = 0;
            int next = 1;

            for (int i = 0; i < Iterations; i++)
            {
                float offset = (i * 0.5f + 0.5f) * Spread;
                bloomMaterial.SetFloat("_Offset", offset);
                Graphics.Blit(pingPongBuffers[current], pingPongBuffers[next], bloomMaterial, 1);

                // Swap buffers
                int temp = current;
                current = next;
                next = temp;
            }

            // 3. Composite the bloom with the original image
            bloomMaterial.SetTexture("_BloomTex", pingPongBuffers[current].MainTexture);
            bloomMaterial.SetFloat("_Intensity", Intensity);
            Graphics.Blit(source, destination, bloomMaterial, 2);
        }

        public override void OnPostRender(Camera camera)
        {
            // Clean up resources if needed
        }
    }

    public sealed class BokehDepthOfFieldEffect : ImageEffect
    {
        public bool UseAutoFocus = true;
        public float ManualFocusPoint = 0.5f;
        public float FocusStrength = 200.0f;

        //[Range(5.0f, 40.0f)]
        public float BlurRadius = 5.0f;

        //[Range(0.1f, 0.9f)]
        public float Quality = 0.9f;

        //[Range(0.25f, 1.0f)]
        public float DownsampleFactor = 0.5f;

        private Material mat;
        private RenderTexture downsampledRT;

        public override void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            mat ??= new Material(Shader.LoadDefault(DefaultShader.BokehDoF));

            int width = (int)(source.Width * DownsampleFactor);
            int height = (int)(source.Height * DownsampleFactor);

            // Create or update downsampled render texture if needed
            if (downsampledRT == null || downsampledRT.Width != width || downsampledRT.Height != height)
            {
                if (downsampledRT != null)
                    downsampledRT.Destroy();

                downsampledRT = new RenderTexture(width, height, false, [source.MainTexture.ImageFormat]);
            }

            // Set shader properties
            mat.SetFloat("_BlurRadius", BlurRadius);
            mat.SetFloat("_FocusStrength", FocusStrength);
            mat.SetFloat("_Quality", Quality);
            mat.SetFloat("_ManualFocusPoint", ManualFocusPoint);
            mat.SetKeyword("AUTOFOCUS", UseAutoFocus);
            mat.SetVector("_Resolution", new Double2(source.Width, source.Height));

            // Two-pass approach:

            // Pass 1: Apply DoF at reduced resolution
            mat.SetVector("_Resolution", new Double2(width, height));
            Graphics.Blit(source, downsampledRT, mat, 0); // DoFDownsample pass

            // Pass 2: Combine original image with blurred result
            mat.SetTexture("_MainTex", source.MainTexture);
            mat.SetTexture("_DownsampledDoF", downsampledRT.MainTexture);
            mat.SetVector("_Resolution", new Double2(source.Width, source.Height));
            Graphics.Blit(source, destination, mat, 1); // DoFCombine pass
        }
    }

    public sealed class ScreenSpaceReflectionEffect : ImageEffect
    {
        public int RayStepCount = 16;
        public float ScreenEdgeFade = 0.1f;

        Material mat;

        public override void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            mat ??= new Material(Shader.LoadDefault(DefaultShader.SSR));
            
            // Set uniforms
            mat.SetInt("_RayStepCount", RayStepCount);
            mat.SetFloat("_ScreenEdgeFade", ScreenEdgeFade);
            
            // Set textures
            mat.SetTexture("_MainTex", source.MainTexture);
            
            // Apply effect
            Graphics.Blit(source, destination, mat, 0);
        }
    }

    /// <summary>
    /// Default rendering pipeline implementation that handles standard forward rendering,
    /// post-processing effects, shadows, and debug visualization.
    /// </summary>
    public class DefaultRenderPipeline : RenderPipeline
    {
        const bool CAMERA_RELATIVE = false;


        #region Static Resources

        private static Mesh s_quadMesh;
        private static Mesh s_skyDome;
        private static Material s_defaultMaterial;
        private static Material s_skybox;
        private static Material s_gizmo;

        private static RenderTexture? ShadowMap;
        private static GraphicsBuffer? LightBuffer;
        private static int LightCount;

        public static DefaultRenderPipeline Default { get; } = new();
        public static HashSet<int> S_activeObjectIds { get => s_activeObjectIds; set => s_activeObjectIds = value; }

        private static Double4x4 s_prevViewProjMatrix;
        private static Dictionary<int, Double4x4> s_prevModelMatrices = new();
        private static HashSet<int> s_activeObjectIds = new();
        private const int CLEANUP_INTERVAL_FRAMES = 120; // Clean up every 120 frames
        private static int s_framesSinceLastCleanup = 0;

        #endregion

        #region Resource Management

        private static void ValidateDefaults()
        {
            s_quadMesh ??= Mesh.GetFullscreenQuad();
            s_defaultMaterial ??= new Material(Shader.LoadDefault(DefaultShader.Standard));
            s_skybox ??= new Material(Shader.LoadDefault(DefaultShader.ProceduralSkybox));
            s_gizmo ??= new Material(Shader.LoadDefault(DefaultShader.Gizmos));

            if (s_skyDome == null)
            {
                Model skyDomeModel = Model.LoadDefault(DefaultModel.SkyDome);
                if(skyDomeModel == null)
                    throw new Exception("SkyDome model not found. Please ensure the model is included in the project.");
                s_skyDome = skyDomeModel.Meshes[0].Mesh;
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
                .Where(key => !S_activeObjectIds.Contains(key))
                .ToList();

            foreach (int key in unusedKeys)
                s_prevModelMatrices.Remove(key);

            // Clear the active IDs set for next frame
            S_activeObjectIds.Clear();
        }

        private static void TrackModelMatrix(int objectId, Double4x4 currentModel)
        {
            // Mark this object ID as active this frame
            S_activeObjectIds.Add(objectId);

            // Store current model matrix for next frame
            if (s_prevModelMatrices.TryGetValue(objectId, out Double4x4 prevModel))
                PropertyState.SetGlobalMatrix("prowl_PrevObjectToWorld", prevModel);
            else
                PropertyState.SetGlobalMatrix("prowl_PrevObjectToWorld", currentModel); // First frame, use current matrix

            s_prevModelMatrices[objectId] = currentModel;
        }

        #endregion

        #region Main Rendering

        public override void Render(Camera camera, in RenderingData data)
        {
            ValidateDefaults();

            // Main rendering with correct order of operations
            Internal_Render(camera, data);

            PropertyState.ClearGlobals();

            // Clean up unused matrices after rendering
            CleanupUnusedModelMatrices();
        }

        private static (List<ImageEffect> all, List<ImageEffect> opaque, List<ImageEffect> final) GatherImageEffects(Camera camera)
        {
            var all = new List<ImageEffect>();
            var opaqueEffects = new List<ImageEffect>();
            var finalEffects = new List<ImageEffect>();
        
            foreach (ImageEffect effect in camera.Effects)
            {
                all.Add(effect);

                if (effect.IsOpaqueEffect)
                    opaqueEffects.Add(effect);
                else
                    finalEffects.Add(effect);
            }
        
            return (all, opaqueEffects, finalEffects);
        }

        private static void SetupGlobalUniforms(CameraSnapshot css, Double3 sunDirection)
        {
            // Set View Rect
            //buffer.SetViewports((int)(camera.Viewrect.x * target.Width), (int)(camera.Viewrect.y * target.Height), (int)(camera.Viewrect.width * target.Width), (int)(camera.Viewrect.height * target.Height), 0, 1000);

            PropertyState.SetGlobalMatrix("prowl_PrevViewProj", css.previousViewProj);

            PropertyState.SetGlobalVector("_SunDir", sunDirection);

            // Setup Default Uniforms for this frame
            // Camera
            PropertyState.SetGlobalVector("_WorldSpaceCameraPos", CAMERA_RELATIVE ? Double3.Zero : css.cameraPosition);
            PropertyState.SetGlobalVector("_ProjectionParams", new Double4(1.0f, css.nearClipPlane, css.farClipPlane, 1.0f / css.farClipPlane));
            PropertyState.SetGlobalVector("_ScreenParams", new Double4(css.pixelWidth, css.pixelHeight, 1.0f + 1.0f / css.pixelWidth, 1.0f + 1.0f / css.pixelHeight));

            // Time
            PropertyState.SetGlobalVector("_Time", new Double4(Time.time / 20, Time.time, Time.time * 2, Time.frameCount));
            PropertyState.SetGlobalVector("_SinTime", new Double4(Math.Sin(Time.time / 8), Math.Sin(Time.time / 4), Math.Sin(Time.time / 2), Math.Sin(Time.time)));
            PropertyState.SetGlobalVector("_CosTime", new Double4(Math.Cos(Time.time / 8), Math.Cos(Time.time / 4), Math.Cos(Time.time / 2), Math.Cos(Time.time)));
            PropertyState.SetGlobalVector("prowl_DeltaTime", new Double4(Time.deltaTime, 1.0f / Time.deltaTime, Time.smoothDeltaTime, 1.0f / Time.smoothDeltaTime));

            // Fog
            Scene.FogParams fog = css.scene.Fog;
            Double4 fogParams;
            fogParams.X = fog.Density / Maths.Sqrt(0.693147181); // ln(2)
            fogParams.Y = fog.Density / 0.693147181; // ln(2)
            fogParams.Z = -1.0 / (fog.End - fog.Start);
            fogParams.W = fog.End / (fog.End - fog.Start);
            PropertyState.SetGlobalVector("prowl_FogColor", fog.Color);
            PropertyState.SetGlobalVector("prowl_FogParams", fogParams);
            PropertyState.SetGlobalVector("prowl_FogStates", new Float3(
                fog.Mode == Scene.FogParams.FogMode.Linear ? 1 : 0,
                fog.Mode == Scene.FogParams.FogMode.Exponential ? 1 : 0,
                fog.Mode == Scene.FogParams.FogMode.ExponentialSquared ? 1 : 0
                ));

            // Ambient Lighting
            Scene.AmbientLightParams ambient = css.scene.Ambient;
            PropertyState.SetGlobalVector("prowl_AmbientMode", new Double2(
                ambient.Mode == Scene.AmbientLightParams.AmbientMode.Uniform ? 1 : 0,
                ambient.Mode == Scene.AmbientLightParams.AmbientMode.Hemisphere ? 1 : 0
            ));

            PropertyState.SetGlobalVector("prowl_AmbientColor", ambient.Color);
            PropertyState.SetGlobalVector("prowl_AmbientSkyColor", ambient.SkyColor);
            PropertyState.SetGlobalVector("prowl_AmbientGroundColor", ambient.GroundColor);
        }

        private static void AssignCameraMatrices(Double4x4 view, Double4x4 projection)
        {
            PropertyState.SetGlobalMatrix("prowl_MatV", view);
            PropertyState.SetGlobalMatrix("prowl_MatIV", view.Invert());
            PropertyState.SetGlobalMatrix("prowl_MatP", projection);
            PropertyState.SetGlobalMatrix("prowl_MatVP", Maths.Mul(projection, view));
        }

        #endregion

        #region Scene Rendering

        public struct CameraSnapshot(Camera camera)
        {
            public Scene scene = camera.Scene;

            public Double3 cameraPosition = camera.Transform.position;
            public Double3 cameraUp = camera.Transform.up;
            public Double3 cameraForward = camera.Transform.forward;
            public LayerMask cullingMask = camera.CullingMask;
            public CameraClearFlags clearFlags = camera.ClearFlags;
            public float nearClipPlane = camera.NearClipPlane;
            public float farClipPlane = camera.FarClipPlane;
            public uint pixelWidth = camera.PixelWidth;
            public uint pixelHeight = camera.PixelHeight;
            public float aspect = camera.Aspect;
            public Double4x4 originView = camera.OriginViewMatrix;
            public Double4x4 view = CAMERA_RELATIVE ? camera.OriginViewMatrix : camera.ViewMatrix;
            public Double4x4 viewInverse = (CAMERA_RELATIVE ? camera.OriginViewMatrix : camera.ViewMatrix).Invert();
            public Double4x4 projection = camera.ProjectionMatrix;
            public Double4x4 previousViewProj = camera.PreviousViewProjectionMatrix;
            public FrustrumD worldFrustum = FrustrumD.FromMatrix(Maths.Mul(camera.ProjectionMatrix, camera.ViewMatrix));
            public DepthTextureMode depthTextureMode = camera.DepthTextureMode; // Flags, Can be None, Normals, MotionVectors
        }

        private static void Internal_Render(Camera camera, in RenderingData data)
        {
            // =======================================================
            // 0. Setup variables, and prepare the camera
            bool isHDR = camera.HDR;
            (List<ImageEffect> all, List<ImageEffect> opaqueEffects, List<ImageEffect> finalEffects) = GatherImageEffects(camera);
            IReadOnlyList<IRenderableLight> lights = camera.GameObject.Scene.Lights;
            Double3 sunDirection = GetSunDirection(lights);
            RenderTexture target = camera.UpdateRenderData();

            // =======================================================
            // 1. Pre Cull
            foreach (ImageEffect effect in all)
                effect.OnPreCull(camera);

            // =======================================================
            // 2. Take a snapshot of all Camera data
            CameraSnapshot css = new(camera);
            SetupGlobalUniforms(css, sunDirection);

            // =======================================================
            // 3. Cull Renderables based on Snapshot data
            IReadOnlyList<IRenderable> renderables = camera.GameObject.Scene.Renderables;
            HashSet<int> culledRenderableIndices = [];// CullRenderables(renderables, css.worldFrustum);

            // =======================================================
            // 4. Pre Render
            foreach (ImageEffect effect in all)
                effect.OnPreRender(camera);

            // =======================================================
            // 5. Setup Lighting and Shadows
            SetupLightingAndShadows(css, lights, renderables);

            // 5.1 Re-Assign camera matrices (The Lighting can modify these)
            AssignCameraMatrices(css.view, css.projection);

            // =======================================================
            // 6. Pre-Depth Pass
            // We draw objects to get the DepthBuffer but we also draw it into a ColorBuffer so we upload it as a Sampleable Texture
            RenderTexture preDepth = RenderTexture.GetTemporaryRT((int)css.pixelWidth, (int)css.pixelHeight, true, []);

            // Bind depth texture as the target
            Graphics.Device.BindFramebuffer(preDepth.frameBuffer);
            Graphics.Device.Clear(1.0f, 1.0f, 1.0f, 1.0f, ClearFlags.Depth | ClearFlags.Stencil);

            // Draw depth for all visible objects
            DrawRenderables(renderables, "RenderOrder", "DepthOnly", css.cameraPosition, culledRenderableIndices, false);

            // =======================================================
            // 6.1. Set the depth texture for use in post-processing
            PropertyState.SetGlobalTexture("_CameraDepthTexture", preDepth.InternalDepth);

            // =======================================================
            // 7. Opaque geometry
            RenderTexture forwardBuffer = RenderTexture.GetTemporaryRT((int)camera.PixelWidth, (int)camera.PixelHeight, true, [
                isHDR ? TextureImageFormat.Float4 : TextureImageFormat.Color4b, // Albedo
                TextureImageFormat.Float2, // Motion Vectors
                TextureImageFormat.Float3, // Normals
                TextureImageFormat.Float2, // Surface
                ]);

            // Copy the depth buffer to the forward buffer
            // This is technically not needed, however, a big reason people do a Pre-Depth pass outside post processing like SSAO
            // Is so the GPU can early cull lighting calculations in forward rendering
            // This turns Forward rendering into essentially deferred in the eyes of lighting, as it now only calculates lighting for pixels that are actually visible
            Graphics.Device.BindFramebuffer(preDepth.frameBuffer, FBOTarget.Read);
            Graphics.Device.BindFramebuffer(forwardBuffer.frameBuffer, FBOTarget.Draw);
            Graphics.Device.BlitFramebuffer(0, 0, preDepth.Width, preDepth.Height, 0, 0, forwardBuffer.Width, forwardBuffer.Height, ClearFlags.Depth, BlitFilter.Nearest);

            // 7.1 Bind the forward buffer fully, The bit only binds it for Drawing into, We need to bind it for reading too
            Graphics.Device.BindFramebuffer(forwardBuffer.frameBuffer);
            Graphics.Device.Clear(0.0f, 0.0f, 0.0f, 1.0f, ClearFlags.Color | ClearFlags.Stencil); // Dont clear Depth

            DrawRenderables(renderables, "RenderOrder", "Opaque", css.cameraPosition, culledRenderableIndices, true);

            // 8.1 Set the Motion Vectors Texture for use in post-processing
            PropertyState.SetGlobalTexture("_CameraMotionVectorsTexture", forwardBuffer.InternalTextures[1]);
            // 8.2 Set the Normals Texture for use in post-processing
            PropertyState.SetGlobalTexture("_CameraNormalsTexture", forwardBuffer.InternalTextures[2]);
            // 8.3 Set the Surface Texture for use in post-processing
            PropertyState.SetGlobalTexture("_CameraSurfaceTexture", forwardBuffer.InternalTextures[3]);

            // 9. Skybox (if enabled)
            // You may be wondering why we render the skybox here, after the opaque geometry
            // and not before it. This is actually an optimization.
            // The skybox can be expensive to draw since its a Procedural Atmosphere Shader
            // We want to avoid drawing as much of it as possible, So by drawing it AFTER the Opaque geometry
            // The GPU Knows that it can skip drawing the skybox for pixels that are already covered by opaque geometry
            if (css.clearFlags == CameraClearFlags.Skybox)
                RenderSkybox(css);

            // 10. Apply opaque post-processing effects
            if (opaqueEffects.Count > 0)
                DrawImageEffects(forwardBuffer, opaqueEffects, ref isHDR);

            // 11. Transparent geometry
            DrawRenderables(renderables, "RenderOrder", "Transparent", css.cameraPosition, culledRenderableIndices, false);

            // 12. Apply final post-processing effects
            if (finalEffects.Count > 0)
                DrawImageEffects(forwardBuffer, finalEffects, ref isHDR);


            //if (data.DisplayGizmo)
                RenderGizmos(css);

            // 13. Blit the Result to the camera's Target whether thats the Screen or a RenderTexture
            bool clearColor = camera.ClearFlags == CameraClearFlags.ColorOnly || camera.ClearFlags == CameraClearFlags.DepthColor;
            bool clearDepth = camera.ClearFlags == CameraClearFlags.DepthOnly || camera.ClearFlags == CameraClearFlags.DepthColor;
            bool drawSkybox = camera.ClearFlags == CameraClearFlags.Skybox;

            // 14. Blit Result to target, If target is null Blit will go to the Screen/Window
            Graphics.Blit(forwardBuffer, target, null, 0, clearDepth || drawSkybox, clearColor || drawSkybox, camera.ClearColor);

            // 15. Post Render
            foreach (ImageEffect effect in all)
                effect.OnPostRender(camera);


            RenderTexture.ReleaseTemporaryRT(preDepth);
            RenderTexture.ReleaseTemporaryRT(forwardBuffer);

            // Reset bound framebuffer if any is bound
            Graphics.Device.UnbindFramebuffer();
            Graphics.Device.Viewport(0, 0, (uint)Window.InternalWindow.FramebufferSize.X, (uint)Window.InternalWindow.FramebufferSize.Y);
        }

        private static HashSet<int> CullRenderables(IReadOnlyList<IRenderable> renderables, FrustrumD? worldFrustum, LayerMask cullingMask)
        {
            HashSet<int> culledRenderableIndices = [];
            for (int renderIndex = 0; renderIndex < renderables.Count; renderIndex++)
            {
                IRenderable renderable = renderables[renderIndex];

                //if (worldFrustum != null && CullRenderable(renderable, worldFrustum))
                //{
                //    culledRenderableIndices.Add(renderIndex);
                //    continue;
                //}

                if (cullingMask.HasLayer(renderable.GetLayer()) == false)
                {
                    culledRenderableIndices.Add(renderIndex);
                    continue;
                }
            }
            return culledRenderableIndices;
        }

        private static void SetupLightingAndShadows(CameraSnapshot css, IReadOnlyList<IRenderableLight> lights, IReadOnlyList<IRenderable> renderables)
        {
            ShadowAtlas.TryInitialize();
            ShadowAtlas.Clear();

            CreateLightBuffer(css.cameraPosition, css.cullingMask, lights, renderables);

            if (ShadowMap != null)
                PropertyState.SetGlobalTexture("_ShadowAtlas", ShadowMap.InternalDepth);
            //PropertyState.SetGlobalBuffer("_Lights", LightBuffer, 0);
            //PropertyState.SetGlobalInt("_LightCount", LightCount);
            PropertyState.SetGlobalVector("prowl_ShadowAtlasSize", new Double2(ShadowAtlas.GetSize(), ShadowAtlas.GetSize()));
        }
        
        private static void CreateLightBuffer(Double3 cameraPosition, LayerMask cullingMask, IReadOnlyList<IRenderableLight> lights, IReadOnlyList<IRenderable> renderables)
        {
            Graphics.Device.BindFramebuffer(ShadowAtlas.GetAtlas().frameBuffer);
            Graphics.Device.Clear(0.0f, 0.0f, 0.0f, 1.0f, ClearFlags.Depth | ClearFlags.Stencil);

            // We have AtlasWidth slots for shadow maps
            // a single shadow map can consume multiple slots if its larger then 128x128
            // We need to distribute these slots and resolutions out to lights
            // based on their distance from the camera
            int width = ShadowAtlas.GetAtlasWidth();

            int numDirLights = 0;
            int spotLightIndex = 0;
            const int MAX_SPOT_LIGHTS = 8;

            foreach (IRenderableLight light in lights)
            {
                if (cullingMask.HasLayer(light.GetLayer()) == false)
                    continue;

                // Calculate resolution based on distance
                int res = CalculateResolution(Maths.Distance(cameraPosition, light.GetLightPosition()));
                if (light is DirectionalLight dir)
                    res = (int)dir.shadowResolution;
        
                if (light.DoCastShadows())
                {
                    Double3 oldPos = Double3.Zero;
                    //if (light is DirectionalLight dirLight)
                    //{
                    //    // Create light space transform matrices
                    //    Vector3 lightDir = dirLight.Transform.forward;
                    //    Vector3 lightUp = dirLight.Transform.up;
                    //    Vector3 lightRight = Vector3.Cross(lightUp, lightDir).normalized;
                    //    lightUp = Vector3.Cross(lightDir, lightRight).normalized; // Recompute to ensure orthogonality
                    //
                    //    // Create light space matrix (world to light space transform)
                    //    Matrix4x4 worldToLight = new Matrix4x4(
                    //        new Vector4(lightRight.x, lightUp.x, lightDir.x, 0),
                    //        new Vector4(lightRight.y, lightUp.y, lightDir.y, 0),
                    //        new Vector4(lightRight.z, lightUp.z, lightDir.z, 0),
                    //        new Vector4(0, 0, 0, 1)
                    //    );
                    //
                    //    // Transform camera position to light space
                    //    Vector3 lightSpacePos = Vector3.Transform(cameraPosition, worldToLight);
                    //
                    //    // Calculate texel size in light space
                    //    float texelSize = (dirLight.shadowDistance * 2) / res;
                    //
                    //    // Snap in light space (only X and Y components, Z doesn't matter for directional light)
                    //    Vector3 snappedLightPos = new Vector3(
                    //        Maths.Round(lightSpacePos.x / texelSize) * texelSize,
                    //        Maths.Round(lightSpacePos.y / texelSize) * texelSize,
                    //        lightSpacePos.z
                    //    );
                    //
                    //    // Transform back to world space
                    //    Matrix4x4 lightToWorld = worldToLight.Invert();
                    //    Vector3 snappedWorldPos = Vector3.Transform(snappedLightPos, lightToWorld);
                    //
                    //    oldPos = dirLight.Transform.position;
                    //    dirLight.Transform.position = snappedWorldPos;
                    //}
        
                    // Find a slot for the shadow map
                    Int2? slot = ShadowAtlas.ReserveTiles(res, res, light.GetLightID());
                    
                    int AtlasX, AtlasY, AtlasWidth;

                    if (slot != null)
                    {
                        AtlasX = slot.Value.X;
                        AtlasY = slot.Value.Y;
                        AtlasWidth = res;
                    
                        // Draw the shadow map
                        ShadowMap = ShadowAtlas.GetAtlas();
                    
                        Graphics.Device.Viewport(slot.Value.X, slot.Value.Y, (uint)res, (uint)res);
                    
                        light.GetShadowMatrix(out Double4x4 view, out Double4x4 proj);

                        FrustrumD frustum = FrustrumD.FromMatrix(Maths.Mul(proj, view));
                        if (CAMERA_RELATIVE)
                            view.Translation = Double3.Zero;

                        HashSet<int> culledRenderableIndices = [];// CullRenderables(renderables, frustum);
                        AssignCameraMatrices(view, proj);
                        DrawRenderables(renderables, "LightMode", "ShadowCaster", light.GetLightPosition(), culledRenderableIndices, false);
                    }
                    else
                    {
                        AtlasX = -1;
                        AtlasY = -1;
                        AtlasWidth = 0;
                    }
                    
                    
                    if (light is DirectionalLight dirLight2)
                    {
                        // Return the light to its original position
                        //dirLight2.Transform.position = oldPos;
                        dirLight2.UploadToGPU(CAMERA_RELATIVE, cameraPosition, AtlasX, AtlasY, AtlasWidth);
                    }
                    else if (light is SpotLight spotLight && spotLightIndex < MAX_SPOT_LIGHTS)
                    {
                        spotLight.UploadToGPU(CAMERA_RELATIVE, cameraPosition, AtlasX, AtlasY, AtlasWidth, spotLightIndex);
                        spotLightIndex++;
                    }
                }
                else
                {
                    if (light is DirectionalLight dirL)
                    {
                        dirL.UploadToGPU(CAMERA_RELATIVE, cameraPosition, -1, -1, 0);
                    }
                    else if (light is SpotLight spotLight && spotLightIndex < MAX_SPOT_LIGHTS)
                    {
                        spotLight.UploadToGPU(CAMERA_RELATIVE, cameraPosition, -1, -1, 0, spotLightIndex);
                        spotLightIndex++;
                    }
                }
            }

            // Set the spot light count
            PropertyState.SetGlobalInt("_SpotLightCount", spotLightIndex);
        
        
            //unsafe
            //{
            //    if (LightBuffer == null || gpuLights.Count > LightCount)
            //    {
            //        LightBuffer?.Dispose();
            //        LightBuffer = Graphics.Device.CreateBuffer<GPULight>(BufferType.UniformBuffer, gpuLights.ToArray(), true);
            //    }
            //    else
            //    {
            //        // Update existing buffer
            //        Graphics.Device.UpdateBuffer<GPULight>(LightBuffer, 0, gpuLights.ToArray());
            //    }
            //
            //    LightCount = lights.Count;
            //}
        }

        private static int CalculateResolution(double distance)
        {
            double t = Maths.Clamp(distance / 16f, 0, 1);
            int tileSize = ShadowAtlas.GetTileSize();
            int resolution = Maths.RoundToInt(Maths.Lerp(ShadowAtlas.GetMaxShadowSize(), tileSize, t));
        
            // Round to nearest multiple of tile size
            return Maths.Max(tileSize, (resolution / tileSize) * tileSize);
        }
        
        private static Double3 GetSunDirection(IReadOnlyList<IRenderableLight> lights)
        {
            if (lights.Count > 0 && lights[0] is IRenderableLight light && light.GetLightType() == LightType.Directional)
                return light.GetLightDirection();
            return Double3.UnitY;
        }

        private static void RenderSkybox(CameraSnapshot css)
        {
            s_skybox.SetMatrix("prowl_MatVP", Maths.Mul(css.projection, css.originView));
            Graphics.DrawMeshNow(s_skyDome, s_skybox);
        }

        private static void RenderGizmos(CameraSnapshot css)
        {
            Double4x4 vp = Maths.Mul(css.projection, css.view);
            (Mesh? wire, Mesh? solid) = Debug.GetGizmoDrawData(CAMERA_RELATIVE, css.cameraPosition);
            
            if (wire != null || solid != null)
            {
                // The vertices have already been transformed by the gizmo system to be camera relative (if needed) so we just need to draw them
                s_gizmo.SetMatrix("prowl_MatVP", vp);
                if (wire != null) Graphics.DrawMeshNow(wire, s_gizmo);
                if (solid != null) Graphics.DrawMeshNow(solid, s_gizmo);
            }

            //List<GizmoBuilder.IconDrawCall> icons = Debug.GetGizmoIcons();
            //if (icons != null)
            //{
            //    buffer.SetMaterial(s_gizmo);
            //
            //    foreach (GizmoBuilder.IconDrawCall icon in icons)
            //    {
            //        Vector3 center = icon.center;
            //        if (CAMERA_RELATIVE)
            //            center -= css.cameraPosition;
            //        Matrix4x4 billboard = Matrix4x4.CreateBillboard(center, Vector3.zero, css.cameraUp, css.cameraForward);
            //
            //        buffer.SetMatrix("_Matrix_VP", (billboard * vp).ToFloat());
            //        buffer.SetTexture("_MainTex", icon.texture);
            //
            //        buffer.DrawSingle(s_quadMesh);
            //    }
            //}
        }

        #endregion

        private static void DrawImageEffects(RenderTexture forwardBuffer, List<ImageEffect> effects, ref bool isHDR)
        {
            // Early exit if no effects to process
            if (effects == null || effects.Count == 0)
                return;

            // Create two buffers for ping-pong rendering
            RenderTexture sourceBuffer = forwardBuffer;

            // Determine if we need to start in LDR mode
            bool firstEffectIsLDR = effects.Count > 0 && effects[0].TransformsToLDR;
            TextureImageFormat destFormat = isHDR && !firstEffectIsLDR ? TextureImageFormat.Float4 : TextureImageFormat.Color4b;

            // Create destination buffer
            RenderTexture destBuffer = RenderTexture.GetTemporaryRT(
                forwardBuffer.Width,
                forwardBuffer.Height,
                false,
                [destFormat]
            );

            // Update HDR flag if needed
            if (firstEffectIsLDR)
            {
                isHDR = false;
            }

            // Keep track of temporary render textures that need cleanup
            List<RenderTexture> tempTextures = new List<RenderTexture> { destBuffer };

            try
            {
                // Process each effect
                for (int i = 0; i < effects.Count; i++)
                {
                    ImageEffect effect = effects[i];

                    // Handle HDR to LDR transition
                    if (isHDR && effect.TransformsToLDR)
                    {
                        isHDR = false;

                        // If destination buffer is HDR, we need to replace it with LDR
                        if (destBuffer != forwardBuffer)
                        {
                            RenderTexture.ReleaseTemporaryRT(destBuffer);
                            tempTextures.Remove(destBuffer);
                        }

                        // Create new LDR destination buffer
                        destBuffer = RenderTexture.GetTemporaryRT(
                            forwardBuffer.Width,
                            forwardBuffer.Height,
                            false,
                            [TextureImageFormat.Color4b]
                        );

                        if (destBuffer != forwardBuffer)
                        {
                            tempTextures.Add(destBuffer);
                        }
                    }

                    // Apply the effect
                    effect.OnRenderImage(sourceBuffer, destBuffer);

                    // Swap buffers for next iteration
                    RenderTexture temp = sourceBuffer;
                    sourceBuffer = destBuffer;
                    destBuffer = temp;

                    // Update temp texture tracking after swap
                    // sourceBuffer now contains the result, destBuffer is the old source
                    if (sourceBuffer != forwardBuffer && !tempTextures.Contains(sourceBuffer))
                    {
                        tempTextures.Add(sourceBuffer);
                    }
                    if (destBuffer == forwardBuffer)
                    {
                        tempTextures.Remove(destBuffer);
                    }
                }

                // After all effects, copy result back to forwardBuffer if needed
                if (sourceBuffer != forwardBuffer)
                {
                    Graphics.Device.BindFramebuffer(sourceBuffer.frameBuffer, FBOTarget.Read);
                    Graphics.Device.BindFramebuffer(forwardBuffer.frameBuffer, FBOTarget.Draw);
                    Graphics.Device.BlitFramebuffer(
                        0, 0, sourceBuffer.Width, sourceBuffer.Height,
                        0, 0, forwardBuffer.Width, forwardBuffer.Height,
                        ClearFlags.Color, BlitFilter.Nearest
                    );
                }
            }
            catch (Exception ex)
            {
                // Re-throw the exception after cleanup
                throw new Exception($"Error in DrawImageEffects: {ex.Message}", ex);
            }
            finally
            {
                // Clean up all temporary render textures
                foreach (var tempRT in tempTextures)
                {
                    if (tempRT != forwardBuffer)
                    {
                        RenderTexture.ReleaseTemporaryRT(tempRT);
                    }
                }
            }
        }

        private static void DrawRenderables(IReadOnlyList<IRenderable> renderables, string tag, string tagValue, Double3 cameraPosition, HashSet<int> culledRenderableIndices, bool updatePreviousMatrices)
        {
            bool hasRenderOrder = !string.IsNullOrWhiteSpace(tag);
            for(int renderIndex=0; renderIndex < renderables.Count; renderIndex++)
            {
                if (culledRenderableIndices?.Contains(renderIndex) ?? false)
                    continue;

                IRenderable renderable = renderables[renderIndex];

                var material = renderable.GetMaterial();
                if (material.Shader == null) continue;

                int passIndex = -1;
                foreach (ShaderPass pass in material.Shader.Passes)
                {
                    passIndex++;

                    // Skip this pass if it doesn't have the expected tag
                    if (hasRenderOrder && !pass.HasTag(tag, tagValue))
                        continue;

                    renderable.GetRenderingData(out PropertyState properties, out Mesh mesh, out Double4x4 model);

                    // Store previous model matrix mainly for motion vectors, however, the user can use it for other things
                    var instanceId = properties.GetInt("_ObjectID");
                    if (updatePreviousMatrices && instanceId != 0)
                        TrackModelMatrix(instanceId, model);

                    if (CAMERA_RELATIVE)
                        model.Translation -= cameraPosition;

                    PropertyState.SetGlobalMatrix("prowl_ObjectToWorld", model);
                    PropertyState.SetGlobalMatrix("prowl_WorldToObject", model.Invert());

                    PropertyState.SetGlobalColor("_MainColor", Colors.White);

                    material._properties.ApplyOverride(properties);

                    Graphics.DrawMeshNow(mesh, material, passIndex);
                }
            }
        }

        private static bool CullRenderable(IRenderable renderable, FrustrumD cameraFrustum)
        {
            renderable.GetCullingData(out bool isRenderable, out AABBD bounds);

            return !isRenderable || !cameraFrustum.Intersects(bounds);
        }
    }
}
