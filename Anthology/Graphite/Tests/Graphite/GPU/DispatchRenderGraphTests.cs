#nullable enable

using System;
using System.Collections.Generic;

using Prowl.Graphite.RenderGraph;

using Xunit;

namespace Prowl.Graphite.Tests;

// Coverage for the high-level GraphicsDevice.DispatchRenderGraph entry point: one graph execution per
// dispatch, the pass loop running once per view against a fresh per-view context, the returned task
// completing, transient acquisition through the context surviving many dispatches, and the
// backbuffer declaration deciding whether the dispatch presents. The present path runs on the
// windowed creator; everything else runs headless.

file readonly struct DispatchView : IRenderView
{
    public DispatchView(uint width, uint height)
    {
        PixelWidth = width;
        PixelHeight = height;
    }

    public uint PixelWidth { get; }
    public uint PixelHeight { get; }
    public int ViewId => 0;
}

file sealed class RecordingPass : IPass<DispatchView>
{
    private readonly bool _rentTransient;
    private TextureHandle _scratch;

    public RecordingPass(bool rentTransient = false) => _rentTransient = rentTransient;

    public int RenderCount { get; private set; }
    public List<uint> ViewWidths { get; } = new();

    public string Name => "Recording";

    public void Setup(RenderContextBuilder builder)
    {
        if (_rentTransient)
            _scratch = builder.DeclareOutputTexture("Scratch", GraphTextureDesc.ViewSized(false, 1f, PixelFormat.R8_G8_B8_A8_UNorm));
    }

    public void Render(RenderContext<DispatchView> context)
    {
        RenderCount++;
        ViewWidths.Add(context.View.PixelWidth);

        if (_rentTransient)
            context.GetRenderTexture(_scratch);
    }
}

file sealed class LeakingCommandBufferPass : IPass<DispatchView>
{
    public string Name => "Leaking";

    public void Setup(RenderContextBuilder builder) { }

    public void Render(RenderContext<DispatchView> context)
    {
        context.GetCommandBuffer("Leaked");
    }
}

file sealed class BackbufferPass : IPass<DispatchView>
{
    private TextureHandle _backbuffer;

    public int RenderCount { get; private set; }
    public bool SawFramebuffer { get; private set; }

    public string Name => "Backbuffer";

    public void Setup(RenderContextBuilder builder) => _backbuffer = builder.DeclareBackbuffer();

    public void Render(RenderContext<DispatchView> context)
    {
        RenderCount++;
        SawFramebuffer = context.GetRenderTexture(_backbuffer).Framebuffer != null;
    }
}

file sealed class TestPipeline : RenderPipeline<DispatchView>
{
    private readonly IPass<DispatchView>[] _passes;

    public TestPipeline(params IPass<DispatchView>[] passes) => _passes = passes;

    protected override void InitializePasses()
    {
        foreach (IPass<DispatchView> pass in _passes)
            AddPass(pass);
    }
}

public abstract class DispatchRenderGraphTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    [Fact]
    public void Dispatch_RunsEachPassOncePerView()
    {
        RecordingPass passA = new();
        RecordingPass passB = new();
        using TestPipeline pipeline = new(passA, passB);
        DispatchView[] views = { new(64, 64), new(80, 48), new(32, 32) };

        GD.DispatchGraph(pipeline, views);
        GD.WaitForIdle();

        Assert.Equal(views.Length, passA.RenderCount);
        Assert.Equal(views.Length, passB.RenderCount);
    }

    [Fact]
    public void Dispatch_ReturnsTaskThatCompletes()
    {
        using TestPipeline pipeline = new(new RecordingPass());

        ExecutionTask task = GD.DispatchGraph(pipeline, new DispatchView[] { new(64, 64) });
        GD.WaitForExecution(task);

        Assert.True(GD.IsExecutionComplete(task));
    }

    [Fact]
    public void Dispatch_EachViewSeesItsOwnContext()
    {
        RecordingPass pass = new();
        using TestPipeline pipeline = new(pass);
        DispatchView[] views = { new(64, 64), new(128, 96), new(32, 200) };

        GD.DispatchGraph(pipeline, views);
        GD.WaitForIdle();

        Assert.Equal(new uint[] { 64, 128, 32 }, pass.ViewWidths);
    }

    [Fact]
    public void Dispatch_BackbufferDeclaredWithoutMainSwapchain_Throws()
    {
        using TestPipeline pipeline = new(new BackbufferPass());

        Assert.Throws<InvalidOperationException>(() => GD.DispatchGraph(pipeline, new DispatchView[] { new(64, 64) }));
        GD.WaitForIdle();
    }

    [Fact]
    public void Dispatch_TransientThroughContext_ReclaimsAcrossManyDispatches()
    {
        RecordingPass pass = new(rentTransient: true);
        using TestPipeline pipeline = new(pass);
        DispatchView[] views = { new(64, 64) };

        uint iterations = GD.MaxExecutingTasks * 2 + 1;
        for (uint i = 0; i < iterations; i++)
        {
            ExecutionTask task = GD.DispatchGraph(pipeline, views);
            GD.WaitForExecution(task);
        }

        Assert.Equal((int)iterations, pass.RenderCount);
    }

    [Fact]
    public void Dispatch_PassRentsCommandBufferWithoutSubmitting_WarnsOnce()
    {
        List<string> warnings = new();
        GraphicsDeviceWarningHandler? previous = GD.OnWarning;
        GD.OnWarning = message => warnings.Add(message);
        try
        {
            using TestPipeline pipeline = new(new LeakingCommandBufferPass());
            GD.DispatchGraph(pipeline, new DispatchView[] { new(64, 64) });
            GD.WaitForIdle();
        }
        finally
        {
            GD.OnWarning = previous;
        }

        Assert.Single(warnings);
        Assert.Contains("Leaking", warnings[0]);
    }

    [Fact]
    public void Dispatch_ManyTimes_NeverExceedsMaxExecutingGraphs()
    {
        using TestPipeline pipeline = new(new RecordingPass());
        DispatchView[] views = { new(64, 64) };

        uint max = GD.MaxExecutingTasks;
        for (uint i = 0; i < max * 3 + 1; i++)
        {
            GD.DispatchGraph(pipeline, views);
            Assert.True(GD.ExecutingTasks <= max, "in-flight executions exceeded MaxExecutingTasks");
        }

        GD.WaitForIdle();
    }
}

public abstract class DispatchRenderGraphPresentTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    [Fact]
    public void Dispatch_BackbufferPass_ResolvesSwapchainAndPresents()
    {
        BackbufferPass pass = new();
        using TestPipeline pipeline = new(new RecordingPass(), pass);
        DispatchView[] views = { new(64, 64) };

        GD.DispatchGraph(pipeline, views);
        GD.WaitForIdle();

        Assert.Equal(1, pass.RenderCount);
        Assert.True(pass.SawFramebuffer);
    }

    [Fact]
    public void Dispatch_NoPassDeclaresBackbuffer_RunsWithoutPresenting()
    {
        RecordingPass pass = new();
        using TestPipeline pipeline = new(pass);

        GD.DispatchGraph(pipeline, new DispatchView[] { new(64, 64) });
        GD.WaitForIdle();

        Assert.Equal(1, pass.RenderCount);
    }
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanDispatchRenderGraphTests : DispatchRenderGraphTests<VulkanDeviceCreator> { }

[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanDispatchRenderGraphPresentTests : DispatchRenderGraphPresentTests<VulkanDeviceCreatorWithMainSwapchain> { }
#endif
