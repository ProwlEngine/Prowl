// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Graphite;
using Prowl.Graphite.RenderGraph;
using Prowl.Graphite.ShaderDef;

using Prowl.Runtime.GUI;
using Prowl.Runtime.Resources;
using Prowl.Vector;


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

    public ViewerData(Camera camera) : this()
    {
        Position = camera.Transform.Position;
        Forward = camera.Transform.Forward;
        Right = camera.Transform.Right;
        Up = camera.Transform.Up;
        PixelWidth = camera.PixelWidth;
        PixelHeight = camera.PixelHeight;
        ViewMatrix = camera.ViewMatrix;
        ProjectionMatrix = camera.ProjectionMatrix;
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
/// Default graph-driven pipeline on Graphite's <see cref="RenderPipeline{TView}"/>. Passes share one scene gbuffer
/// (see <see cref="SceneResources"/>); presentation is pluggable via <see cref="Presenter"/>, read once on first dispatch.
/// </summary>
public class DefaultRenderPipeline : RenderPipeline<CameraView>
{
    private static Shader? s_blitShader;
    private static Material? s_blitMaterial;

    /// <summary>Default material used by <c>cmd.Blit</c> when no material is supplied.
    /// Lazy-loaded on first call.</summary>
    public static Material GetBlitMaterial()
    {
        if (s_blitShader.IsNotValid())
            s_blitShader = Shader.LoadDefault(DefaultShader.Blit);
        if (s_blitMaterial.IsNotValid())
            s_blitMaterial = new Material(s_blitShader);
        return s_blitMaterial!;
    }

    /// <summary>
    /// Overrides the pipeline's present pass. Must be assigned before this pipeline's first dispatch -
    /// <see cref="InitializePasses"/> runs once, lazily, on first use. Null uses the runtime-provided
    /// default (<see cref="DefaultPresentPass"/>).
    /// </summary>
    public IPresentPass<CameraView>? Presenter { get; set; }

    /// <summary>
    /// Optional Paper/Quill UI pass, injected by a caller (e.g. <see cref="Game"/>) that wants its UI
    /// drawn as part of this pipeline's own graph instead of dispatching a separate standalone pipeline.
    /// Must be assigned before this pipeline's first dispatch - <see cref="InitializePasses"/> runs once,
    /// lazily, on first use. When set, <see cref="DefaultPresentPass"/> composites it over the final
    /// content only when presenting to the swapchain (a camera with an explicit <see cref="CameraView.Target"/>
    /// never gets UI composited in - offscreen/editor renders skip it). Null draws no UI (the pipeline's
    /// existing behavior).
    /// </summary>
    public PaperRenderer<CameraView>? UIRenderer { get; set; }

    protected override void InitializePasses()
    {
        DeclareTexture(SceneResources.GBuffer, SceneResources.GBufferDesc());

        AddPass(new ShadowsPass());
        AddPass(new OpaquePass());
        AddPass(new DepthCopyPass());
        AddPass(new GizmoPass());
        AddPass(new TransparentsPass());
        AddPass(new VolumetricsPass());
        AddPass(new PostProcessingPass());
        AddPass(new FinalBlitPass());

        if (UIRenderer != null)
            AddPass(UIRenderer);

        SetPresentPass(Presenter ?? new DefaultPresentPass(UIRenderer));
    }
}
