// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Runtime.Resources;
using Prowl.Vector;

using Material = Prowl.Runtime.Resources.Material;
using Mesh = Prowl.Runtime.Resources.Mesh;
using Shader = Prowl.Runtime.Resources.Shader;

namespace Prowl.Runtime.Rendering;

public struct ViewerData
{
    public Float3 Position;
    public Float3 Forward;
    public Float3 Up;
    public Float3 Right;

    public ViewerData(DefaultRenderPipeline.CameraSnapshot css)
    {
        Position = css.CameraPosition;
        Forward = css.CameraForward;
        Up = css.CameraUp;
        Right = css.CameraRight;
    }

    public ViewerData(Float3 position, Float3 forward, Float3 right, Float3 up) : this()
    {
        Position = position;
        Forward = forward;
        Right = right;
        Up = up;
    }
}

/// <summary>
/// Default rendering pipeline implementation that handles standard forward rendering,
/// post-processing effects, shadows, and debug visualization.
/// </summary>
public class DefaultRenderPipeline : RenderPipeline
{
    #region Static Resources

    private static Mesh s_quadMesh;
    private static Mesh s_skyDome;
    private static Material s_defaultMaterial;
    private static Material s_skybox;
    private static Material s_gizmo;
    private static Material s_deferredCompose;

    public static DefaultRenderPipeline Default { get; } = new();

    #endregion

    #region Resource Management

    private static void ValidateDefaults()
    {
        s_quadMesh ??= Mesh.GetFullscreenQuad();
        s_defaultMaterial ??= new Material(Shader.LoadDefault(DefaultShader.Standard));
        s_skybox ??= new Material(Shader.LoadDefault(DefaultShader.ProceduralSkybox));
        s_gizmo ??= new Material(Shader.LoadDefault(DefaultShader.Gizmos));

        // Load deferred shaders
        s_deferredCompose ??= new Material(Shader.LoadDefault(DefaultShader.DeferredCompose));

        if (s_skyDome.IsNotValid())
        {
            Model skyDomeModel = Model.LoadDefault(DefaultModel.SkyDome) ?? throw new Exception("SkyDome model not found. Please ensure the model is included in the project.");
            s_skyDome = skyDomeModel.Meshes[0].Mesh;
        }
    }

    #endregion

    #region Main Rendering

    public override void Render(Camera camera, in RenderingData data)
    {
        ValidateDefaults();

        // Main rendering with correct order of operations
        Internal_Render(camera, data);

        PropertyState.ClearGlobals();

        base.Render(camera, in data);
    }

    private Dictionary<RenderStage, List<ImageEffect>> GatherImageEffects(Camera camera)
    {
        var effectsByStage = new Dictionary<RenderStage, List<ImageEffect>>
        {
            { RenderStage.BeforeGBuffer, new List<ImageEffect>() },
            { RenderStage.AfterGBuffer, new List<ImageEffect>() },
            { RenderStage.DuringLighting, new List<ImageEffect>() },
            { RenderStage.AfterLighting, new List<ImageEffect>() },
            { RenderStage.PostProcess, new List<ImageEffect>() }
        };

        foreach (ImageEffect effect in camera.Effects)
        {
            // Get the stage, with backward compatibility for IsOpaqueEffect
            RenderStage stage = effect.Stage;

            #pragma warning disable CS0618 // Type or member is obsolete
            if (effect.IsOpaqueEffect && stage == RenderStage.PostProcess)
            {
                // Backward compatibility: IsOpaqueEffect means AfterLighting
                stage = RenderStage.AfterLighting;
            }
            #pragma warning restore CS0618

            effectsByStage[stage].Add(effect);
        }

        return effectsByStage;
    }

    private void ExecuteImageEffects(RenderContext context, List<ImageEffect> effects)
    {
        if (effects == null || effects.Count == 0)
            return;

        foreach (var effect in effects)
        {
            effect.OnRenderEffect(context);
        }
    }

    #endregion

    #region Scene Rendering

