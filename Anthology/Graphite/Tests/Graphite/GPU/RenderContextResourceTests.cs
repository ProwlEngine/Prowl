#nullable enable

using System;
using System.Collections.Generic;

using Prowl.Graphite.RenderGraph;

using Xunit;

namespace Prowl.Graphite.Tests;

// Coverage for RenderContext.GetRenderTexture: the resolution/caching seam behind "independent draw
// calls get the right resources" and "render textures are properly created and returned by the graph".
// Also covers view-size wiring for graph resources, isolation of resolved resources across views and
// across dispatches, the profiler capture path, and the current (permissive) behavior of requesting the
// swapchain target from a non-present pass.

file readonly struct ResourceView : IRenderView
{
    public ResourceView(uint width, uint height)
    {
        PixelWidth = width;
        PixelHeight = height;
    }

    public uint PixelWidth { get; }
    public uint PixelHeight { get; }
}

file sealed class ResolvingPass : IPass<ResourceView>
{
    private readonly RenderResourceID _id;
    private readonly GraphTextureDesc _desc;
    private readonly bool _isOutput;
    private readonly int _resolvesPerRender;
    private TextureHandle _handle;

    public ResolvingPass(string name, RenderResourceID id, GraphTextureDesc desc, bool isOutput = true, int resolvesPerRender = 1)
    {
        Name = name;
        _id = id;
        _desc = desc;
        _isOutput = isOutput;
        _resolvesPerRender = resolvesPerRender;
    }

    public string Name { get; }

    public List<RenderTexture> Resolved { get; } = new();

    public void Setup(RenderContextBuilder builder)
        => _handle = _isOutput ? builder.GetOutputTexture(_id, _desc) : builder.GetInputTexture(_id);

    public void Render(RenderContext<ResourceView> context)
    {
        for (int i = 0; i < _resolvesPerRender; i++)
            Resolved.Add(context.GetRenderTexture(_handle));
    }
}

file sealed class UndeclaredResolvePass : IPass<ResourceView>
{
    public string Name => "UndeclaredResolve";

    public void Setup(RenderContextBuilder builder) { }

    public void Render(RenderContext<ResourceView> context)
        => context.GetRenderTexture(new TextureHandle(RenderResourceID.Intern("resourcetest_undeclared")));
}

file sealed class DefaultHandleResolvePass : IPass<ResourceView>
{
    public string Name => "DefaultHandleResolve";

    public void Setup(RenderContextBuilder builder) { }

    public void Render(RenderContext<ResourceView> context)
        => context.GetRenderTexture(default);
}

file sealed class TwoOutputPass : IPass<ResourceView>
{
    private readonly RenderResourceID _a;
    private readonly RenderResourceID _b;
    private readonly GraphTextureDesc _desc;

    public TwoOutputPass(string name, RenderResourceID a, RenderResourceID b, GraphTextureDesc desc)
    {
        Name = name;
        _a = a;
        _b = b;
        _desc = desc;
    }

    public string Name { get; }

    public void Setup(RenderContextBuilder builder)
    {
        builder.GetOutputTexture(_a, _desc);
        builder.GetOutputTexture(_b, _desc);
    }

    public void Render(RenderContext<ResourceView> context) { }
}

file sealed class ZeroOutputPass : IPass<ResourceView>
{
    public ZeroOutputPass(string name) => Name = name;

    public string Name { get; }

    public void Setup(RenderContextBuilder builder) { }

    public void Render(RenderContext<ResourceView> context) { }
}

file sealed class HistoryResolvingPass : IPass<ResourceView>
{
    private readonly RenderResourceID _id;
    private readonly GraphTextureDesc _desc;
    private TextureHandle _handle;

    public HistoryResolvingPass(RenderResourceID id, GraphTextureDesc desc)
    {
        _id = id;
        _desc = desc;
    }

    public string Name => "History";

    public List<RenderTexture> Current { get; } = new();
    public List<RenderTexture> Previous { get; } = new();

    public void Setup(RenderContextBuilder builder) => _handle = builder.GetOutputTexture(_id, _desc, history: 1);

    public void Render(RenderContext<ResourceView> context)
    {
        Current.Add(context.GetRenderTexture(_handle, 0));
        Previous.Add(context.GetRenderTexture(_handle, 1));
    }
}

file sealed class ImportingPass : IPass<ResourceView>
{
    private readonly RenderResourceID _id;
    private readonly RenderTexture _external;
    private TextureHandle _handle;

    public ImportingPass(RenderResourceID id, RenderTexture external)
    {
        _id = id;
        _external = external;
    }

    public string Name => "Import";
    public RenderTexture? Resolved { get; private set; }

    public void Setup(RenderContextBuilder builder) => _handle = builder.ImportTexture(_id, _external);

    public void Render(RenderContext<ResourceView> context) => Resolved = context.GetRenderTexture(_handle);
}

