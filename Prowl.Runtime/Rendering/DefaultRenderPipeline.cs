// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

using Prowl.Graphite;
using Prowl.Graphite.ShaderDef;
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

    public ViewerData(RenderPipeline.CameraSnapshot css) : this()
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
            s_skyDome = skyImport.Meshes.Count > 0 ? skyImport.Meshes[0] : new Mesh { Name = "SkyDome" };
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

        GlobalPropertySet.ClearGlobals();

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
        RenderTexture? target = camera.UpdateRenderData();
        if (!target.IsValid())
            return;

        CameraSnapshot css = new(camera);
        SetupGlobalUniforms(css);
        AssignCameraMatrices(css.View, css.Projection);

        var (renderables, lights) = CollectRenderables(camera.GameObject.Scene, camera);

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

        // Reconcile picks shadow casters and refits the light BVHs, and UploadGlobalUniforms
        // pushes the BVH textures + directional/local light uniforms the opaque shaders read.
        // The shadow atlas itself is not rendered yet (RenderShadows is a no-op for now), so
        // shadow-mapped lights fall back to their unshadowed contribution.
        SceneLightSystem lightSystem = GetOrCreateLightSystem(css.Scene);
        lightSystem.Reconcile(lights, css.CameraPosition, css.CullingMask);
        lightSystem.UploadGlobalUniforms();

        UploadFogUniforms(css.Scene);
        UploadAmbientUniforms(css.Scene);

        RenderTexture colorRT = RenderTexture.GetTemporaryRT((int)css.PixelWidth, (int)css.PixelHeight, true, [
            camera.HDR ? PixelFormat.R16_G16_B16_A16_Float : PixelFormat.R8_G8_B8_A8_UNorm,
        ]);

        CommandBuffer cmd = Graphics.GetCommandBuffer("ColorPass");

        cmd.SetRenderTarget(colorRT.frameBuffer);
        cmd.SetViewport(0, 0, (uint)colorRT.Width, (uint)colorRT.Height);
        cmd.ClearRenderTarget(true, true, camera.ClearColor);

        RenderSkybox(cmd, css, lights);

        RenderStats.BeginColorPass();
        DrawRenderables(cmd, renderables, "RenderOrder", "Opaque", new ViewerData(css), culledRenderableIndices, colorRT);
        RenderStats.EndColorPass();

        if (data.DisplayGrid)
            RenderGrid(cmd, css, colorRT);

        if (data.DisplayGizmos)
            RenderGizmos(cmd, css, colorRT);

        Graphics.Submit(cmd);

        var blitCmd = Graphics.GetCommandBuffer("FinalBlit");
        blitCmd.Blit(colorRT, target, null, 0, false, false);
        Graphics.Submit(blitCmd);

        RenderTexture.ReleaseTemporaryRT(colorRT);

        camera.SavePreviousViewProjectionMatrix();
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

        GlobalPropertySet.SetColor("_FogColor", fog.Color);
        GlobalPropertySet.SetVector("_FogParams", fogParams);
        GlobalPropertySet.SetVector("_FogStates", new Float3(
            fog.Mode == Scene.FogParams.FogMode.Linear ? 1 : 0,
            fog.Mode == Scene.FogParams.FogMode.Exponential ? 1 : 0,
            fog.Mode == Scene.FogParams.FogMode.ExponentialSquared ? 1 : 0
        ));
    }

    private static void UploadAmbientUniforms(Scene scene)
    {
        Scene.AmbientLightParams ambient = scene.Ambient;
        GlobalPropertySet.SetVector("_AmbientMode", new Float2(
            ambient.Mode == Scene.AmbientLightParams.AmbientMode.Uniform ? 1 : 0,
            ambient.Mode == Scene.AmbientLightParams.AmbientMode.Hemisphere ? 1 : 0
        ));
        GlobalPropertySet.SetColor("_AmbientColor", ambient.Color);
        GlobalPropertySet.SetColor("_AmbientSkyColor", ambient.SkyColor);
        GlobalPropertySet.SetColor("_AmbientGroundColor", ambient.GroundColor);
        GlobalPropertySet.SetFloat("_AmbientStrength", (float)ambient.Strength);
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

            item.GetRenderingData(viewer, out PropertySet properties, out Mesh mesh, out Float4x4 model, out _);
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
        cmd.SetFullScissorRects();
    }

    private static Float4x4 BuildScreenOrtho(CameraSnapshot css)
        => Float4x4.CreateOrthoOffCenter(0, css.PixelWidth, 0, css.PixelHeight, -1000f, 1000f);

    private void RenderSkybox(CommandBuffer cmd, CameraSnapshot css, List<IRenderableLight> lights)
    {
        GlobalPropertySet.Apply(cmd);

        var skyParams = css.Scene.Skybox;

        switch (skyParams.Mode)
        {
            case Scene.SkyboxMode.Procedural:
                {
                    var sun = lights.FirstOrDefault(l => l is IRenderableLight rl && rl.GetLightType() == LightType.Directional);
                    var sunDir = sun != null ? sun.GetLightDirection() : Float3.Normalize(new Float3(0.5f, 0.7f, 0.5f));
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

    private void RenderGrid(CommandBuffer cmd, CameraSnapshot css, RenderTexture colorRT)
    {
        EnsureGridResources();
        if (s_gridMesh == null || s_gridMaterial == null)
            return;

        GlobalPropertySet.Apply(cmd);

        s_gridMaterial.SetTexture("_CameraDepthTexture", colorRT.InternalDepth);

        float cx = MathF.Round(css.CameraPosition.X);
        float cz = MathF.Round(css.CameraPosition.Z);
        var gridTransform = Float4x4.CreateTranslation(new Float3(cx, 0, cz));
        cmd.DrawMesh(s_gridMesh, s_gridMaterial, 0, gridTransform, null);
    }

    private void RenderGizmos(CommandBuffer cmd, CameraSnapshot css, RenderTexture colorRT)
    {
        GlobalPropertySet.Apply(cmd);

        Float4x4 vp = css.Projection * css.View;
        (GizmoBuilder.Batch? wire, GizmoBuilder.Batch? solid) = Debug.UploadGizmos();

        // The Gizmos shader samples _CameraDepthTexture to dim lines occluded by opaque geometry.
        s_gizmo.SetTexture("_CameraDepthTexture", colorRT.InternalDepth);

        if (wire != null) DrawGizmoBatch(cmd, wire, s_gizmo);
        if (solid != null) DrawGizmoBatch(cmd, solid, s_gizmo);

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

    private static void DrawGizmoBatch(CommandBuffer cmd, IVertexSource source, Material material)
    {
        Prowl.Graphite.ShaderDef.ShaderPass? pass = material.Shader?.GetPass(0);
        if (pass == null)
            return;

        cmd.SetShader(pass);
        cmd.SetMaterialProperties(material);

        var transforms = new PropertySet();
        transforms.SetMatrix("prowl_ObjectToWorld", Float4x4.Identity);
        transforms.SetMatrix("prowl_WorldToObject", Float4x4.Identity);
        transforms.SetMatrix("prowl_PrevObjectToWorld", Float4x4.Identity);
        cmd.SetProperties(transforms);

        cmd.SetVertexSource(source);
        cmd.DrawIndexed();
    }

    #endregion
}
