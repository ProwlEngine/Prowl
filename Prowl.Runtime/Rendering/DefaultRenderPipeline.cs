// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

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
    private static Material s_gradientSkybox;
    private static Material s_gizmo;
    private static Material? s_iconMaterial;
    private static Mesh? s_iconQuad;
    private static Mesh s_gridMesh;
    private static Material s_gridMaterial;

    public static DefaultRenderPipeline Default { get; } = new();

    // Per-Scene BVH state. Keyed weakly so it goes away with the scene without an explicit
    // unload hook from this side; a dropped scene drops its light textures on the next GC pass.
    private static readonly ConditionalWeakTable<Scene, SceneLightSystem> s_lightSystems = new();

    /// <summary>Get (or create) the light system bound to a scene. Creating one is cheap (no
    /// GPU allocation until the first <see cref="SceneLightSystem.Reconcile"/>).</summary>
    public static SceneLightSystem GetOrCreateLightSystem(Scene scene)
    {
        if (scene == null) throw new ArgumentNullException(nameof(scene));
        return s_lightSystems.GetValue(scene, _ => new SceneLightSystem());
    }

    #endregion

    #region Resource Management

    private static void ValidateDefaults()
    {
        s_quadMesh ??= Mesh.GetFullscreenQuad();
        s_defaultMaterial ??= new Material(Shader.LoadDefault(DefaultShader.Standard));
        s_skybox ??= new Material(Shader.LoadDefault(DefaultShader.ProceduralSkybox));
        s_gizmo ??= new Material(Shader.LoadDefault(DefaultShader.Gizmos));

        if (s_skyDome.IsNotValid())
        {
            using var stream = EmbeddedResources.GetStream("Assets/Defaults/SkyDome.obj");
            var skyImport = new AssetImporting.ModelImporter().Import(stream, "SkyDome.obj");
            s_skyDome = skyImport.Meshes.Count > 0 ? skyImport.Meshes[0] : new Resources.Mesh { Name = "SkyDome" };
        }

        // Pre-compute and upload BRDF integration LUT for PBR
        BRDFLutGenerator.UploadGlobal();
    }

    #endregion

    #region Main Rendering

    public override void Render(Camera camera, in RenderingData data)
    {
        ValidateDefaults();

        // Main rendering with correct order of operations. The CommandExecutor
        // keeps its own GL state mirror and skips redundant binds, so we no
        // longer need a per-render "reset to defaults" call here the first state
        // change in the pipeline's CBs picks up wherever GL is.
        Internal_Render(camera, data);

        PropertyState.ClearGlobals();

        base.Render(camera, in data);
    }

    private Dictionary<RenderStage, List<ImageEffect>> GatherImageEffects(Camera camera)
    {
        var effectsByStage = new Dictionary<RenderStage, List<ImageEffect>>
        {
            { RenderStage.AfterOpaques, new List<ImageEffect>() },
            { RenderStage.PostProcess, new List<ImageEffect>() }
        };

        foreach (ImageEffect effect in camera.Effects)
        {
            if (effect == null || !effect.Enabled) continue;
            RenderStage stage = effect.Stage;
            if (effectsByStage.ContainsKey(stage))
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
            RenderStats.AddImageEffect();
        }
    }

    #endregion

    #region Scene Rendering

    private void Internal_Render(Camera camera, in RenderingData data)
    {
        // =======================================================
        // 0. Setup
        bool isHDR = camera.HDR;
        var effectsByStage = GatherImageEffects(camera);
        var allEffects = new List<ImageEffect>();
        foreach (var effects in effectsByStage.Values)
            allEffects.AddRange(effects);

        // Fire OnDisable on effects that were active last frame but aren't now
        // (user disabled them, removed them from Camera.Effects, or hot-swapped).
        camera.UpdateImageEffectLifecycle(allEffects);

        RenderTexture target = camera.UpdateRenderData();

        // =======================================================
        // 1. Pre Cull
        foreach (ImageEffect effect in allEffects)
            effect.OnPreCull(camera);

        // =======================================================
        // 2. Camera snapshot and global uniforms
        CameraSnapshot css = new(camera);
        SetupGlobalUniforms(css);

        // =======================================================
        // 3. Collect and Cull Renderables
        var (renderables, lights) = CollectRenderables(camera.GameObject.Scene, camera);
        //lights.Clear();

        // Inject editor grid
        if (data.DisplayGrid)
        {
            EnsureGridResources();
            if (s_gridMesh != null && s_gridMaterial != null)
            {
                float cx = MathF.Round(css.CameraPosition.X);
                float cz = MathF.Round(css.CameraPosition.Z);
                var gridTransform = Float4x4.CreateTranslation(new Float3(cx, 0, cz));
                renderables.Add(new MeshRenderable(s_gridMesh, s_gridMaterial, gridTransform, 0));
            }
        }

        bool[] culledRenderableIndices = CullRenderables(renderables, css.WorldFrustum, css.CullingMask);

        RenderStats.AddCamera();

        int dirCount = 0, pointCount = 0, spotCount = 0, shadowCount = 0;
        foreach (var l in lights)
        {
            switch (l.GetLightType())
            {
                case LightType.Directional: dirCount++; break;
                case LightType.Point: pointCount++; break;
                case LightType.Spot: spotCount++; break;
            }
            if (l.DoCastShadows()) shadowCount++;
        }
        RenderStats.AddLightCounts(lights.Count, dirCount, pointCount, spotCount, shadowCount);

        // =======================================================
        // 4. Pre Render
        foreach (ImageEffect effect in allEffects)
            effect.OnPreRender(camera);

        // =======================================================
        // 5. Light system reconcile + shadow atlas + uniform upload
        // Reconcile does the cheap work first (BVH refits, shadow caster picks). Then the atlas
        // is rendered for only the directional and the closest-N point/spot, and finally the
        // BVH textures + directional + shadow uniforms are pushed to the GPU.
        SceneLightSystem lightSystem = GetOrCreateLightSystem(css.Scene);
        lightSystem.Reconcile(lights, css.CameraPosition, css.CullingMask);

        // ─── Shadow atlas setup (clear) ───
        // Done in its own CB and submitted before the lights start so the depth/stencil
        // clear is in place before any face/cascade draws into the atlas. Each face
        // then submits its own CB (necessary because each face uploads different
        // view/proj matrices and they can't share a CB see Light.RenderShadows).
        {
            using var shadowSetup = Graphics.GetCommandBuffer("ShadowAtlasClear");
            shadowSetup.SetRenderTarget(ShadowAtlas.GetAtlas().frameBuffer);
            shadowSetup.ClearRenderTarget(ClearFlags.Depth | ClearFlags.Stencil, new Color(0, 0, 0, 1));
            Graphics.Submit(shadowSetup);
        }

        RenderStats.BeginShadowPass();
        lightSystem.RenderShadows(this, css.CameraPosition, renderables);
        RenderStats.EndShadowPass();

        AssignCameraMatrices(css.View, css.Projection);
        lightSystem.UploadGlobalUniforms();

        UploadFogUniforms(css.Scene);
        UploadAmbientUniforms(css.Scene);

        // Main color render target
        RenderTexture colorRT = RenderTexture.GetTemporaryRT((int)css.PixelWidth, (int)css.PixelHeight, true, [
            isHDR ? TextureImageFormat.Short4 : TextureImageFormat.Color4b,
        ]);

        // Unified prepass RT. One MRT pass writes everything depth-based effects need:
        //   depth attachment      = scene depth
        //   color 0 (Color4b)     = view-space normals
        //   color 1 (Short4)      = motion vectors (.rg) + roughness (.b) + metallic (.a)
        RenderTexture prepass = RenderTexture.GetTemporaryRT((int)css.PixelWidth, (int)css.PixelHeight, true, [
            TextureImageFormat.Color4b,
            TextureImageFormat.Short4,
        ]);

        // ─── Pre-pass + opaque CB ───
        var mainCmd = Graphics.GetCommandBuffer("ColorPass");

        // Single MRT prepass: depth + view-space normals + motion + roughness/metallic.
        // Cleared to zero so sky/background reads zero motion (and the unwritten normal/material
        // is only ever sampled by effects that gate on depth < 1, so its value there is moot).
        // updatePreviousMatrices = true so prowl_PrevObjectToWorld is bound per object for motion.
        mainCmd.SetRenderTarget(prepass.frameBuffer);
        mainCmd.ClearRenderTarget(ClearFlags.Color | ClearFlags.Depth, new Color(0, 0, 0, 0));
        DrawRenderables(mainCmd, renderables, "LightMode", "Prepass", new ViewerData(css), culledRenderableIndices, true);

        // Expose depth + normals + motion as globals AFTER the prepass draws have been encoded
        // into mainCmd. Using cmd.SetGlobalTexture (not the static directly) means the executor
        // mutates the global at the right point in submit order setting the static here would
        // expose the textures as sampler inputs while they are still bound FBO attachments for
        // the prepass draws above (GL undefined behavior).
        mainCmd.SetGlobalTexture("_CameraDepthTexture", prepass.InternalDepth);
        mainCmd.SetGlobalTexture("_CameraNormalsTexture", prepass.InternalTextures[0]);
        mainCmd.SetGlobalTexture("_CameraMotionVectorsTexture", prepass.InternalTextures[1]);

        // Copy depth from prepass into colorRT so the opaque pass can ZTest LEqual against it.
        mainCmd.SetRenderTargets(colorRT.frameBuffer, prepass.frameBuffer);
        mainCmd.BlitFramebuffer(0, 0, prepass.Width, prepass.Height,
                                 0, 0, colorRT.Width, colorRT.Height,
                                 ClearFlags.Depth, BlitFilter.Nearest);

        // Switch back to colorRT for the opaque draws.
        mainCmd.SetRenderTarget(colorRT.frameBuffer);
        mainCmd.SetViewport(0, 0, (uint)colorRT.Width, (uint)colorRT.Height);

        // Camera clear flags
        switch (camera.ClearFlags)
        {
            case CameraClearFlags.Skybox:
            {
                var skyColor = css.Scene.Skybox.Mode == Scene.SkyboxMode.SolidColor
                    ? css.Scene.Skybox.SolidColor : camera.ClearColor;
                mainCmd.ClearRenderTarget(ClearFlags.Color, skyColor);
                RenderSkybox(mainCmd, css, lights);
                break;
            }
            case CameraClearFlags.SolidColor:
                mainCmd.ClearRenderTarget(ClearFlags.Color, camera.ClearColor);
                break;
            case CameraClearFlags.Depth:
                if (target.IsValid())
                {
                    mainCmd.SetRenderTargets(colorRT.frameBuffer, target.frameBuffer);
                    mainCmd.BlitFramebuffer(0, 0, target.Width, target.Height,
                                            0, 0, colorRT.Width, colorRT.Height,
                                            ClearFlags.Color, BlitFilter.Nearest);
                    mainCmd.SetRenderTarget(colorRT.frameBuffer);
                }
                break;
            case CameraClearFlags.Nothing:
                if (target.IsValid())
                {
                    mainCmd.SetRenderTargets(colorRT.frameBuffer, target.frameBuffer);
                    mainCmd.BlitFramebuffer(0, 0, target.Width, target.Height,
                                            0, 0, colorRT.Width, colorRT.Height,
                                            ClearFlags.Color, BlitFilter.Nearest);
                    mainCmd.SetRenderTarget(colorRT.frameBuffer);
                }
                break;
        }

        // Forward opaques (with PBR lighting inline).
        RenderStats.BeginColorPass();
        DrawRenderables(mainCmd, renderables, "RenderOrder", "Opaque", new ViewerData(css), culledRenderableIndices, false, colorRT);

        // Submit so image effects see all the rendering above. Motion vectors were already
        // produced jitter-free in the unified prepass (via PROWL_MATRIX_VP_NONJITTERED), so no
        // separate pass or mid-frame matrix swap is needed here.
        Graphics.Submit(mainCmd);

        // ─── AfterOpaques image effects ───
        // Image effects rent + submit their own CommandBuffers internally.
        RenderStats.BeginPostFx();
        if (effectsByStage[RenderStage.AfterOpaques].Count > 0)
        {
            var afterContext = new RenderContext
            {
                DepthNormals = prepass,
                MotionVectors = prepass.InternalTextures[1],
                SceneColor = colorRT,
                Camera = camera,
                Width = (int)css.PixelWidth,
                Height = (int)css.PixelHeight,
                CurrentStage = RenderStage.AfterOpaques
            };
            ExecuteImageEffects(afterContext, effectsByStage[RenderStage.AfterOpaques]);
        }
        RenderStats.EndPostFx();

        // ─── Transparents CB ───
        var transparentCmd = Graphics.GetCommandBuffer("Transparents");
        transparentCmd.SetRenderTarget(colorRT.frameBuffer);
        transparentCmd.SetViewport(0, 0, (uint)colorRT.Width, (uint)colorRT.Height);
        List<IRenderable> sortBackToFront = SortRenderables(renderables, culledRenderableIndices, css.CameraPosition, SortMode.BackToFront);
        DrawRenderables(transparentCmd, sortBackToFront, "RenderOrder", "Transparent", new ViewerData(css), null, false, colorRT);
        Graphics.Submit(transparentCmd);

        RenderStats.EndColorPass();

        // ─── PostProcess image effects ───
        RenderStats.BeginPostFx();
        if (effectsByStage[RenderStage.PostProcess].Count > 0)
        {
            var postContext = new RenderContext
            {
                DepthNormals = prepass,
                MotionVectors = prepass.InternalTextures[1],
                SceneColor = colorRT,
                Camera = camera,
                Width = (int)css.PixelWidth,
                Height = (int)css.PixelHeight,
                CurrentStage = RenderStage.PostProcess
            };

            ExecuteImageEffects(postContext, effectsByStage[RenderStage.PostProcess]);

            var replacedRTs = postContext.GetReplacedRTs();
            if (replacedRTs.Count > 0)
            {
                colorRT = postContext.SceneColor;
                foreach (var oldRT in replacedRTs)
                    RenderTexture.ReleaseTemporaryRT(oldRT);
            }
        }
        RenderStats.EndPostFx();

        // ─── Gizmos + final blit CB ───
        var finalCmd = Graphics.GetCommandBuffer("FinalBlit");
        if (data.DisplayGizmos)
        {
            finalCmd.SetRenderTarget(colorRT.frameBuffer);
            finalCmd.SetViewport(0, 0, (uint)colorRT.Width, (uint)colorRT.Height);
            RenderGizmos(finalCmd, css);
        }

        finalCmd.Blit(colorRT, target, null, 0, false, false);

        // Reset to backbuffer for whatever runs after the pipeline (Paper UI, etc.).
        finalCmd.SetRenderTarget(null);
        finalCmd.SetViewport(0, 0, (uint)Window.InternalWindow.FramebufferSize.X, (uint)Window.InternalWindow.FramebufferSize.Y);
        Graphics.Submit(finalCmd);

        // Save previous VP for next frame's motion vectors.
        camera.SavePreviousViewProjectionMatrix();

        foreach (ImageEffect effect in allEffects)
            effect.OnPostRender(camera);

        // Cleanup
        RenderTexture.ReleaseTemporaryRT(prepass);
        RenderTexture.ReleaseTemporaryRT(colorRT);
    }

    private static void UploadFogUniforms(Scene scene)
    {
        Scene.FogParams fog = scene.Fog;
        Float4 fogParams = Float4.Zero;
        fogParams.X = fog.Density / 1.2011224f;
        fogParams.Y = fog.Density / 0.693147181f;
        float fogRange = fog.End - fog.Start;
        if (MathF.Abs(fogRange) < 0.0001f) fogRange = 0.0001f;
        fogParams.Z = -1.0f / fogRange;
        fogParams.W = fog.End / fogRange;

        PropertyState.SetGlobalColor("_FogColor", fog.Color);
        PropertyState.SetGlobalVector("_FogParams", fogParams);
        PropertyState.SetGlobalVector("_FogStates", new Float3(
            fog.Mode == Scene.FogParams.FogMode.Linear ? 1 : 0,
            fog.Mode == Scene.FogParams.FogMode.Exponential ? 1 : 0,
            fog.Mode == Scene.FogParams.FogMode.ExponentialSquared ? 1 : 0
        ));
    }

    private static void UploadAmbientUniforms(Scene scene)
    {
        Scene.AmbientLightParams ambient = scene.Ambient;
        PropertyState.SetGlobalVector("_AmbientMode", new Float2(
            ambient.Mode == Scene.AmbientLightParams.AmbientMode.Uniform ? 1 : 0,
            ambient.Mode == Scene.AmbientLightParams.AmbientMode.Hemisphere ? 1 : 0
        ));
        PropertyState.SetGlobalColor("_AmbientColor", ambient.Color);
        PropertyState.SetGlobalColor("_AmbientSkyColor", ambient.SkyColor);
        PropertyState.SetGlobalColor("_AmbientGroundColor", ambient.GroundColor);
        PropertyState.SetGlobalFloat("_AmbientStrength", (float)ambient.Strength);
    }

    private void RenderSkybox(CommandBuffer cmd, CameraSnapshot css, List<IRenderableLight> lights)
    {
        var skyParams = css.Scene.Skybox;

        switch (skyParams.Mode)
        {
            case Scene.SkyboxMode.Procedural:
            {
                var sun = lights.FirstOrDefault(l => l is IRenderableLight rl && rl.GetLightType() == LightType.Directional);
                var sunDir = sun != null ? sun.GetLightDirection() : Float3.Normalize(new Float3(0.5f, -0.7f, 0.5f));
                s_skybox.SetVector("_SunDir", sunDir);
                cmd.DrawMesh(s_skyDome, s_skybox);
                break;
            }

            case Scene.SkyboxMode.SolidColor:
                // Camera clear already filled with color nothing more to do.
                break;

            case Scene.SkyboxMode.Gradient:
            {
                s_gradientSkybox ??= new Material(Shader.LoadDefault(DefaultShader.GradientSkybox));
                s_gradientSkybox.SetColor("_TopColor", skyParams.GradientTop);
                s_gradientSkybox.SetColor("_BottomColor", skyParams.GradientBottom);
                s_gradientSkybox.SetFloat("_Exponent", skyParams.GradientExponent);
                cmd.DrawMesh(s_skyDome, s_gradientSkybox);
                break;
            }

            case Scene.SkyboxMode.Material:
            {
                var customMat = skyParams.CustomMaterial.Res;
                if (customMat != null)
                    cmd.DrawMesh(s_skyDome, customMat);
                else
                    cmd.DrawMesh(s_skyDome, s_skybox);
                break;
            }
        }
    }

    private static void EnsureGridResources()
    {
        if (s_gridMesh == null)
        {
            const float e = 500f;
            s_gridMesh = new Mesh();
            s_gridMesh.Vertices = [new(-e, 0, -e), new(e, 0, -e), new(e, 0, e), new(-e, 0, e)];
            s_gridMesh.UV = [new(-e, -e), new(e, -e), new(e, e), new(-e, e)];
            s_gridMesh.Normals = [Float3.UnitY, Float3.UnitY, Float3.UnitY, Float3.UnitY];
            s_gridMesh.Indices = [0, 2, 1, 0, 3, 2];
            s_gridMesh.RecalculateBounds();
            s_gridMesh.Upload();
        }

        if (s_gridMaterial == null)
        {
            var shader = Shader.LoadDefault(DefaultShader.Grid);
            if (shader != null)
            {
                s_gridMaterial = new Material(shader);
                s_gridMaterial.SetColor("_GridColor", new Color(0.5f, 0.5f, 0.5f, 0.3f));
                s_gridMaterial.SetFloat("_PrimaryGridSize", 1f);
                s_gridMaterial.SetFloat("_SecondaryGridSize", 0.25f);
                s_gridMaterial.SetFloat("_LineWidth", 0.02f);
                s_gridMaterial.SetFloat("_Falloff", 1.5f);
                s_gridMaterial.SetFloat("_MaxDist", 500f);
            }
        }
    }

    private void RenderGizmos(CommandBuffer cmd, CameraSnapshot css)
    {
        Float4x4 vp = css.Projection * css.View;
        (Mesh? wire, Mesh? solid) = Debug.GetGizmoDrawData();

        if (wire.IsValid() || solid.IsValid())
        {
            if (wire.IsValid()) cmd.DrawMesh(wire, s_gizmo);
            if (solid.IsValid()) cmd.DrawMesh(solid, s_gizmo);
        }

        var icons = Debug.GetGizmoIcons();
        if (icons.Count > 0)
        {
            s_iconMaterial ??= new Material(Shader.LoadDefault(DefaultShader.GizmoIcon));
            s_iconQuad ??= Mesh.GetFullscreenQuad();

            foreach (var icon in icons)
            {
                if (icon.Texture == null || icon.Texture.IsDisposed) continue;
                s_iconMaterial.SetTexture("_MainTex", icon.Texture);
                s_iconMaterial.SetVector("_IconCenter", icon.Center);
                s_iconMaterial.SetFloat("_IconScale", icon.Scale);
                s_iconMaterial.SetVector("_IconColor", new Float4(icon.Color.R, icon.Color.G, icon.Color.B, icon.Color.A));
                cmd.DrawMesh(s_iconQuad, s_iconMaterial);
            }
        }
    }

    #endregion
}