file sealed class RequestingPresentPass : IPresentPass<ResourceView>
{
    public bool SawSwapchainTarget { get; private set; }

    public string Name => "RequestingPresent";

    public void Setup(PresentContextBuilder builder) => builder.RequestSwapchain();

    public void Present(RenderContext<ResourceView> context)
        => SawSwapchainTarget = context.SwapchainTarget != null;
}

file sealed class NonRequestingPresentPass : IPresentPass<ResourceView>
{
    public bool SawSwapchainTarget { get; private set; }

    public string Name => "NonRequestingPresent";

    public void Setup(PresentContextBuilder builder) { }

    public void Present(RenderContext<ResourceView> context)
        => SawSwapchainTarget = context.SwapchainTarget != null;
}

file sealed class NoOpPresentPass : IPresentPass<ResourceView>
{
    public string Name => "Present";

    public void Setup(PresentContextBuilder builder) { }

    public void Present(RenderContext<ResourceView> context) { }
}

file sealed class ReadingPresentPass : IPresentPass<ResourceView>
{
    private readonly RenderResourceID _id;
    private TextureHandle _handle;

    public ReadingPresentPass(RenderResourceID id)
    {
        _id = id;
    }

    public string Name => "ReadingPresent";

    public RenderTexture? Resolved { get; private set; }

    public void Setup(PresentContextBuilder builder) => _handle = builder.GetInputTexture(_id);

    public void Present(RenderContext<ResourceView> context) => Resolved = context.GetRenderTexture(_handle);
}

file sealed class RecordingProfiler : IProfiler
{
    public bool RequestCapture { get; set; }

    public List<int> Captures { get; } = new();

    public void Allocate(AllocBin type, long bytes) { }
    public void Free(AllocBin type, long bytes) { }
    public void AllocateMemory(BufferRoleBin role, long bytes) { }
    public void FreeMemory(BufferRoleBin role, long bytes) { }
    public void Record(BufferOpBin op, long bytes) { }
    public void RecordSwap(SwapBin evt, long bytes) { }

    public void BeginPass(in PassInfo pass) { }
    public void EndPass(in PassInfo pass) { }
    public void RecordPassRead(in PassInfo pass, RenderResourceID resource, DeviceResource resolved) { }

    public void BeginSample(string name) { }

    public void EndSample() { }

    public void Capture(in PassInfo pass, IReadOnlyList<Framebuffer> passOutputs, TransferCommandBuffer transfer)
    {
        transfer.Begin();
        transfer.End();
        Captures.Add(passOutputs.Count);
    }

    public void RecordDraw(in DrawCallInfo info) { }
    public void RecordDispatch(in DispatchCallInfo info) { }
    public void RecordPipelineSwitch(in PipelineBindInfo info) { }
    public void RecordResourceSetBind(uint setCount) { }
    public void RecordBarrier(BarrierBin kind, uint count) { }
    public void RecordSubmit(in ProfilerSubmitInfo info) { }

    public bool RequestExecutionTiming => false;
    public void RecordExecutionTime(PassInfo? pass, string bufferName, bool isTransfer, double milliseconds) { }
}

file sealed class ResourceTestPipeline : RenderPipeline<ResourceView>
{
    private readonly IPresentPass<ResourceView> _present;
    private readonly IPass<ResourceView>[] _passes;

    public ResourceTestPipeline(params IPass<ResourceView>[] passes) : this(new NoOpPresentPass(), passes) { }

    public ResourceTestPipeline(IPresentPass<ResourceView> present, params IPass<ResourceView>[] passes)
    {
        _present = present;
        _passes = passes;
    }

    protected override void InitializePasses()
    {
        foreach (IPass<ResourceView> pass in _passes)
            AddPass(pass);

        SetPresentPass(_present);
    }
}

