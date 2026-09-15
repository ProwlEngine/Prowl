// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Graphite;
using Prowl.Graphite.RenderGraph;

using Prowl.Runtime.Resources;
using Prowl.Vector;

using RenderTexture = Prowl.Runtime.Resources.RenderTexture;

namespace Prowl.Runtime.Rendering;

public struct RenderingData
{
    /// <summary>Whether to draw gizmos (editor scene view).</summary>
    public bool DisplayGizmos;

    /// <summary>Whether to draw the editor grid.</summary>
    public bool DisplayGrid;

    /// <summary>Whether the render is happening from the Scene View.</summary>
    public bool IsSceneView;

    public bool SkipUI;
}

public sealed class CameraView : IRenderView
{
    /// <summary>The camera being rendered.</summary>
    public Camera Camera;

    public int ViewId => Camera.InstanceID;

    /// <summary>Per-frame render flags (gizmos, grid, scene-view, etc.) for this camera.</summary>
    public RenderingData Data;

    /// <summary>
    /// Where the pipeline's default presenter (<see cref="DefaultPresentPass"/>) blits its final content.
    /// Null presents to the swapchain instead.
    /// </summary>
    public RenderTexture? Target;

    /// <summary>The scene gbuffer attachments for this view, filled by the first pass that resolves them.</summary>
    public SceneTargets Targets;

    /// <summary>
    /// The shader-side <c>Frame</c> parameter block (see ShaderVariables.slang) for this camera: view/projection
    /// matrices and their inverses, camera position and projection/screen params, merged with the frame-global
    /// <see cref="GlobalUniforms"/>. Built once in <see cref="From"/>; every pass that records camera-relative
    /// draws binds it with <c>cmd.SetProperties</c>.
    /// </summary>
    public PropertySet FrameProperties = new();

    public uint PixelWidth { get; set; }
    public uint PixelHeight { get; set; }

    /// <summary>Identifies this view to the profiler - the owning camera's GameObject name.</summary>
    public string Name { get; set; } = "";

    /// <summary>Sub-pixel jitter applied to the projection matrix by TAA. Zero when TAA is off.</summary>
    public Float2 Jitter;

    private Float2 _previousJitter;

    /// <summary>The camera's visible renderables and lights for this frame, rebuilt by <see cref="From"/>.</summary>
    public ViewCullResults Cull = new();

    private CameraView(Camera camera)
    {
        Camera = camera;
    }

    /// <summary>The persistent view owned by <paramref name="camera"/>, created on first use and reused every frame.</summary>
    public static CameraView GetOrCreate(Camera camera)
    {
        CameraView? view = camera.RenderView;
        if (view == null)
        {
            view = new CameraView(camera);
            camera.RenderView = view;
        }
        return view;
    }

    /// <summary>Builds the view for one camera's render: refreshes its per-frame pixel/projection data
    /// (<see cref="Camera.UpdateRenderData"/>), resolves its target and culls the camera's scene.</summary>
    public static CameraView From(Camera camera, in RenderingData data)
    {
        RenderTexture? target = camera.UpdateRenderData();

        CameraView view = GetOrCreate(camera);
        view.Data = data;
        view.Target = target;
        view.Targets = default;
        view.PixelWidth = camera.PixelWidth;
        view.PixelHeight = camera.PixelHeight;
        view.Name = camera.GameObject.Name;
        view.BuildFrameProperties();

        Scene? scene = camera.Scene;
        if (scene != null)
            ViewCuller.Cull(scene.Culler, camera, view.Cull);
        else
            view.Cull.Reset(0);
        return view;
    }

