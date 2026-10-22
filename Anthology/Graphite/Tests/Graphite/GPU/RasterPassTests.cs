#nullable enable

using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Tests;

// Coverage for the RasterPass convenience base (Phases E + F): the graph binds the declared target and
// applies its declared load ops, so a transient target is cleared by its lifetime-default Clear op even
// though the pass records no explicit ClearColorTarget call.

file sealed class ClearingRasterPass : InstanceRasterPass
{
    public override GraphKind Kind => TestViews.Kind;

    private readonly RenderResourceID _id;
    private readonly Color _clear;

    public ClearingRasterPass(RenderResourceID id, Color clear)
    {
        _id = id;
        _clear = clear;
    }

    public override string Name => "ClearRaster";

    public override void Setup(InstanceContextBuilder builder)
        => SetTarget(builder, _id, GraphTextureDesc.InstanceSized(PixelFormat.R32_G32_B32_A32_Float), ops: TargetLoadStoreOps.Clear(_clear));

    public override void Render(InstanceContext context, CommandBuffer cmd)
    {
    }
}

file sealed class RawClearPass : IInstancePass
{
    public GraphKind Kind => TestViews.Kind;

    private readonly RenderResourceID _id;
    private readonly Color _clear;

    public RawClearPass(RenderResourceID id, Color clear)
    {
        _id = id;
        _clear = clear;
    }

    public string Name => "RawClear";

    public void Setup(InstanceContextBuilder builder)
        => builder.DeclareOutputTexture(_id, GraphTextureDesc.InstanceSized(PixelFormat.R32_G32_B32_A32_Float), ops: TargetLoadStoreOps.Clear(_clear));

    public void Render(InstanceContext context, CommandBuffer cmd)
    {
    }
}

file sealed class CopyReadbackPass : IInstancePass
{
    public GraphKind Kind => TestViews.Kind;

    private readonly RenderResourceID _id;
    private readonly DeviceBuffer _readback;
    private TextureHandle _handle;

    public CopyReadbackPass(RenderResourceID id, DeviceBuffer readback)
    {
        _id = id;
        _readback = readback;
    }

    public string Name => "CopyReadback";

    public void Setup(InstanceContextBuilder builder) => _handle = builder.DeclareInputTexture(_id);

    public void Render(InstanceContext context, CommandBuffer cmd)
    {
        RenderTexture target = context.GetRenderTexture(_handle);
        cmd.CopyTextureToBuffer(target.ColorTextures[0], _readback, 0, TextureRegion.Whole(target.ColorTextures[0]));
    }
}

public abstract class RasterPassTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    [Fact]
    public void DeclaredTarget_TransientTarget_AppliesDeclaredDefaultClear_WithNoExplicitClearCall()
    {
        const uint size = 64;
        Color clear = new(0.2f, 0.4f, 0.6f, 1.0f);

        DeviceBuffer readback = CreateTexelReadbackBuffer<Color>(size, size);

        RenderResourceID id = RenderResourceID.Intern("raster_clear_target");
        ClearingRasterPass clearPass = new(id, clear);
        CopyReadbackPass copyPass = new(id, readback);
        using RenderGraph graph = TestViews.Build([clearPass, copyPass]);

        GD.DispatchGraph(graph, TestViews.Single(graph, new GraphExtent(size, size)));
        GD.WaitForIdle();

        TexelData<Color> map = ReadTexels<Color>(readback, size, size);
        Assert.Equal(clear, map[(int)size / 2, (int)size / 2], ColorFuzzyComparer.Instance);
        Assert.Equal(clear, map[0, 0], ColorFuzzyComparer.Instance);
    }

    [Fact]
    public void DeclaredTarget_RawPass_AppliesDeclaredClear()
    {
        const uint size = 64;
        Color clear = new(0.9f, 0.1f, 0.3f, 1.0f);

        DeviceBuffer readback = CreateTexelReadbackBuffer<Color>(size, size);

        RenderResourceID id = RenderResourceID.Intern("raster_raw_clear_target");
        using RenderGraph graph = TestViews.Build([new RawClearPass(id, clear), new CopyReadbackPass(id, readback)]);

        GD.DispatchGraph(graph, TestViews.Single(graph, new GraphExtent(size, size)));
        GD.WaitForIdle();

        TexelData<Color> map = ReadTexels<Color>(readback, size, size);
        Assert.Equal(clear, map[(int)size / 2, (int)size / 2], ColorFuzzyComparer.Instance);
    }
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanRasterPassTests : RasterPassTests<VulkanDeviceCreator> { }
#endif