public abstract class RenderContextResourceTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    private static GraphTextureDesc ColorDesc(float scale = 1f)
        => GraphTextureDesc.ViewSized(false, scale, PixelFormat.R8_G8_B8_A8_UNorm);

    [Fact]
    public void GetRenderTexture_SameHandleWithinContext_ReturnsCachedInstance()
    {
        ResolvingPass pass = new("Pass", RenderResourceID.Intern("resourcetest_cache"), ColorDesc(), resolvesPerRender: 3);
        using ResourceTestPipeline pipeline = new(pass);

        GD.DispatchGraph(pipeline, new ResourceView[] { new(64, 64) });
        GD.WaitForIdle();

        Assert.Equal(3, pass.Resolved.Count);
        Assert.Same(pass.Resolved[0], pass.Resolved[1]);
        Assert.Same(pass.Resolved[0], pass.Resolved[2]);
    }

    [Fact]
    public void GetRenderTexture_WriterAndReaderOfSameResource_ResolveToSameInstance()
    {
        RenderResourceID id = RenderResourceID.Intern("resourcetest_shared");
        ResolvingPass writer = new("Writer", id, ColorDesc(), isOutput: true);
        ResolvingPass reader = new("Reader", id, ColorDesc(), isOutput: false);
        using ResourceTestPipeline pipeline = new(writer, reader);

        GD.DispatchGraph(pipeline, new ResourceView[] { new(64, 64) });
        GD.WaitForIdle();

        Assert.Same(writer.Resolved[0], reader.Resolved[0]);
    }

    [Fact]
    public void GetRenderTexture_DifferentDeclaredResources_ResolveToDistinctInstances()
    {
        ResolvingPass a = new("A", RenderResourceID.Intern("resourcetest_distinct_a"), ColorDesc());
        ResolvingPass b = new("B", RenderResourceID.Intern("resourcetest_distinct_b"), ColorDesc());
        using ResourceTestPipeline pipeline = new(a, b);

        GD.DispatchGraph(pipeline, new ResourceView[] { new(64, 64) });
        GD.WaitForIdle();

        Assert.NotSame(a.Resolved[0], b.Resolved[0]);
    }

    [Fact]
    public void GetRenderTexture_UndeclaredHandle_Throws()
    {
        using ResourceTestPipeline pipeline = new(new UndeclaredResolvePass());

        Assert.Throws<InvalidOperationException>(
            () => GD.DispatchGraph(pipeline, new ResourceView[] { new(64, 64) }));
    }

    [Fact]
    public void GetRenderTexture_DefaultHandle_Throws()
    {
        using ResourceTestPipeline pipeline = new(new DefaultHandleResolvePass());

        Assert.Throws<ArgumentException>(
            () => GD.DispatchGraph(pipeline, new ResourceView[] { new(64, 64) }));
    }

    [Fact]
    public void GetRenderTexture_TwoViewsInOneDispatch_ResolveToIndependentCorrectlySizedTextures()
    {
        ResolvingPass pass = new("Pass", RenderResourceID.Intern("resourcetest_perview"), ColorDesc());
        using ResourceTestPipeline pipeline = new(pass);
        ResourceView[] views = { new(64, 48), new(128, 96) };

        GD.DispatchGraph(pipeline, views);
        GD.WaitForIdle();

        Assert.Equal(2, pass.Resolved.Count);
        Assert.NotSame(pass.Resolved[0], pass.Resolved[1]);
        Assert.Equal(64u, pass.Resolved[0].Desc.Width);
        Assert.Equal(48u, pass.Resolved[0].Desc.Height);
        Assert.Equal(128u, pass.Resolved[1].Desc.Width);
        Assert.Equal(96u, pass.Resolved[1].Desc.Height);
    }

    [Fact]
    public void GetRenderTexture_ViewSizedResource_ScalesToViewPixelSize()
    {
        ResolvingPass pass = new("Pass", RenderResourceID.Intern("resourcetest_scale"), ColorDesc(0.5f));
        using ResourceTestPipeline pipeline = new(pass);

        GD.DispatchGraph(pipeline, new ResourceView[] { new(200, 100) });
        GD.WaitForIdle();

        Assert.Equal(100u, pass.Resolved[0].Desc.Width);
        Assert.Equal(50u, pass.Resolved[0].Desc.Height);
    }

    [Fact]
    public void GetRenderTexture_ExplicitSizedResource_IgnoresViewSize()
    {
        ResolvingPass pass = new("Pass",
            RenderResourceID.Intern("resourcetest_explicit"),
            GraphTextureDesc.Sized(37, 41, false, PixelFormat.R8_G8_B8_A8_UNorm));
        using ResourceTestPipeline pipeline = new(pass);

        GD.DispatchGraph(pipeline, new ResourceView[] { new(200, 300) });
        GD.WaitForIdle();

        Assert.Equal(37u, pass.Resolved[0].Desc.Width);
        Assert.Equal(41u, pass.Resolved[0].Desc.Height);
    }

    [Fact]
    public void Dispatch_ConsecutiveDispatchesWithDifferentViewSizes_EachResolvesOwnSizedTexture()
    {
        ResolvingPass pass = new("Pass", RenderResourceID.Intern("resourcetest_crossdispatch"), ColorDesc());
        using ResourceTestPipeline pipeline = new(pass);

        ExecutionTask task1 = GD.DispatchGraph(pipeline, new ResourceView[] { new(64, 64) });
        ExecutionTask task2 = GD.DispatchGraph(pipeline, new ResourceView[] { new(128, 128) });
        GD.WaitForExecution(task1);
        GD.WaitForExecution(task2);

        Assert.Equal(2, pass.Resolved.Count);
        Assert.Equal(64u, pass.Resolved[0].Desc.Width);
        Assert.Equal(128u, pass.Resolved[1].Desc.Width);
    }

    [Fact]
    public void ExecuteView_ProfilerRequestsCapture_CapturesEachPassByItsOwnDeclaredOutputCount()
    {
        RenderResourceID a = RenderResourceID.Intern("resourcetest_capture_a");
        RenderResourceID b = RenderResourceID.Intern("resourcetest_capture_b");
        TwoOutputPass twoOutputs = new("TwoOutputs", a, b, ColorDesc());
        ZeroOutputPass zeroOutputs = new("ZeroOutputs");
        using ResourceTestPipeline pipeline = new(zeroOutputs, twoOutputs);
        RecordingProfiler profiler = new() { RequestCapture = true };

        using GraphicsDevice profiledDevice = GD.BackendType switch
        {
            GraphicsBackend.Vulkan => GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true) { Profiler = profiler }),
            _ => throw new NotSupportedException(),
        };

        profiledDevice.DispatchGraph(pipeline, new ResourceView[] { new(64, 64) });
        profiledDevice.WaitForIdle();

        Assert.Single(profiler.Captures);
        Assert.Equal(2, profiler.Captures[0]);
    }

    [Fact]
    public void GetRenderTexture_HistoryResource_ThisFramesPreviousEqualsLastFramesCurrent()
    {
        HistoryResolvingPass pass = new(RenderResourceID.Intern("resourcetest_history"), ColorDesc());
        using ResourceTestPipeline pipeline = new(pass);

        for (int frame = 0; frame < 3; frame++)
        {
            ExecutionTask task = GD.DispatchGraph(pipeline, new ResourceView[] { new(64, 64) });
            GD.WaitForExecution(task);
        }

        for (int frame = 0; frame < 3; frame++)
            Assert.NotSame(pass.Current[frame], pass.Previous[frame]);

        Assert.Same(pass.Current[0], pass.Previous[1]);
        Assert.Same(pass.Current[1], pass.Previous[2]);
    }

    [Fact]
    public void ImportTexture_ResolvesToTheExternalTexture()
    {
        RenderTexture external = RF.CreateRenderTexture(new RenderTextureDescription(
            64, 64, new[] { PixelFormat.R8_G8_B8_A8_UNorm }, false, TextureSampleCount.Count1));
        ImportingPass pass = new(RenderResourceID.Intern("resourcetest_imported"), external);
        using ResourceTestPipeline pipeline = new(pass);

        GD.DispatchGraph(pipeline, new ResourceView[] { new(64, 64) });
        GD.WaitForIdle();

        Assert.Same(external, pass.Resolved);
    }

    [Fact]
    public void PresentPass_DeclaresInputInSetup_ResolvesToSameInstanceAsWriter()
    {
        RenderResourceID id = RenderResourceID.Intern("resourcetest_present_reads_graph");
        ResolvingPass writer = new("Writer", id, ColorDesc());
        ReadingPresentPass present = new(id);
        using ResourceTestPipeline pipeline = new(present, writer);

        GD.DispatchGraph(pipeline, new ResourceView[] { new(64, 64) });
        GD.WaitForIdle();

        Assert.NotNull(present.Resolved);
        Assert.Same(writer.Resolved[0], present.Resolved);
    }
}

public abstract class RenderContextResourcePresentTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    [Fact]
    public void SwapchainTarget_PresentPassRequestedItInSetup_IsResolvedDuringPresent()
    {
        RequestingPresentPass present = new();
        using ResourceTestPipeline pipeline = new(present);

        GD.DispatchGraph(pipeline, new ResourceView[] { new(64, 64) });
        GD.WaitForIdle();

        Assert.True(present.SawSwapchainTarget);
    }

    [Fact]
    public void SwapchainTarget_PresentPassDidNotRequestItInSetup_IsNullEvenWithAWindow()
    {
        NonRequestingPresentPass present = new();
        using ResourceTestPipeline pipeline = new(present);

        GD.DispatchGraph(pipeline, new ResourceView[] { new(64, 64) });
        GD.WaitForIdle();

        Assert.False(present.SawSwapchainTarget);
    }
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanRenderContextResourceTests : RenderContextResourceTests<VulkanDeviceCreator> { }

[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanRenderContextResourcePresentTests : RenderContextResourcePresentTests<VulkanDeviceCreatorWithMainSwapchain> { }
#endif
