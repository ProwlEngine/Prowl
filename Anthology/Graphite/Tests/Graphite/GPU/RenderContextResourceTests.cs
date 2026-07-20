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

file sealed class ResolvingPass : IPass<ResourceView, int>
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
        => _handle = _isOutput ? builder.GetOutputTexture(_id, _desc) : builder.GetInputTexture(_id, _desc);

    public void Render(RenderContext<ResourceView, int> context)
    {
        for (int i = 0; i < _resolvesPerRender; i++)
            Resolved.Add(context.GetRenderTexture(_handle));
    }
}

file sealed class UndeclaredResolvePass : IPass<ResourceView, int>
{
    public string Name => "UndeclaredResolve";

    public void Setup(RenderContextBuilder builder) { }

    public void Render(RenderContext<ResourceView, int> context)
        => context.GetRenderTexture(new TextureHandle(RenderResourceID.Intern("resourcetest_undeclared")));
}

file sealed class DefaultHandleResolvePass : IPass<ResourceView, int>
{
    public string Name => "DefaultHandleResolve";

    public void Setup(RenderContextBuilder builder) { }

    public void Render(RenderContext<ResourceView, int> context)
        => context.GetRenderTexture(default);
}

file sealed class TwoOutputPass : IPass<ResourceView, int>
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

    public void Render(RenderContext<ResourceView, int> context) { }
}

file sealed class ZeroOutputPass : IPass<ResourceView, int>
{
    public ZeroOutputPass(string name) => Name = name;

    public string Name { get; }

    public void Setup(RenderContextBuilder builder) { }

    public void Render(RenderContext<ResourceView, int> context) { }
}

file sealed class RequestingPresentPass : IPresentPass<ResourceView, int>
{
    public bool SawSwapchainTarget { get; private set; }

    public string Name => "RequestingPresent";

    public void Setup(PresentContextBuilder builder) => builder.RequestSwapchain();

    public void Present(RenderContext<ResourceView, int> context)
        => SawSwapchainTarget = context.SwapchainTarget != null;
}

file sealed class NonRequestingPresentPass : IPresentPass<ResourceView, int>
{
    public bool SawSwapchainTarget { get; private set; }

    public string Name => "NonRequestingPresent";

    public void Setup(PresentContextBuilder builder) { }

    public void Present(RenderContext<ResourceView, int> context)
        => SawSwapchainTarget = context.SwapchainTarget != null;
}

file sealed class NoOpPresentPass : IPresentPass<ResourceView, int>
{
    public string Name => "Present";

    public void Setup(PresentContextBuilder builder) { }

    public void Present(RenderContext<ResourceView, int> context) { }
}

file sealed class ReadingPresentPass : IPresentPass<ResourceView, int>
{
    private readonly RenderResourceID _id;
    private readonly GraphTextureDesc _desc;
    private TextureHandle _handle;

    public ReadingPresentPass(RenderResourceID id, GraphTextureDesc desc)
    {
        _id = id;
        _desc = desc;
    }

    public string Name => "ReadingPresent";

    public RenderTexture? Resolved { get; private set; }

    public void Setup(PresentContextBuilder builder) => _handle = builder.GetInputTexture(_id, _desc);

    public void Present(RenderContext<ResourceView, int> context) => Resolved = context.GetRenderTexture(_handle);
}

file sealed class RecordingProfiler : IPassProfiler
{
    public bool RequestCapture { get; set; }

    public List<int> Captures { get; } = new();

    public void BeginSample(string name) { }

    public void EndSample() { }

    public void RecordDrawCall(int indexCount, int instanceCount) { }

    public void Capture(IReadOnlyList<Framebuffer> passOutputs, TransferCommandBuffer transfer)
    {
        transfer.Begin();
        transfer.End();
        Captures.Add(passOutputs.Count);
    }
}

file sealed class ResourceTestPipeline : RenderPipeline<ResourceView, int>
{
    private readonly IPresentPass<ResourceView, int> _present;
    private readonly IPass<ResourceView, int>[] _passes;

    public ResourceTestPipeline(params IPass<ResourceView, int>[] passes) : this(new NoOpPresentPass(), passes) { }

    public ResourceTestPipeline(IPresentPass<ResourceView, int> present, params IPass<ResourceView, int>[] passes)
    {
        _present = present;
        _passes = passes;
    }

    protected override void InitializePasses()
    {
        foreach (IPass<ResourceView, int> pass in _passes)
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

        GD.DispatchGraph(pipeline, new ResourceView[] { new(64, 64) }, profiler);
        GD.WaitForIdle();

        Assert.Single(profiler.Captures);
        Assert.Equal(2, profiler.Captures[0]);
    }

    [Fact]
    public void PresentPass_DeclaresInputInSetup_ResolvesToSameInstanceAsWriter()
    {
        RenderResourceID id = RenderResourceID.Intern("resourcetest_present_reads_graph");
        ResolvingPass writer = new("Writer", id, ColorDesc());
        ReadingPresentPass present = new(id, ColorDesc());
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
