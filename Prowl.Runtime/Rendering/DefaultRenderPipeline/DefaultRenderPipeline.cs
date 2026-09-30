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
/// Minimal placeholder pipeline: clears each camera's color/depth target and composites the optional
/// <see cref="UIRenderer"/> when presenting to the swapchain. It draws no scene geometry.
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

    /// <summary>Overrides the present pass. Must be assigned before the pipeline's first dispatch.</summary>
    public IPresentPass<CameraView>? Presenter { get; set; }

    /// <summary>Paper UI pass composited over swapchain presents. Must be assigned before the first dispatch.</summary>
    public PaperRenderer<CameraView>? UIRenderer { get; set; }

    protected override void InitializePasses()
    {
        AddPass(new ClearPass());

        if (UIRenderer != null)
            AddPass(UIRenderer);

        SetPresentPass(Presenter ?? new DefaultPresentPass(UIRenderer));
    }
}
