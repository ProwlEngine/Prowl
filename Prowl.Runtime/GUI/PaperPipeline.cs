// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Graphite;
using Prowl.Graphite.RenderGraph;
using Prowl.Quill;
using Prowl.Runtime.Rendering;
using Prowl.Vector;

namespace Prowl.Runtime.GUI;

/// <summary>
/// The <see cref="IRenderView"/> <see cref="PaperPipeline"/> dispatches with, one per
/// <see cref="PaperPipeline.Execute"/> call. Just a pixel size - all of Paper's actual state (pending
/// canvas/draw calls, scene texture) lives on the wrapped <see cref="GUI.PaperRenderer{TView}"/>.
/// </summary>
public sealed class PaperView : IRenderView
{
    public uint PixelWidth { get; set; }
    public uint PixelHeight { get; set; }
    public Framebuffer? TargetFramebuffer { get; set; }
    public bool TargetSwapchain { get; set; }
    public string Name => "UI";
    public int ViewId => 0;
}

/// <summary>
/// <see cref="PaperPipeline"/>'s final pass: reads the scene texture <see cref="GUI.PaperRenderer{TView}"/>
/// declared and composites it into the view's target.
/// </summary>
internal sealed class PaperPresentPass : IPass<PaperView>
{
    private readonly PaperRenderer<PaperView> _paper;

    private TextureHandle _sceneHandle;
    private TextureHandle _targetHandle;

    public PaperPresentPass(PaperRenderer<PaperView> paper)
    {
        _paper = paper;
    }

    public string Name => "Paper Present";

    public void Setup(RenderContextBuilder builder)
    {
        _sceneHandle = builder.DeclareInputTexture(_paper.SceneResourceId);
        _targetHandle = builder.DeclareViewTarget();
    }

    public void Render(RenderContext<PaperView> context, CommandBuffer cmd)
    {
        RenderTexture sceneRT = context.GetRenderTexture(_sceneHandle);
        Framebuffer target = context.GetRenderTexture(_targetHandle).Framebuffer;

        if (context.View.TargetSwapchain)
        {
            cmd.SetFramebuffer(target);
            cmd.ClearColorTarget(0, new Color(0f, 0f, 0f, 1f));
        }

        _paper.CompositeInto(cmd, sceneRT.ColorTextures[0], target);
    }
}

/// <summary>
/// Default standalone pipeline for Paper/Quill UI, for callers with no existing render pipeline to add a
/// <see cref="GUI.PaperRenderer{TView}"/> pass into (editor UI, the pre-render project launcher). Owns
/// one and forwards <see cref="ICanvasRenderer"/> to it, so a <see cref="PaperPipeline"/> instance can be
/// passed straight to <c>new Paper(pipeline, ...)</c>. Call <see cref="Execute"/> once per frame, after
/// Paper's frame has ended, to actually dispatch and draw the calls
/// <see cref="GUI.PaperRenderer{TView}.RenderCalls"/> stashed - <c>RenderCalls</c> no longer dispatches on
/// its own.
/// </summary>
public sealed class PaperPipeline : RenderPipeline<PaperView>, ICanvasRenderer
{
    private readonly PaperRenderer<PaperView> _paper = new();

    /// <summary>Where the present pass composites Paper's UI. Null presents to the swapchain.</summary>
    public Framebuffer? PresentTarget { get; set; }

    public bool SupportsBackdropBlur => _paper.SupportsBackdropBlur;

    public void Initialize(int width, int height) => _paper.Initialize(width, height);

    public void UpdateProjection(int width, int height) => _paper.UpdateProjection(width, height);

    public object CreateTexture(uint width, uint height) => _paper.CreateTexture(width, height);

    public Int2 GetTextureSize(object texture) => _paper.GetTextureSize(texture);

    public void SetTextureData(object texture, IntRect bounds, byte[] data) => _paper.SetTextureData(texture, bounds, data);

    public void RenderCalls(Canvas canvas, IReadOnlyList<DrawCall> drawCalls) => _paper.RenderCalls(canvas, drawCalls);

    protected override void InitializePasses()
    {
        AddPass(_paper);
        AddPass(new PaperPresentPass(_paper));
    }

    /// <summary>Dispatches the graph, running the draw calls <see cref="PaperRenderer{TView}.RenderCalls"/> stashed.</summary>
    public void Execute()
    {
        var view = new PaperView
        {
            PixelWidth = (uint)_paper.PixelWidth,
            PixelHeight = (uint)_paper.PixelHeight,
            TargetFramebuffer = PresentTarget,
            TargetSwapchain = PresentTarget == null,
        };

        Graphics.Device.DispatchGraph(this, [view]);
    }

    public void Cleanup() => _paper.Cleanup();

    public override void Dispose()
    {
        _paper.Dispose();
        base.Dispose();
    }
}