    private void BuildFrameProperties()
    {
        PropertySet props = FrameProperties;
        Float4x4 v = Camera.ViewMatrix;
        Float4x4 p = Camera.ProjectionMatrix;
        Float4x4 vp = p * v;
        Float4x4 nonJitteredVP = Camera.NonJitteredProjectionMatrix * v;

        props.SetMatrix("prowl_MatV", v);
        props.SetMatrix("prowl_MatIV", v.Invert());
        props.SetMatrix("prowl_MatP", p);
        props.SetMatrix("prowl_MatVP", vp);
        props.SetMatrix("prowl_MatIP", p.Invert());
        props.SetMatrix("prowl_MatIVP", vp.Invert());
        props.SetMatrix("prowl_MatVP_NonJittered", nonJitteredVP);
        props.SetMatrix("prowl_PrevViewProj", Camera.HasPreviousViewProjectionMatrix ? Camera.PreviousViewProjectionMatrix : nonJitteredVP);

        props.SetFloat3("_WorldSpaceCameraPos", Camera.Transform.Position);
        props.SetFloat4("_ProjectionParams", new Float4(1.0f, Camera.NearClipPlane, Camera.FarClipPlane, 1.0f / Camera.FarClipPlane));
        props.SetFloat4("_ScreenParams", new Float4(PixelWidth, PixelHeight, 1.0f + 1.0f / PixelWidth, 1.0f + 1.0f / PixelHeight));

        props.SetFloat2("_CameraJitter", Jitter);
        props.SetFloat2("_CameraPreviousJitter", _previousJitter);
        _previousJitter = Jitter;

        Scene? scene = Camera.Scene;
        if (scene == null)
        {
            props.SetFloat4("_FogColor", Float4.Zero);
            props.SetFloat4("_FogParams", Float4.Zero);
            props.SetFloat4("_FogStates", Float4.Zero);
            props.SetFloat4("_AmbientMode", Float4.Zero);
            props.SetFloat4("_AmbientColor", Float4.Zero);
            props.SetFloat4("_AmbientSkyColor", Float4.Zero);
            props.SetFloat4("_AmbientGroundColor", Float4.Zero);
            props.SetFloat4("_AmbientParams", Float4.Zero);
            props.SetFloat4("_ShadowFocusPos", new Float4(Camera.GetShadowFocusPosition(), 0.0f));

            GlobalUniforms.Update();
            props.ApplyOther(GlobalUniforms.Properties);
            return;
        }

        Scene.FogParams fog = scene.Fog;
        float fogRange = fog.End - fog.Start;
        if (Maths.Abs(fogRange) < 0.0001f)
            fogRange = 0.0001f;
        Float4 fogParams = new(
            fog.Density / 1.2011224f,
            fog.Density / 0.693147181f,
            -1.0f / fogRange,
            fog.End / fogRange);

        props.SetColor("_FogColor", fog.Color);
        props.SetFloat4("_FogParams", fogParams);
        props.SetFloat4("_FogStates", new Float4(
            fog.Mode == Scene.FogParams.FogMode.Linear ? 1.0f : 0.0f,
            fog.Mode == Scene.FogParams.FogMode.Exponential ? 1.0f : 0.0f,
            fog.Mode == Scene.FogParams.FogMode.ExponentialSquared ? 1.0f : 0.0f,
            0.0f));

        Scene.AmbientLightParams ambient = scene.Ambient;
        props.SetFloat4("_AmbientMode", new Float4(
            ambient.Mode == Scene.AmbientLightParams.AmbientMode.Uniform ? 1.0f : 0.0f,
            ambient.Mode == Scene.AmbientLightParams.AmbientMode.Hemisphere ? 1.0f : 0.0f,
            0.0f, 0.0f));
        props.SetFloat4("_AmbientColor", ambient.Color);
        props.SetFloat4("_AmbientSkyColor", ambient.SkyColor);
        props.SetFloat4("_AmbientGroundColor", ambient.GroundColor);
        props.SetFloat4("_AmbientParams", new Float4(ambient.Strength, 0.0f, 0.0f, 0.0f));

        props.SetFloat4("_ShadowFocusPos", new Float4(Camera.GetShadowFocusPosition(), 0.0f));

        GlobalUniforms.Update();
        props.ApplyOther(GlobalUniforms.Properties);
    }
}
