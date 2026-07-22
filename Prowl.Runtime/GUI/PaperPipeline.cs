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
}

/// <summary>
/// <see cref="PaperPipeline"/>'s present pass: reads the scene texture <see cref="GUI.PaperRenderer{TView}"/>
/// declared (as a graph input, not a stored field) and composites it into
/// <see cref="PaperPipeline.PresentTarget"/>, or the swapchain when that is null.
/// </summary>
internal sealed class PaperPresentPass : IPresentPass<PaperView>
{
    private readonly PaperRenderer<PaperView> _paper;
    private readonly Func<Framebuffer?> _target;

    private TextureHandle _sceneHandle;

    public PaperPresentPass(PaperRenderer<PaperView> paper, Func<Framebuffer?> target)
    {
        _paper = paper;
        _target = target;
    }

    public string Name => "Paper Present";

    public void Setup(PresentContextBuilder builder)
    {
        _sceneHandle = builder.GetInputTexture(_paper.SceneResourceId);
        builder.RequestSwapchain();
    }

    public void Present(RenderContext<PaperView> context)
    {
        RenderTexture sceneRT = context.GetRenderTexture(_sceneHandle);

        Framebuffer? target = _target();
        if (target != null)
        {
            CommandBuffer copyCmd = context.GetCommandBuffer(Name);
            _paper.CompositeInto(copyCmd, sceneRT.ColorTextures[0], target);
            context.SubmitCommandBuffer(copyCmd);
            return;
        }

        Framebuffer? swap = context.SwapchainTarget;
        if (swap == null)
            return;

        CommandBuffer cmd = context.GetCommandBuffer(Name);
        _paper.CompositeInto(cmd, sceneRT.ColorTextures[0], swap);
        context.SubmitCommandBuffer(cmd);
        context.Present();
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
        SetPresentPass(new PaperPresentPass(_paper, () => PresentTarget));
    }

    /// <summary>Dispatches the graph, running the draw calls <see cref="PaperRenderer{TView}.RenderCalls"/> stashed.</summary>
    public void Execute()
    {
        var view = new PaperView
        {
            PixelWidth = (uint)_paper.PixelWidth,
            PixelHeight = (uint)_paper.PixelHeight,
        };

        RenderProfilerHooks.Sink?.BeginView("UI");
        Graphics.Device.DispatchGraph(this, [view]);
        RenderProfilerHooks.Sink?.EndView();
    }

    public void Cleanup() => _paper.Cleanup();

    public override void Dispose()
    {
        _paper.Dispose();
        base.Dispose();
    }
}