    private void Internal_Render(Camera camera, in RenderingData data)
    {
        // =======================================================
        // 0. Setup variables, and prepare the camera
        bool isHDR = camera.HDR;
        var effectsByStage = GatherImageEffects(camera);
        var allEffects = new List<ImageEffect>();
        foreach (var effects in effectsByStage.Values)
            allEffects.AddRange(effects);

        IReadOnlyList<IRenderableLight> lights = camera.GameObject.Scene.Lights;
        RenderTexture target = camera.UpdateRenderData();

        // =======================================================
        // 1. Pre Cull
        foreach (ImageEffect effect in allEffects)
            effect.OnPreCull(camera);

        // =======================================================
        // 2. Take a snapshot of all Camera data
        CameraSnapshot css = new(camera);
        SetupGlobalUniforms(css);

        // =======================================================
        // 3. Cull Renderables based on Snapshot data
        IReadOnlyList<IRenderable> renderables = camera.GameObject.Scene.Renderables;
        HashSet<int> culledRenderableIndices = CullRenderables(renderables, css.WorldFrustum, css.CullingMask);

        // =======================================================
        // 4. Pre Render
        foreach (ImageEffect effect in allEffects)
            effect.OnPreRender(camera);

        // =======================================================
        // 5. Setup Lighting and Shadows
        RenderShadowAtlas(css, lights, renderables);

        // 5.1 Re-Assign camera matrices (The Lighting can modify these)
        AssignCameraMatrices(css.View, css.Projection);

        // =======================================================
        // 6. Create GBuffer for Deferred Rendering
        // GBuffer layout:
        // BufferA: RGB = Albedo, A = Alpha
        // BufferB: RGB = Normal (view space), A = ShadingMode
        // BufferC: R = Roughness, G = Metalness, B = Specular, A = AO
        // BufferD: Custom Data per Shading Mode (e.g., Emissive for Lit mode)
        RenderTexture gBuffer = RenderTexture.GetTemporaryRT((int)css.PixelWidth, (int)css.PixelHeight, true, [
            TextureImageFormat.Short4, // BufferA - Albedo + Alpha
            TextureImageFormat.Color4b, // BufferB - Normal + ShadingMode
            TextureImageFormat.Color4b, // BufferC - Roughness, Metalness, Specular, AO
            TextureImageFormat.Color4b, // BufferD - Custom Data (Emissive, etc.)
            ]);

        // Bind GBuffer as the target
        Graphics.BindFramebuffer(gBuffer.frameBuffer);
        // 6.1 Clear GBuffer
        switch (camera.ClearFlags)
        {
            case CameraClearFlags.Skybox:
                Graphics.Clear(
                    (float)camera.ClearColor.R,
                    (float)camera.ClearColor.G,
                    (float)camera.ClearColor.B,
                    (float)camera.ClearColor.A,
                    ClearFlags.Color | ClearFlags.Depth
                );

                RenderSkybox(css);
                break;

            case CameraClearFlags.SolidColor:
                Graphics.Clear(
                    (float)camera.ClearColor.R,
                    (float)camera.ClearColor.G,
                    (float)camera.ClearColor.B,
                    (float)camera.ClearColor.A,
                    ClearFlags.Color | ClearFlags.Depth
                );
                break;

            case CameraClearFlags.Depth:
                Graphics.Clear(0, 0, 0, 0, ClearFlags.Depth);
                break;

            case CameraClearFlags.Nothing:
                // Do not clear anything
                break;
        }

        // 6.2 Draw opaque geometry to GBuffer
        //List<IRenderable> sortFrontToBack = SortRenderables(renderables, culledRenderableIndices, css.CameraPosition, SortMode.FrontToBack);
        DrawRenderables(renderables, "RenderOrder", "Opaque", new ViewerData(css), culledRenderableIndices, false); // Its deffered rendering, overdraw is cheap

        // =======================================================
        // 7. Deferred Lighting Pass - Render each light's contribution
        // Create light accumulation buffer
        RenderTexture lightAccumulation = RenderTexture.GetTemporaryRT((int)camera.PixelWidth, (int)camera.PixelHeight, false, [
            isHDR ? TextureImageFormat.Short4 : TextureImageFormat.Color4b, // Accumulated lighting
            ]);

        // Set GBuffer textures as global textures for shaders
        PropertyState.SetGlobalTexture("_GBufferA", gBuffer.InternalTextures[0]);
        PropertyState.SetGlobalTexture("_GBufferB", gBuffer.InternalTextures[1]);
        PropertyState.SetGlobalTexture("_GBufferC", gBuffer.InternalTextures[2]);
        PropertyState.SetGlobalTexture("_GBufferD", gBuffer.InternalTextures[3]);
        PropertyState.SetGlobalTexture("_CameraDepthTexture", gBuffer.InternalDepth);

        // Clear light accumulation to black
        Graphics.BindFramebuffer(lightAccumulation.frameBuffer);
        Graphics.Clear(0, 0, 0, 0, ClearFlags.Color);

        // Render each light's contribution (additive blending)
        foreach (IRenderableLight light in lights)
        {
            if (css.CullingMask.HasLayer(light.GetLayer()) == false)
                continue;

            light.OnRenderLight(gBuffer, lightAccumulation, css);
        }

        // =======================================================
        // 7.5. Apply DuringLighting effects (e.g., SSPT, GTAO that need light accumulation)
        if (effectsByStage[RenderStage.DuringLighting].Count > 0)
        {
            var lightingContext = new RenderContext
            {
                GBuffer = gBuffer,
                LightAccumulation = lightAccumulation,
                SceneColor = null, // Not available yet
                Camera = camera,
                Width = (int)css.PixelWidth,
                Height = (int)css.PixelHeight,
                CurrentStage = RenderStage.DuringLighting
            };

            ExecuteImageEffects(lightingContext, effectsByStage[RenderStage.DuringLighting]);
        }

        // =======================================================
        // 8. Deferred Composition Pass - Combine light accumulation with GBuffer
        // Create final composition output
        RenderTexture composedOutput = RenderTexture.GetTemporaryRT((int)camera.PixelWidth, (int)camera.PixelHeight, true, [
            isHDR ? TextureImageFormat.Short4 : TextureImageFormat.Color4b,
            ]);

        // Set GBuffer and light textures for compose shader
        s_deferredCompose.SetTexture("_LightAccumulation", lightAccumulation.InternalTextures[0]);
        s_deferredCompose.SetTexture("_GBufferA", gBuffer.InternalTextures[0]);
        s_deferredCompose.SetTexture("_GBufferB", gBuffer.InternalTextures[1]);
        s_deferredCompose.SetTexture("_GBufferD", gBuffer.InternalTextures[3]);
        s_deferredCompose.SetTexture("_CameraDepthTexture", gBuffer.InternalDepth);

        // Set fog parameters
        Scene.FogParams fog = css.Scene.Fog;
        Float4 fogParams = Float4.Zero;
        fogParams.X = fog.Density / 1.2011224f; // density/sqrt(ln(2))
        fogParams.Y = fog.Density / 0.693147181f; // ln(2)
        fogParams.Z = -1.0f / (fog.End - fog.Start);
        fogParams.W = fog.End / (fog.End - fog.Start);
        s_deferredCompose.SetColor("_FogColor", fog.Color);
        s_deferredCompose.SetVector("_FogParams", fogParams);
        s_deferredCompose.SetVector("_FogStates", new Float3(
            fog.Mode == Scene.FogParams.FogMode.Linear ? 1 : 0,
            fog.Mode == Scene.FogParams.FogMode.Exponential ? 1 : 0,
            fog.Mode == Scene.FogParams.FogMode.ExponentialSquared ? 1 : 0
        ));

        // Set ambient lighting parameters
        Scene.AmbientLightParams ambient = css.Scene.Ambient;
        s_deferredCompose.SetVector("_AmbientMode", new Float2(
            ambient.Mode == Scene.AmbientLightParams.AmbientMode.Uniform ? 1 : 0,
            ambient.Mode == Scene.AmbientLightParams.AmbientMode.Hemisphere ? 1 : 0
        ));
        s_deferredCompose.SetColor("_AmbientColor", ambient.Color);
        s_deferredCompose.SetColor("_AmbientSkyColor", ambient.SkyColor);
        s_deferredCompose.SetColor("_AmbientGroundColor", ambient.GroundColor);
        s_deferredCompose.SetFloat("_AmbientStrength", (float)ambient.Strength);

        // Perform composition
        Blit(lightAccumulation, composedOutput, s_deferredCompose, 0, false, false);

        // Copy depth from GBuffer to composed output for transparent rendering
        Graphics.BindFramebuffer(gBuffer.frameBuffer, FBOTarget.Read);
        Graphics.BindFramebuffer(composedOutput.frameBuffer, FBOTarget.Draw);
        Graphics.BlitFramebuffer(0, 0, gBuffer.Width, gBuffer.Height, 0, 0, composedOutput.Width, composedOutput.Height, ClearFlags.Depth, BlitFilter.Nearest);

        // Bind composed output for transparent rendering
        Graphics.BindFramebuffer(composedOutput.frameBuffer);

        // =======================================================
        // 9. Apply AfterLighting effects (opaque post-processing)
        if (effectsByStage[RenderStage.AfterLighting].Count > 0)
        {
            var afterLightingContext = new RenderContext
            {
                GBuffer = gBuffer,
                LightAccumulation = lightAccumulation,
                SceneColor = composedOutput,
                Camera = camera,
                Width = (int)css.PixelWidth,
                Height = (int)css.PixelHeight,
                CurrentStage = RenderStage.AfterLighting
            };

            ExecuteImageEffects(afterLightingContext, effectsByStage[RenderStage.AfterLighting]);
        }

        // =======================================================
        // 10. Transparent geometry (Forward rendered on top of composed result)
        List<IRenderable> sortBackToFront = SortRenderables(renderables, culledRenderableIndices, css.CameraPosition, SortMode.BackToFront);
        DrawRenderables(sortBackToFront, "RenderOrder", "Transparent", new ViewerData(css), null, false);

        // =======================================================
        // 11. Apply PostProcess effects (final post-processing)
        if (effectsByStage[RenderStage.PostProcess].Count > 0)
        {
            var postProcessContext = new RenderContext
            {
                GBuffer = gBuffer,
                LightAccumulation = lightAccumulation,
                SceneColor = composedOutput,
                Camera = camera,
                Width = (int)css.PixelWidth,
                Height = (int)css.PixelHeight,
                CurrentStage = RenderStage.PostProcess
            };

            ExecuteImageEffects(postProcessContext, effectsByStage[RenderStage.PostProcess]);

            // Effects may have replaced the scene color buffer (e.g., HDR to LDR)
            var replacedRTs = postProcessContext.GetReplacedRTs();
            if (replacedRTs.Count > 0)
            {
                // Update our reference to the new buffer
                composedOutput = postProcessContext.SceneColor;

                // Clean up old buffers
                foreach (var oldRT in replacedRTs)
                {
                    RenderTexture.ReleaseTemporaryRT(oldRT);
                }
            }
        }

        // =======================================================
        // 12. Render Gizmos
        RenderGizmos(css);

        // =======================================================
        // 13. Blit Result to target, If target is null Blit will go to the Screen/Window
        Blit(composedOutput, target, null, 0, false, false);

        // =======================================================
        // 14. Post Render
        foreach (ImageEffect effect in allEffects)
            effect.OnPostRender(camera);

        // =======================================================
        // 15. Cleanup temporary render textures
        RenderTexture.ReleaseTemporaryRT(gBuffer);
        RenderTexture.ReleaseTemporaryRT(lightAccumulation);
        RenderTexture.ReleaseTemporaryRT(composedOutput);

        // Reset bound framebuffer if any is bound
        Graphics.UnbindFramebuffer();
        Graphics.Viewport(0, 0, (uint)Window.InternalWindow.FramebufferSize.X, (uint)Window.InternalWindow.FramebufferSize.Y);
    }

