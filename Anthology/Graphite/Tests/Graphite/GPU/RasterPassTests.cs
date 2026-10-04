#nullable enable

using Prowl.Graphite.RenderGraph;
using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Tests;

// Coverage for the RasterPass convenience base (Phases E + F): BindTarget binds the declared target and
// applies its declared load ops, so a transient target is cleared by its lifetime-default Clear op even
// though the pass records no explicit ClearColorTarget call.

file readonly struct RasterView : IRenderView
{
    public RasterView(uint width, uint height)
    {
        PixelWidth = width;
        PixelHeight = height;
    }

    public uint PixelWidth { get; }
    public uint PixelHeight { get; }
    public int ViewId => 0;
}

file sealed class ClearingRasterPass : RasterPass<RasterView>
{
    private readonly RenderResourceID _id;
    private readonly Color _clear;

    public ClearingRasterPass(RenderResourceID id, Color clear)
    {
        _id = id;
        _clear = clear;
    }

    public override string Name => "ClearRaster";

    public override void Setup(RenderContextBuilder builder)
        => SetTarget(builder, _id, GraphTextureDesc.ViewSized(PixelFormat.R32_G32_B32_A32_Float), ops: TargetLoadStoreOps.Clear(_clear));

    public override void Render(RenderContext<RasterView> context, CommandBuffer cmd)
    {
        BindTarget(context, cmd);
    }
}

file sealed class CopyReadbackPass : IPass<RasterView>
{
    private readonly RenderResourceID _id;
    private readonly DeviceBuffer _readback;
    private TextureHandle _handle;

    public CopyReadbackPass(RenderResourceID id, DeviceBuffer readback)
    {
        _id = id;
        _readback = readback;
    }

    public string Name => "CopyReadback";

    public void Setup(RenderContextBuilder builder) => _handle = builder.DeclareInputTexture(_id);

    public void Render(RenderContext<RasterView> context, CommandBuffer cmd)
    {
        RenderTexture target = context.GetRenderTexture(_handle);
        cmd.CopyTextureToBuffer(target.ColorTextures[0], _readback, 0, TextureRegion.Whole(target.ColorTextures[0]));
    }
}

public abstract class RasterPassTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    [Fact]
    public void BindTarget_TransientTarget_AppliesDeclaredDefaultClear_WithNoExplicitClearCall()
    {
        const uint size = 64;
        Color clear = new(0.2f, 0.4f, 0.6f, 1.0f);

        DeviceBuffer readback = CreateTexelReadbackBuffer<Color>(size, size);

        RenderResourceID id = RenderResourceID.Intern("raster_clear_target");
        ClearingRasterPass clearPass = new(id, clear);
        CopyReadbackPass copyPass = new(id, readback);
        using RenderPipeline<RasterView> pipeline = new([clearPass, copyPass]);

        GD.DispatchGraph(pipeline, new RasterView[] { new(size, size) });
        GD.WaitForIdle();

        TexelData<Color> map = ReadTexels<Color>(readback, size, size);
        Assert.Equal(clear, map[(int)size / 2, (int)size / 2], ColorFuzzyComparer.Instance);
        Assert.Equal(clear, map[0, 0], ColorFuzzyComparer.Instance);
    }
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanRasterPassTests : RasterPassTests<VulkanDeviceCreator> { }
#endif
