// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

using Prowl.Runtime.Resources;
using Prowl.Runtime.UI;
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

    // Camera projection data, used by screen-space and world-space UI canvases.
    public uint PixelWidth;
    public uint PixelHeight;
    public Float4x4 ViewMatrix;
    public Float4x4 ProjectionMatrix;

    public ViewerData(DefaultRenderPipeline.CameraSnapshot css) : this()
    {
        Position = css.CameraPosition;
        Forward = css.CameraForward;
        Up = css.CameraUp;
        Right = css.CameraRight;
        PixelWidth = css.PixelWidth;
        PixelHeight = css.PixelHeight;
        ViewMatrix = css.View;
        ProjectionMatrix = css.Projection;
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

        // MSAA applies to the geometry passes only. The target is resolved back down to a
        // normal single-sampled RT before any image effect can sample it, so everything from
        // PostProcess onward is identical to the non-MSAA path.
        int samples = Math.Min((int)camera.MSAA, Graphics.MaxSamples);
        bool msaa = samples > 1;

        TextureImageFormat colorFormat = isHDR ? TextureImageFormat.Short4 : TextureImageFormat.Color4b;

        // Main color render target
        RenderTexture colorRT = RenderTexture.GetTemporaryRT((int)css.PixelWidth, (int)css.PixelHeight, true, [
            colorFormat,
        ], samples);

        // Unified prepass RT. One MRT pass writes everything depth-based effects need:
        //   depth attachment      = scene depth
        //   color 0 (Color4b)     = view-space normals
        //   color 1 (Short4)      = motion vectors (.rg) + roughness (.b) + metallic (.a)
        // Stays single-sampled even under MSAA: these are sampled as plain sampler2D by
        // GTAO/SSR/TAA, and resolving three attachments per frame would cost more than the
        // per-sample depth is worth to effects that are screen-space approximations anyway.
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
        // Skipped under MSAA: the prepass is single-sampled, and replicating its per-pixel depth
        // to every sample would defeat the coverage MSAA exists to capture. Opaques regenerate
        // correct per-sample depth on their own (RasterizerState defaults to ZWrite On / LEqual),
        // so this blit is only an early-Z optimization. The skybox does not need it either it
        // is ZTest Off / ZWrite Off and is simply overdrawn by the opaques.
        if (!msaa)
        {
            mainCmd.SetRenderTargets(colorRT.frameBuffer, prepass.frameBuffer);
            mainCmd.BlitFramebuffer(0, 0, prepass.Width, prepass.Height,
                                     0, 0, colorRT.Width, colorRT.Height,
                                     ClearFlags.Depth, BlitFilter.Nearest);
        }

        // Switch back to colorRT for the opaque draws.
        mainCmd.SetRenderTarget(colorRT.frameBuffer);
        mainCmd.SetViewport(0, 0, (uint)colorRT.Width, (uint)colorRT.Height);

        // With the depth blit skipped, nothing else initializes depth. This has to sit outside
        // the switch below: the Depth and Nothing branches never clear at all, and colorRT is
        // pooled, so they would otherwise inherit stale depth from last frame's tenant.
        if (msaa)
            mainCmd.ClearRenderTarget(ClearFlags.Depth, default);

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
            case CameraClearFlags.Nothing:
                if (target.IsValid())
                {
                    if (msaa)
                    {
                        // A framebuffer blit involving a multisampled target cannot scale, and
                        // RenderScale routinely makes target and colorRT different sizes. Copy
                        // with a fullscreen quad instead pass 1 of the blit shader is Blend Off,
                        // so target's color replaces rather than blends, and it is ZWrite Off so
                        // the depth cleared above survives.
                        mainCmd.Blit(target, colorRT, GetBlitMaterial(), 1);
                    }
                    else
                    {
                        mainCmd.SetRenderTargets(colorRT.frameBuffer, target.frameBuffer);
                        mainCmd.BlitFramebuffer(0, 0, target.Width, target.Height,
                                                0, 0, colorRT.Width, colorRT.Height,
                                                ClearFlags.Color, BlitFilter.Nearest);
                    }
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
            // These effects read and write SceneColor through a sampler2D, which a multisampled
            // buffer cannot be. Resolve a copy for them to work on, then put the result back into
            // the multisampled target so the transparents below still rasterize with real coverage
            // against the live multisampled depth buffer. Replicating the resolved average across
            // every sample does not lose the opaque edge AA already baked into it resolving
            // uniform samples again is a no-op.
            RenderTexture effectRT = colorRT;
            if (msaa)
            {
                effectRT = RenderTexture.GetTemporaryRT((int)css.PixelWidth, (int)css.PixelHeight, true, [colorFormat]);
                using var resolveCmd = Graphics.GetCommandBuffer("MSAAResolveAfterOpaques");
                resolveCmd.ResolveMultisample(colorRT, effectRT);
                Graphics.Submit(resolveCmd);
            }

            var afterContext = new RenderContext
            {
                DepthNormals = prepass,
                MotionVectors = prepass.InternalTextures[1],
                SceneColor = effectRT,
                Camera = camera,
                Width = (int)css.PixelWidth,
                Height = (int)css.PixelHeight,
                CurrentStage = RenderStage.AfterOpaques
            };
            ExecuteImageEffects(afterContext, effectsByStage[RenderStage.AfterOpaques]);

            if (msaa)
            {
                using (var restoreCmd = Graphics.GetCommandBuffer("MSAARestoreAfterOpaques"))
                {
                    // Pass 1 is Blend Off / ZWrite Off: replaces color, leaves MS depth alone.
                    restoreCmd.Blit(effectRT, colorRT, GetBlitMaterial(), 1);
                    Graphics.Submit(restoreCmd);
                }
                RenderTexture.ReleaseTemporaryRT(effectRT);
            }
        }
        RenderStats.EndPostFx();

        // ─── Transparents CB ───
        var transparentCmd = Graphics.GetCommandBuffer("Transparents");
        transparentCmd.SetRenderTarget(colorRT.frameBuffer);
        transparentCmd.SetViewport(0, 0, (uint)colorRT.Width, (uint)colorRT.Height);
        List<IRenderable> sortBackToFront = SortRenderables(renderables, culledRenderableIndices, css.CameraPosition, SortMode.BackToFront);
        DrawRenderables(transparentCmd, sortBackToFront, "RenderOrder", "Transparent", new ViewerData(css), null, false, colorRT);
        Graphics.Submit(transparentCmd);

        // World-space UI canvases (drawn with the camera matrices, into the scene color).
        RenderUIQueue(css, colorRT, UISurface.World, data);

        RenderStats.EndColorPass();

        // ─── MSAA resolve ───
        // Placed after the last geometry (transparents + world-space UI) so all of it is
        // antialiased, and before anything samples the scene color. Everything downstream
        // post-process effects, gizmos, the final blit then sees an ordinary single-sampled
        // target and behaves exactly as it does with MSAA off.
        if (msaa)
        {
            RenderTexture resolved = RenderTexture.GetTemporaryRT((int)css.PixelWidth, (int)css.PixelHeight, true, [colorFormat]);
            using (var resolveCmd = Graphics.GetCommandBuffer("MSAAResolve"))
            {
                resolveCmd.ResolveMultisample(colorRT, resolved);
                Graphics.Submit(resolveCmd);
            }
            RenderTexture.ReleaseTemporaryRT(colorRT);
            colorRT = resolved;
        }

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
        Graphics.Submit(finalCmd);

        // ─── Screen-space UI (Overlay surface) on top of the final image (into target) ───
        RenderUIQueue(css, target, UISurface.Overlay, data);

        // Reset to backbuffer for whatever runs after the pipeline (Paper UI, etc.). MUST run after the
        // overlay pass, which binds `target` - otherwise the editor's UI draws into the game RT and the
        // window goes black.
        var resetCmd = Graphics.GetCommandBuffer("PipelineReset");
        resetCmd.SetRenderTarget(null);
        resetCmd.SetViewport(0, 0, (uint)Window.InternalWindow.FramebufferSize.X, (uint)Window.InternalWindow.FramebufferSize.Y);
        Graphics.Submit(resetCmd);

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

    // ─────────────────────── UI ───────────────────────

    private static readonly List<IRenderable> s_uiTmp = new(64);

    /// <summary>
    /// Draws the screen-space UI for one surface (Camera or Overlay) of a camera. Collects the scene's
    /// UI render items, sorts them by canvas/hierarchy order, sets an orthographic screen projection, and
    /// draws each item's baked mesh with its material's UI pass through a command buffer. World-space
    /// canvases are not handled here (they render alongside the scene transparents).
    /// </summary>
    private void RenderUIQueue(CameraSnapshot css, RenderTexture? targetRT, UISurface surface, in RenderingData data)
    {
        if (surface == UISurface.World) { RenderUIWorld(css, targetRT, data); return; }
        // Screen-space UI is game-view only. In the scene view every canvas is drawn world-space instead
        // (see RenderUIWorld), so it can be seen and edited in 3D.
        if (data.SkipUI || data.IsSceneView) return;

        // Tell every screen-space GameCanvas the surface size so its design-pixel layout matches the
        // orthographic projection built below; canvases rebuild themselves when this changes.
        Float2? prevOverride = GameCanvas.ScreenSizeOverride;
        GameCanvas.ScreenSizeOverride = new Float2(css.PixelWidth, css.PixelHeight);
        try
        {
            s_uiTmp.Clear();
            UIRenderTree.CollectFor(css.Scene, surface, s_uiTmp);
            if (s_uiTmp.Count == 0) return;

            // Items arrive in per-canvas hierarchy order; stable-sort by SortKey across canvases.
            s_uiTmp.Sort(static (a, b) => ((UIRenderItem)a).SortKey.CompareTo(((UIRenderItem)b).SortKey));

            // Screen-space orthographic projection (origin bottom-left, +Y up to match RectTransform).
            AssignCameraMatrices(Float4x4.Identity, BuildScreenOrtho(css));

            var cmd = Graphics.GetCommandBuffer("UI");
            if (targetRT != null)
            {
                cmd.SetRenderTarget(targetRT.frameBuffer);
                cmd.SetViewport(0, 0, (uint)targetRT.Width, (uint)targetRT.Height);
            }
            else
            {
                cmd.SetRenderTarget(null);
                cmd.SetViewport(0, 0, (uint)Window.InternalWindow.FramebufferSize.X, (uint)Window.InternalWindow.FramebufferSize.Y);
            }

            DrawUIItems(cmd, s_uiTmp, new ViewerData(css));
            Graphics.Submit(cmd);

            // Restore the camera's matrices for anything that runs after this pass.
            AssignCameraMatrices(css.View, css.Projection);
        }
        finally
        {
            GameCanvas.ScreenSizeOverride = prevOverride;
        }
    }

    /// <summary>
    /// Draws world-space UI canvases. Their render items carry world-space model matrices, so they use
    /// the camera's own view/projection (already current after the transparents pass) and render into the
    /// scene color. Shown in the scene view too, since they live in the world.
    /// </summary>
    private void RenderUIWorld(CameraSnapshot css, RenderTexture? targetRT, in RenderingData data)
    {
        // In the scene view, screen-space canvases lay out against the viewport size so their world rect
        // matches the UISceneEditor handles (which push the same override while editing).
        Float2? prevOverride = GameCanvas.ScreenSizeOverride;
        if (data.IsSceneView)
            GameCanvas.ScreenSizeOverride = new Float2(css.PixelWidth, css.PixelHeight);
        try
        {
            s_uiTmp.Clear();
            UIRenderTree.CollectFor(css.Scene, UISurface.World, s_uiTmp);
            if (data.IsSceneView)
            {
                // The scene view shows every canvas in world space, regardless of RenderMode.
                UIRenderTree.CollectFor(css.Scene, UISurface.Overlay, s_uiTmp);
            }
            if (s_uiTmp.Count == 0) return;

            s_uiTmp.Sort(static (a, b) => ((UIRenderItem)a).SortKey.CompareTo(((UIRenderItem)b).SortKey));

            var cmd = Graphics.GetCommandBuffer("UIWorld");
            if (targetRT != null)
            {
                cmd.SetRenderTarget(targetRT.frameBuffer);
                cmd.SetViewport(0, 0, (uint)targetRT.Width, (uint)targetRT.Height);
            }
            DrawUIItems(cmd, s_uiTmp, new ViewerData(css));
            Graphics.Submit(cmd);
        }
        finally
        {
            GameCanvas.ScreenSizeOverride = prevOverride;
        }
    }

    private void DrawUIItems(CommandBuffer cmd, List<IRenderable> items, ViewerData viewer)
    {
        // GetPassesWithTag allocates a fresh list per call, and UI items are drawn back-to-back with the
        // same shared material - so memoize the resolved pass index for the last shader seen instead of
        // re-querying (and allocating) for every one of potentially hundreds of items each frame.
        Shader? cachedShader = null;
        int cachedPass = -1;

        for (int i = 0; i < items.Count; i++)
        {
            var item = (UIRenderItem)items[i];
            Material material = item.Material;
            if (material == null || material.Shader.IsNotValid()) continue;

            item.GetRenderingData(viewer, out PropertyState properties, out Mesh mesh, out Float4x4 model, out _);
            if (mesh == null || mesh.VertexCount <= 0) continue;

            // UI materials carry a single pass tagged RenderOrder=UI.
            int uiPass;
            if (ReferenceEquals(material.Shader, cachedShader))
                uiPass = cachedPass;
            else
            {
                var uiPasses = material.Shader.GetPassesWithTag("RenderOrder", "UI");
                uiPass = uiPasses.Count > 0 ? uiPasses[0] : -1;
                cachedShader = material.Shader;
                cachedPass = uiPass;
            }
            if (uiPass < 0) continue;

            // Clipping (RectMask) is done per-fragment in the shader via the item's clip uniforms, so
            // no GPU scissor here - that lets the clip follow rotation/scale and round its corners.
            cmd.DrawMesh(mesh, material, uiPass, model, properties);
        }

        // Leave the scissor test off so the next command buffer isn't clipped.
        cmd.DisableScissor();
    }

    private static Float4x4 BuildScreenOrtho(CameraSnapshot css)
        => Float4x4.CreateOrthoOffCenter(0, css.PixelWidth, 0, css.PixelHeight, -1000f, 1000f);

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