    private void RenderShadowAtlas(CameraSnapshot css, IReadOnlyList<IRenderableLight> lights, IReadOnlyList<IRenderable> renderables)
    {
        Graphics.BindFramebuffer(ShadowAtlas.GetAtlas().frameBuffer);
        Graphics.Clear(0.0f, 0.0f, 0.0f, 1.0f, ClearFlags.Depth | ClearFlags.Stencil);

        // Process all lights - each light handles its own shadow rendering
        foreach (IRenderableLight light in lights)
        {
            if (css.CullingMask.HasLayer(light.GetLayer()) == false)
                continue;

            if (light is Light lightComponent)
            {
                lightComponent.RenderShadows(this, css.CameraPosition, renderables);
            }
        }
    }

    private void RenderSkybox(CameraSnapshot css)
    {
        // Set sun direction for skybox from scene's directional light
        var sun = css.Scene.Lights.FirstOrDefault(l => l is IRenderableLight rl && rl.GetLightType() == LightType.Directional);
        if (sun != null)
        {
            s_skybox.SetVector("_SunDir", sun.GetLightDirection());
        }

        DrawMeshNow(s_skyDome, s_skybox);
    }

    private void RenderGizmos(CameraSnapshot css)
    {
        Float4x4 vp = css.Projection * css.View;
        (Mesh? wire, Mesh? solid) = Debug.GetGizmoDrawData();

        if (wire.IsValid() || solid.IsValid())
        {
            if (wire.IsValid()) DrawMeshNow(wire, s_gizmo);
            if (solid.IsValid()) DrawMeshNow(solid, s_gizmo);
        }

#warning TODO: Implement Gizmo Icons rendering

        //List<GizmoBuilder.IconDrawCall> icons = Debug.GetGizmoIcons();
        //if (icons != null)
        //{
        //    buffer.SetMaterial(s_gizmo);
        //
        //    foreach (GizmoBuilder.IconDrawCall icon in icons)
        //    {
        //        Vector3 center = icon.center;
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
}
