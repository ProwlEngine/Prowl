#nullable enable

using System;
using System.Collections.Generic;

using Prowl.Graphite.RenderGraph;

using Xunit;

namespace Prowl.Graphite.Tests;

file readonly struct ResourceView : IRenderView
{
    public Framebuffer? Target { get; }

    public ResourceView(uint width, uint height, Swapchain? swapchain = null)
    {
        Target = swapchain?.Framebuffer;
        PixelWidth = width;
        PixelHeight = height;
    }

    public uint PixelWidth { get; }
    public uint PixelHeight { get; }
    public int ViewId => 0;
}

file sealed class ResolvingPass : IPass
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
        => _handle = _isOutput ? builder.DeclareOutputTexture(_id, _desc) : builder.DeclareInputTexture(_id);

    public void Render(RenderContext context, CommandBuffer cmd)
    {
        for (int i = 0; i < _resolvesPerRender; i++)
            Resolved.Add(context.GetRenderTexture(_handle));
    }
}

file sealed class TwoOutputPass : IPass
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
        builder.DeclareOutputTexture(_a, _desc);
        builder.DeclareOutputTexture(_b, _desc);
    }

    public void Render(RenderContext context, CommandBuffer cmd) { }
}

file sealed class ZeroOutputPass : IPass
{
    public ZeroOutputPass(string name) => Name = name;

    public string Name { get; }

    public void Setup(RenderContextBuilder builder) { }

    public void Render(RenderContext context, CommandBuffer cmd) { }
}

file sealed class ImportingPass : IPass
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

    public void Setup(RenderContextBuilder builder) => _handle = builder.DeclareImportedTexture(_id, _external);

    public void Render(RenderContext context, CommandBuffer cmd) => Resolved = context.GetRenderTexture(_handle);
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

    public void BeginView(in ViewInfo view) { }
    public void EndView(in ViewInfo view) { }

    public void BeginPass(in PassInfo pass) { }
    public void EndPass(in PassInfo pass) { }
    public void RecordPassRead(in PassInfo pass, RenderResourceID resource, RenderTexture? texture, DeviceBuffer? buffer) { }
    public void RecordPassWrite(in PassInfo pass, RenderResourceID resource, RenderTexture? texture, DeviceBuffer? buffer) { }

    public void Capture(in PassInfo pass, IReadOnlyList<Framebuffer> passOutputs, CommandBuffer capture)
    {
        Captures.Add(passOutputs.Count);
    }

    public void RecordDraw(in CommandBufferInfo commandBuffer, in DrawCallInfo info) { }
    public void RecordDrawBuffers(in CommandBufferInfo commandBuffer, in DrawBufferInfo info) { }
    public void RecordDispatch(in CommandBufferInfo commandBuffer, in DispatchCallInfo info) { }
    public void RecordPipelineSwitch(in CommandBufferInfo commandBuffer, in PipelineBindInfo info) { }

    public void RecordResourceSetBind(uint setCount) { }
    public void RecordBarrier(BarrierBin kind, uint count) { }
    public void RecordSubmit(in CommandBufferInfo commandBuffer, bool isTransfer) { }

    public bool RequestGPUStatistics => false;
    public void RecordExecutionTime(in CommandBufferInfo commandBuffer, bool isTransfer, double milliseconds) { }
    public void RecordGpuVertexStats(in CommandBufferInfo commandBuffer, in GpuVertexStats stats) { }
}

public abstract class RenderContextResourceTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    private static GraphTextureDesc ColorDesc(float scale = 1f)
        => GraphTextureDesc.ViewSized(PixelFormat.R8_G8_B8_A8_UNorm, scale);

    [Fact]
    public void GetRenderTexture_SameHandleWithinContext_ReturnsCachedInstance()
    {
        ResolvingPass pass = new("Pass", RenderResourceID.Intern("resourcetest_cache"), ColorDesc(), resolvesPerRender: 3);
        using RenderPipeline pipeline = new([pass]);

        GD.DispatchGraph(pipeline, new ResourceView[] { new(64, 64, GD.MainSwapchain) });
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
        using RenderPipeline pipeline = new([writer, reader]);

        GD.DispatchGraph(pipeline, new ResourceView[] { new(64, 64, GD.MainSwapchain) });
        GD.WaitForIdle();

        Assert.Same(writer.Resolved[0], reader.Resolved[0]);
    }

    [Fact]
    public void GetRenderTexture_DifferentDeclaredResources_ResolveToDistinctInstances()
    {
        ResolvingPass a = new("A", RenderResourceID.Intern("resourcetest_distinct_a"), ColorDesc());
        ResolvingPass b = new("B", RenderResourceID.Intern("resourcetest_distinct_b"), ColorDesc());
        using RenderPipeline pipeline = new([a, b]);

        GD.DispatchGraph(pipeline, new ResourceView[] { new(64, 64, GD.MainSwapchain) });
        GD.WaitForIdle();

        Assert.NotSame(a.Resolved[0], b.Resolved[0]);
    }

    [Fact]
    public void GetRenderTexture_TwoViewsInOneDispatch_ResolveToIndependentCorrectlySizedTextures()
    {
        ResolvingPass pass = new("Pass", RenderResourceID.Intern("resourcetest_perview"), ColorDesc());
        using RenderPipeline pipeline = new([pass]);
        ResourceView[] views = { new(64, 48, GD.MainSwapchain), new(128, 96, GD.MainSwapchain) };

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
        using RenderPipeline pipeline = new([pass]);

        GD.DispatchGraph(pipeline, new ResourceView[] { new(200, 100, GD.MainSwapchain) });
        GD.WaitForIdle();

        Assert.Equal(100u, pass.Resolved[0].Desc.Width);
        Assert.Equal(50u, pass.Resolved[0].Desc.Height);
    }

    [Fact]
    public void ExecuteView_ProfilerRequestsCapture_CapturesEachPassByItsOwnDeclaredOutputCount()
    {
        RenderResourceID a = RenderResourceID.Intern("resourcetest_capture_a");
        RenderResourceID b = RenderResourceID.Intern("resourcetest_capture_b");
        TwoOutputPass twoOutputs = new("TwoOutputs", a, b, ColorDesc());
        ZeroOutputPass zeroOutputs = new("ZeroOutputs");
        using RenderPipeline pipeline = new([zeroOutputs, twoOutputs]);
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
    public void DeclareImportedTexture_ResolvesToTheExternalTexture()
    {
        RenderTexture external = RF.CreateRenderTexture(new RenderTextureDescription(
            64, 64, new[] { PixelFormat.R8_G8_B8_A8_UNorm }, false, TextureSampleCount.Count1));
        ImportingPass pass = new(RenderResourceID.Intern("resourcetest_imported"), external);
        using RenderPipeline pipeline = new([pass]);

        GD.DispatchGraph(pipeline, new ResourceView[] { new(64, 64, GD.MainSwapchain) });
        GD.WaitForIdle();

        Assert.Same(external, pass.Resolved);
    }
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanRenderContextResourceTests : RenderContextResourceTests<VulkanDeviceCreator> { }
#endif
