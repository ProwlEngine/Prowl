#nullable enable

using System.Collections.Generic;

using Prowl.Graphite.RenderGraph;

using Xunit;

namespace Prowl.Graphite.Tests;

// Coverage for the high-level GraphicsDevice.DispatchRenderGraph entry point: one graph execution per
// dispatch, the pass loop running once per view against a fresh per-view context, the returned task
// completing, transient acquisition through the context surviving many dispatches, and the present
// pass arming the swap only when a swapchain target is available. The armed-present path runs on the
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
}

file sealed class RecordingPass : IPass<DispatchView>
{
    private readonly bool _rentTransient;

    public RecordingPass(bool rentTransient = false) => _rentTransient = rentTransient;

    public int RenderCount { get; private set; }
    public List<uint> ViewWidths { get; } = new();

    public string Name => "Recording";

    public void Setup(RenderContextBuilder builder) { }

    public void Render(RenderContext<DispatchView> context)
    {
        RenderCount++;
        ViewWidths.Add(context.View.PixelWidth);

        if (_rentTransient)
            context.GetTransientTexture(GraphTextureDesc.ViewSized(false, 1f, PixelFormat.R8_G8_B8_A8_UNorm));
    }
}

file sealed class RecordingPresentPass : IPresentPass<DispatchView>
{
    private readonly bool _arm;
    private readonly bool _requestSwapchain;

    public RecordingPresentPass(bool arm, bool requestSwapchain = true)
    {
        _arm = arm;
        _requestSwapchain = requestSwapchain;
    }

    public int PresentCount { get; private set; }
    public bool SawSwapchainTarget { get; private set; }

    public string Name => "Present";

    public void Setup(PresentContextBuilder builder)
    {
        if (_requestSwapchain)
            builder.RequestSwapchain();
    }

    public void Present(RenderContext<DispatchView> context)
    {
        PresentCount++;
        Framebuffer? target = context.SwapchainTarget;
        SawSwapchainTarget = target != null;

        if (_arm && target != null)
            context.Present();
    }
}

file sealed class TestPipeline : RenderPipeline<DispatchView>
{
    private readonly IPresentPass<DispatchView> _present;
    private readonly IPass<DispatchView>[] _passes;

    public TestPipeline(IPresentPass<DispatchView> present, params IPass<DispatchView>[] passes)
    {
        _present = present;
        _passes = passes;
    }

    protected override void InitializePasses()
    {
        foreach (IPass<DispatchView> pass in _passes)
            AddPass(pass);

        SetPresentPass(_present);
    }
}

public abstract class DispatchRenderGraphTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    [Fact]
    public void Dispatch_RunsEachPassOncePerView()
    {
        RecordingPass passA = new();
        RecordingPass passB = new();
        using TestPipeline pipeline = new(new RecordingPresentPass(arm: false), passA, passB);
        DispatchView[] views = { new(64, 64), new(80, 48), new(32, 32) };

        GD.DispatchGraph(pipeline, views);
        GD.WaitForIdle();

        Assert.Equal(views.Length, passA.RenderCount);
        Assert.Equal(views.Length, passB.RenderCount);
    }

    [Fact]
    public void Dispatch_ReturnsTaskThatCompletes()
    {
        using TestPipeline pipeline = new(new RecordingPresentPass(arm: false), new RecordingPass());

        ExecutionTask task = GD.DispatchGraph(pipeline, new DispatchView[] { new(64, 64) });
        GD.WaitForExecution(task);

        Assert.True(GD.IsExecutionComplete(task));
    }

    [Fact]
    public void Dispatch_EachViewSeesItsOwnContext()
    {
        RecordingPass pass = new();
        using TestPipeline pipeline = new(new RecordingPresentPass(arm: false), pass);
        DispatchView[] views = { new(64, 64), new(128, 96), new(32, 200) };

        GD.DispatchGraph(pipeline, views);
        GD.WaitForIdle();

        Assert.Equal(new uint[] { 64, 128, 32 }, pass.ViewWidths);
    }

    [Fact]
    public void Dispatch_OffscreenPresentPass_NeverArmsSwapchain()
    {
        RecordingPresentPass present = new(arm: true);
        using TestPipeline pipeline = new(present, new RecordingPass());
        DispatchView[] views = { new(64, 64), new(64, 64) };

        GD.DispatchGraph(pipeline, views);
        GD.WaitForIdle();

        Assert.Equal(views.Length, present.PresentCount);
        Assert.False(present.SawSwapchainTarget);
    }

    [Fact]
    public void Dispatch_TransientThroughContext_ReclaimsAcrossManyDispatches()
    {
        RecordingPass pass = new(rentTransient: true);
        using TestPipeline pipeline = new(new RecordingPresentPass(arm: false), pass);
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
    public void Dispatch_ManyTimes_NeverExceedsMaxExecutingGraphs()
    {
        using TestPipeline pipeline = new(new RecordingPresentPass(arm: false), new RecordingPass());
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
    public void Dispatch_ArmedPresentPass_AcquiresSwapchainAndPresents()
    {
        RecordingPresentPass present = new(arm: true);
        using TestPipeline pipeline = new(present, new RecordingPass());
        DispatchView[] views = { new(64, 64) };

        GD.DispatchGraph(pipeline, views);
        GD.WaitForIdle();

        Assert.Equal(1, present.PresentCount);
        Assert.True(present.SawSwapchainTarget);
    }

    [Fact]
    public void Dispatch_PresentPassDidNotRequestSwapchainInSetup_SwapchainTargetIsNullEvenWithAWindow()
    {
        RecordingPresentPass present = new(arm: true, requestSwapchain: false);
        using TestPipeline pipeline = new(present, new RecordingPass());
        DispatchView[] views = { new(64, 64) };

        GD.DispatchGraph(pipeline, views);
        GD.WaitForIdle();

        Assert.Equal(1, present.PresentCount);
        Assert.False(present.SawSwapchainTarget);
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
