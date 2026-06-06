// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

using Prowl.Runtime.Rendering.Shaders;
using Prowl.Runtime.Resources;
using Prowl.Runtime.UI;
using Prowl.PaperUI;
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

    public uint PixelWidth;
    public uint PixelHeight;

    public Float4x4 ViewMatrix;
    public Float4x4 ProjectionMatrix;

    public ViewerData(DefaultRenderPipeline.CameraSnapshot css)
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
            s_skyDome = AssetImporting.Obj.ObjImporter.ParseMeshOnly(stream, "SkyDome");
        }

        // Pre-compute and upload BRDF integration LUT for PBR
        BRDFLutGenerator.UploadGlobal();
    }

    #endregion

    private static readonly List<IRenderable> s_uiTmp = new(64);

    private void RenderUIQueue(Camera cam, CameraSnapshot css, RenderTexture? colorRT, UISurface surface, in RenderingData data)
    {
        if (data.SkipUI && surface != UISurface.World) return;
        // Tell every screen-space GameCanvas the size of the surface it is being drawn onto.
        // This MUST match the orthographic projection built by BuildScreenOrtho below — otherwise
        // canvas-design-pixel layout (computed from this size) will not align with the projection
        // and the UI will be off-center / mis-scaled. The canvas will rebuild itself when this
        // value differs from the last rebuild's size.
        Float2? prevOverride = GameCanvas.ScreenSizeOverride;
        GameCanvas.ScreenSizeOverride = new Float2(css.PixelWidth, css.PixelHeight);
        try
        {
            s_uiTmp.Clear();
            UIRenderTree.CollectFor(css.Scene, surface, s_uiTmp);
            if (s_uiTmp.Count == 0) return;

            // Items already in hierarchical order per-canvas. Stable-sort by SortKey across canvases.
            s_uiTmp.Sort((a, b) => ((UIRenderItem)a).SortKey.CompareTo(((UIRenderItem)b).SortKey));

            if (!data.IsSceneView)
            {
                switch (surface)
                {
                    case UISurface.Camera:
                        Graphics.BindFramebuffer(colorRT!.frameBuffer);
                        AssignCameraMatrices(Float4x4.Identity, BuildScreenOrtho(css));
                        break;
                    case UISurface.Overlay:
                        if (colorRT != null)
                        {
                            // Editor
                            Graphics.BindFramebuffer(colorRT.frameBuffer);
                            AssignCameraMatrices(Float4x4.Identity, BuildScreenOrtho(css));
                        }
                        else
                        {
                            // Build / fullscreen
                            Graphics.UnbindFramebuffer();
                            uint fbW = (uint)Window.InternalWindow.FramebufferSize.X;
                            uint fbH = (uint)Window.InternalWindow.FramebufferSize.Y;
                            Graphics.Viewport(0, 0, fbW, fbH);
                            AssignCameraMatrices(
                                Float4x4.Identity,
                                // +Y up: bottom=0, top=fbH (canvas-design pixel Y grows upward).
                                Float4x4.CreateOrthoOffCenter(0, fbW, 0, fbH, -1000f, 1000f));
                        }

                        break;
                    case UISurface.World:
                        return; // world items go through the UI stage right after transparents.
                }
            }

            DrawUIItems(s_uiTmp, new ViewerData(css));

            Graphics.DisableScissor();

            if (!data.IsSceneView)
            {
                if (surface != UISurface.World)
                    AssignCameraMatrices(css.View, css.Projection); // restore for subsequent stages
            }
        }
        finally
        {
            GameCanvas.ScreenSizeOverride = prevOverride;
        }
    }

    private void DrawUIItems(List<IRenderable> items, ViewerData viewer)
    {
        if (items.Count == 0) return;

        // Track previous shader/material so we don't re-bind on every item when neighbours share them.
        // Materials *can* repeat across UI items (default GameUI), and per-frame uniforms are cheap to skip.
        Material? boundMaterial = null;
        GraphicsProgram? boundVariant = null;
        ShaderPass? boundPass = null;
        int materialTexSlot = 0;

        for (int i = 0; i < items.Count; i++)
        {
            var item = (UIRenderItem)items[i];
            Material material = item.Material;
            if (material == null || material.Shader.IsNotValid()) continue;

            item.GetRenderingData(viewer, out PropertyState properties, out Mesh mesh, out Float4x4 model, out _);
            if (mesh == null || mesh.VertexCount <= 0) continue;

            // Find the UI pass for this material. UI materials are expected to have a single matching pass.
            ShaderPass? pass = null;
            int passIndex = -1;
            foreach (ShaderPass p in material.Shader.Passes)
            {
                passIndex++;
                if (p.HasTag("RenderOrder", "UI")) { pass = p; break; }
            }
            if (pass == null) continue;

            // ---------- Material / shader binding (when changed) ----------
            if (!ReferenceEquals(material, boundMaterial) || !ReferenceEquals(pass, boundPass))
            {
                // UI meshes have a consistent vertex layout (pos + uv + color32); enabling these keywords
                // unconditionally is correct for every current UI behaviour and avoids per-item keyword churn.
                material.SetKeyword("HAS_NORMALS", mesh.HasNormals);
                material.SetKeyword("HAS_TANGENTS", mesh.HasTangents);
                material.SetKeyword("HAS_UV", mesh.HasUV);
                material.SetKeyword("HAS_UV2", mesh.HasUV2);
                material.SetKeyword("HAS_COLORS", mesh.HasColors || mesh.HasColors32);
                material.SetKeyword("HAS_BONEINDICES", false);
                material.SetKeyword("HAS_BONEWEIGHTS", false);
                material.SetKeyword("SKINNED", false);

                if (!pass.TryGetVariantProgram(material._localKeywords, out GraphicsProgram? variant) || variant == null)
                    continue;
                boundVariant = variant;
                boundMaterial = material;
                boundPass = pass;

                GraphicsBuffer? globals = GlobalUniforms.GetBuffer();
                if (globals != null)
                    Graphics.BindUniformBuffer(boundVariant, "GlobalUniforms", globals, 0);

                materialTexSlot = 0;
                PropertyState.ApplyGlobals(boundVariant, boundVariant.uniformCache, ref materialTexSlot);
                PropertyState.ApplyMaterialUniformsWithDefaults(material._properties, material.Shader!, boundVariant, ref materialTexSlot);

                // pass.State is the material's intended depth/blend/cull baseline. Stencil bits in
                // pass.State are ignored — we override below per item.
                Graphics.SetState(pass.State);
            }

            // ---------- Per-item scissor ----------
            if (item.ScissorPixels is { } sp)
            {
                int x = (int)MathF.Floor(sp.X);
                int y = (int)MathF.Floor(sp.Y);
                int w = Math.Max(0, (int)MathF.Ceiling(sp.X + sp.Z) - x);
                int h = Math.Max(0, (int)MathF.Ceiling(sp.Y + sp.W) - y);
                Graphics.SetScissor(x, y, (uint)w, (uint)h);
            }
            else
            {
                Graphics.DisableScissor();
            }

            // ---------- Per-item uniforms + draw ----------
            int instTexSlot = materialTexSlot;
            PropertyState.ApplyInstanceUniforms(properties, boundVariant!, ref instTexSlot);

            var fModel = (Float4x4)model;
            Graphics.SetUniformMatrix(boundVariant!, "prowl_ObjectToWorld", false, fModel);
            Graphics.SetUniformMatrix(boundVariant!, "prowl_WorldToObject", false, fModel.Invert());

            mesh.Upload();
            unsafe
            {
                Graphics.BindVertexArray(mesh.VertexArrayObject);
                Graphics.DrawIndexed(mesh.MeshTopology, (uint)mesh.IndexCount, mesh.IndexFormat == IndexFormat.UInt32, null);
                Graphics.BindVertexArray(null);
            }
            RenderStats.AddBatch();
        }
    }

    private static Float4x4 BuildScreenOrtho(CameraSnapshot css)
    {
        // +Y up: bottom=0, top=PixelHeight. The origin sits at the bottom-left of the
        // surface so canvas-design-pixel Y grows upward, matching RectTransform layout
        // (AnchoredPosition.Y+ moves up) and the world-space camera projection.
        return Float4x4.CreateOrthoOffCenter(0, css.PixelWidth, 0, css.PixelHeight, -1000f, 1000f);
    }

    #region Main Rendering

    public override void Render(Camera camera, in RenderingData data)
    {
        ValidateDefaults();

        // Reset GL rasterizer state to known defaults so this render pass
        // doesn't inherit stale state from a previous pipeline render.
        Graphics.ResetState();

        // Main rendering with correct order of operations
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

        // Gather DepthTextureMode requirements from all active effects
        DepthTextureMode depthMode = DepthTextureMode.None;
        foreach (var effect in allEffects)
            depthMode |= effect.RequiredDepthTextureMode;

        RenderTexture target = camera.UpdateRenderData();

        // =======================================================
        // 1. Pre Cull
        foreach (ImageEffect effect in allEffects)
            effect.OnPreCull(camera);

        // =======================================================
        // 2. Camera snapshot and global uniforms
        CameraSnapshot css = new(camera, depthMode);
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

        HashSet<int> culledRenderableIndices = CullRenderables(renderables, css.WorldFrustum, css.CullingMask);

        RenderStats.AddCamera();
        int collected = renderables.Count;
        int culled = culledRenderableIndices.Count;
        RenderStats.AddRenderables(collected, culled, collected - culled);

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

        Graphics.BindFramebuffer(ShadowAtlas.GetAtlas().frameBuffer);
        Graphics.Clear(0.0f, 0.0f, 0.0f, 1.0f, ClearFlags.Depth | ClearFlags.Stencil);
        RenderStats.BeginShadowPass();
        lightSystem.RenderShadows(this, css.CameraPosition, renderables);
        RenderStats.EndShadowPass();

        AssignCameraMatrices(css.View, css.Projection);
        lightSystem.UploadGlobalUniforms();

        // Upload fog parameters as globals for forward shaders
        UploadFogUniforms(css.Scene);
        UploadAmbientUniforms(css.Scene);

        // =======================================================
        // 7. Create main color render target.
        // Stencil is requested so UI Mask components can write/test against the buffer; we get an
        // 8-bit stencil slot for free as part of the packed Depth24Stencil8 format.
        RenderTexture colorRT = RenderTexture.GetTemporaryRT((int)css.PixelWidth, (int)css.PixelHeight, true, [
            isHDR ? TextureImageFormat.Short4 : TextureImageFormat.Color4b,
        ]);

        // =======================================================
        // 8. Depth + Normals pre-pass
        RenderTexture depthPrepass = RenderTexture.GetTemporaryRT((int)css.PixelWidth, (int)css.PixelHeight, true, [
            TextureImageFormat.Color4b, // View-space normals
        ]);

        Graphics.BindFramebuffer(depthPrepass.frameBuffer);
        Graphics.Clear(0.5f, 0.5f, 1.0f, 1.0f, ClearFlags.Color | ClearFlags.Depth);
        DrawRenderables(renderables, "LightMode", "DepthNormals", new ViewerData(css), culledRenderableIndices, false);

        // Set depth + normals as global textures for post-processing effects
        PropertyState.SetGlobalTexture("_CameraDepthTexture", depthPrepass.InternalDepth);
        PropertyState.SetGlobalTexture("_CameraNormalsTexture", depthPrepass.InternalTextures[0]);

        // Copy pre-pass depth to colorRT
        Graphics.BindFramebuffer(depthPrepass.frameBuffer, FBOTarget.Read);
        Graphics.BindFramebuffer(colorRT.frameBuffer, FBOTarget.Draw);
        Graphics.BlitFramebuffer(0, 0, depthPrepass.Width, depthPrepass.Height,
                                 0, 0, colorRT.Width, colorRT.Height,
                                 ClearFlags.Depth, BlitFilter.Nearest);

        // =======================================================
        // 9. Apply camera clear flags to colorRT
        Graphics.BindFramebuffer(colorRT.frameBuffer);

        switch (camera.ClearFlags)
        {
            case CameraClearFlags.Skybox:
            {
                var skyColor = css.Scene.Skybox.Mode == Scene.SkyboxMode.SolidColor
                    ? css.Scene.Skybox.SolidColor : camera.ClearColor;
                Graphics.Clear(
                    (float)skyColor.R, (float)skyColor.G,
                    (float)skyColor.B, (float)skyColor.A,
                    ClearFlags.Color);
                RenderSkybox(css, lights);
                break;
            }

            case CameraClearFlags.SolidColor:
                Graphics.Clear(
                    (float)camera.ClearColor.R, (float)camera.ClearColor.G,
                    (float)camera.ClearColor.B, (float)camera.ClearColor.A,
                    ClearFlags.Color);
                break;

            case CameraClearFlags.Depth:
                // Keep previous camera's color, depth already from pre-pass.
                // Copy color from target into colorRT to preserve it.
                if (target.IsValid())
                {
                    Graphics.BindFramebuffer(target.frameBuffer, FBOTarget.Read);
                    Graphics.BindFramebuffer(colorRT.frameBuffer, FBOTarget.Draw);
                    Graphics.BlitFramebuffer(0, 0, target.Width, target.Height,
                                             0, 0, colorRT.Width, colorRT.Height,
                                             ClearFlags.Color, BlitFilter.Nearest);
                    Graphics.BindFramebuffer(colorRT.frameBuffer);
                }
                break;

            case CameraClearFlags.Nothing:
                // Keep everything from the previous camera copy color from target.
                if (target.IsValid())
                {
                    Graphics.BindFramebuffer(target.frameBuffer, FBOTarget.Read);
                    Graphics.BindFramebuffer(colorRT.frameBuffer, FBOTarget.Draw);
                    Graphics.BlitFramebuffer(0, 0, target.Width, target.Height,
                                             0, 0, colorRT.Width, colorRT.Height,
                                             ClearFlags.Color, BlitFilter.Nearest);
                    Graphics.BindFramebuffer(colorRT.frameBuffer);
                }
                break;
        }

        // =======================================================
        // 10. Forward Opaque Rendering shaders do PBR lighting inline
        //     Depth test is LEqual against pre-pass depth (same geometry = equal depth = passes)
        DrawRenderables(renderables, "RenderOrder", "Opaque", new ViewerData(css), culledRenderableIndices, false);

        // =======================================================
        // 10b. Motion Vectors pass (when requested by DepthTextureMode)
        // Motion vectors must use UNJITTERED current and previous VP so they
        // represent real scene motion only, not sub-pixel jitter offsets.
        RenderTexture motionVectorsRT = null;
        if (css.DepthTextureMode.HasFlag(DepthTextureMode.MotionVectors))
        {
            motionVectorsRT = RenderTexture.GetTemporaryRT((int)css.PixelWidth, (int)css.PixelHeight, true, [
                TextureImageFormat.Short2, // RG16F for per-pixel motion vectors
            ]);

            // Copy depth prepass into MV RT so ZTest LEqual rejects occluded fragments
            Graphics.BindFramebuffer(depthPrepass.frameBuffer, FBOTarget.Read);
            Graphics.BindFramebuffer(motionVectorsRT.frameBuffer, FBOTarget.Draw);
            Graphics.BlitFramebuffer(0, 0, depthPrepass.Width, depthPrepass.Height,
                                     0, 0, motionVectorsRT.Width, motionVectorsRT.Height,
                                     ClearFlags.Depth, BlitFilter.Nearest);

            // Use the camera's NonJitteredProjectionMatrix for motion vectors
            Float4x4 prevVP = css.HasPreviousViewProj
                ? css.PreviousViewProj
                : css.NonJitteredProjection * css.View; // First frame fallback: no motion

            // Upload unjittered current VP and previous VP
            AssignCameraMatrices(css.View, css.NonJitteredProjection);
            GlobalUniforms.SetPrevViewProj(prevVP);
            GlobalUniforms.Upload();

            Graphics.BindFramebuffer(motionVectorsRT.frameBuffer);
            Graphics.Clear(0.0f, 0.0f, 0.0f, 0.0f, ClearFlags.Color);

            // Draw per-object motion vectors (uses previous model matrices)
            DrawRenderables(renderables, "LightMode", "MotionVectors", new ViewerData(css), culledRenderableIndices, true);

            PropertyState.SetGlobalTexture("_CameraMotionVectorsTexture", motionVectorsRT.MainTexture);

            // Restore jittered matrices for subsequent passes
            AssignCameraMatrices(css.View, css.Projection);

            // Restore color RT for subsequent rendering
            Graphics.BindFramebuffer(colorRT.frameBuffer);
        }

        // =======================================================
        // 11. AfterOpaques effects (GTAO, SSR, etc.)
        if (effectsByStage[RenderStage.AfterOpaques].Count > 0)
        {
            var afterContext = new RenderContext
            {
                DepthNormals = depthPrepass,
                MotionVectors = motionVectorsRT,
                SceneColor = colorRT,
                Camera = camera,
                Width = (int)css.PixelWidth,
                Height = (int)css.PixelHeight,
                CurrentStage = RenderStage.AfterOpaques
            };
            ExecuteImageEffects(afterContext, effectsByStage[RenderStage.AfterOpaques]);
        }

        // =======================================================
        // 13. Transparent geometry (forward, sorted back-to-front)
        Graphics.BindFramebuffer(colorRT.frameBuffer);
        List<IRenderable> sortBackToFront = SortRenderables(renderables, culledRenderableIndices, css.CameraPosition, SortMode.BackToFront);
        DrawRenderables(sortBackToFront, "RenderOrder", "Transparent", new ViewerData(css), null, false);
        DrawRenderables(sortBackToFront, "RenderOrder", "UI", new ViewerData(css), null, false); // world-space UI

        // =======================================================
        // 13b. ScreenSpaceCamera UI — drawn before PostProcess so post-FX (bloom, tonemap, ...) composite over UI
        RenderUIQueue(camera, css, colorRT, UISurface.Camera, data);

        // =======================================================
        // 14. PostProcess effects
        if (effectsByStage[RenderStage.PostProcess].Count > 0)
        {
            var postContext = new RenderContext
            {
                DepthNormals = depthPrepass,
                MotionVectors = motionVectorsRT,
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

        if (data.IsSceneView)
        {
            RenderUIQueue(camera, css, target, UISurface.Overlay, data); // 16b (NEW)
        }

        // =======================================================
        // 15. Gizmos
        if (data.DisplayGizmos)
        {
            Graphics.BindFramebuffer(colorRT.frameBuffer);
            RenderGizmos(css);
        }

        // =======================================================
        // 16. Final blit to target (null = screen)
        Blit(colorRT, target, null, 0, false, false);

        if (!data.IsSceneView)
        {
            RenderUIQueue(camera, css, target, UISurface.Overlay, data); // 16b (NEW)
        }

        // =======================================================
        // 17. Save previous VP for next frame's motion vectors (before OnPostRender resets jitter)
        camera.SavePreviousViewProjectionMatrix();

        // =======================================================
        // 18. Post Render
        foreach (ImageEffect effect in allEffects)
            effect.OnPostRender(camera);


        // =======================================================
        // 18. Cleanup
        RenderTexture.ReleaseTemporaryRT(depthPrepass);
        RenderTexture.ReleaseTemporaryRT(colorRT);
        if (motionVectorsRT != null)
            RenderTexture.ReleaseTemporaryRT(motionVectorsRT);

        Graphics.UnbindFramebuffer();
        Graphics.Viewport(0, 0, (uint)Window.InternalWindow.FramebufferSize.X, (uint)Window.InternalWindow.FramebufferSize.Y);
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

    private void RenderSkybox(CameraSnapshot css, List<IRenderableLight> lights)
    {
        var skyParams = css.Scene.Skybox;

        switch (skyParams.Mode)
        {
            case Scene.SkyboxMode.Procedural:
            {
                var sun = lights.FirstOrDefault(l => l is IRenderableLight rl && rl.GetLightType() == LightType.Directional);
                // Default to a pleasant angled sun direction when no directional light exists
                var sunDir = sun != null ? sun.GetLightDirection() : Float3.Normalize(new Float3(0.5f, -0.7f, 0.5f));
                s_skybox.SetVector("_SunDir", sunDir);
                DrawMeshNow(s_skyDome, s_skybox);
                break;
            }

            case Scene.SkyboxMode.SolidColor:
                // Camera clear already fills with color just need to write unlit to GBuffer
                // The camera clear handles the solid color via ClearColor override
                break;

            case Scene.SkyboxMode.Gradient:
            {
                s_gradientSkybox ??= new Material(Shader.LoadDefault(DefaultShader.GradientSkybox));
                s_gradientSkybox.SetColor("_TopColor", skyParams.GradientTop);
                s_gradientSkybox.SetColor("_BottomColor", skyParams.GradientBottom);
                s_gradientSkybox.SetFloat("_Exponent", skyParams.GradientExponent);
                DrawMeshNow(s_skyDome, s_gradientSkybox);
                break;
            }

            case Scene.SkyboxMode.Material:
            {
                var customMat = skyParams.CustomMaterial.Res;
                if (customMat != null)
                    DrawMeshNow(s_skyDome, customMat);
                else
                    DrawMeshNow(s_skyDome, s_skybox); // Fallback to procedural
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

    private void RenderGizmos(CameraSnapshot css)
    {
        Float4x4 vp = css.Projection * css.View;
        (Mesh? wire, Mesh? solid) = Debug.GetGizmoDrawData();

        if (wire.IsValid() || solid.IsValid())
        {
            if (wire.IsValid()) DrawMeshNow(wire, s_gizmo);
            if (solid.IsValid()) DrawMeshNow(solid, s_gizmo);
        }

        // Gizmo Icons - billboarded textured quads
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
                DrawMeshNow(s_iconQuad, s_iconMaterial);
            }
        }
    }

    #endregion
}
