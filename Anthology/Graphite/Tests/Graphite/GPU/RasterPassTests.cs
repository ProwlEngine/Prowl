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
        => SetTarget(builder, _id, GraphTextureDesc.ViewSized(false, 1f, PixelFormat.R32_G32_B32_A32_Float));

    public override void Render(RenderContext<RasterView> context)
    {
        CommandBuffer cmd = context.GetCommandBuffer(Name);
        BindTarget(context, cmd, _clear);
        context.SubmitCommandBuffer(cmd);
    }
}

file sealed class CopyReadbackPass : IPass<RasterView>
{
    private readonly RenderResourceID _id;
    private readonly Texture _readback;
    private TextureHandle _handle;

    public CopyReadbackPass(RenderResourceID id, Texture readback)
    {
        _id = id;
        _readback = readback;
    }

    public string Name => "CopyReadback";

    public void Setup(RenderContextBuilder builder) => _handle = builder.GetInputTexture(_id);

    public void Render(RenderContext<RasterView> context)
    {
        RenderTexture target = context.GetRenderTexture(_handle);
        CommandBuffer cmd = context.GetCommandBuffer(Name);
        cmd.CopyTexture(target.ColorTextures[0], _readback);
        context.SubmitCommandBuffer(cmd);
    }
}

file sealed class NoOpRasterPresentPass : IPresentPass<RasterView>
{
    public string Name => "Present";
    public void Setup(PresentContextBuilder builder) { }
    public void Present(RenderContext<RasterView> context) { }
}

file sealed class RasterTestPipeline : RenderPipeline<RasterView>
{
    private readonly IPass<RasterView>[] _passes;

    public RasterTestPipeline(params IPass<RasterView>[] passes) => _passes = passes;

    protected override void InitializePasses()
    {
        foreach (IPass<RasterView> pass in _passes)
            AddPass(pass);
        SetPresentPass(new NoOpRasterPresentPass());
    }
}

public abstract class RasterPassTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    [Fact]
    public void BindTarget_TransientTarget_AppliesDeclaredDefaultClear_WithNoExplicitClearCall()
    {
        const uint size = 64;
        Color clear = new(0.2f, 0.4f, 0.6f, 1.0f);

        Texture readback = RF.CreateTexture(TextureDescription.Texture2D(
            size, size, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Staging));

        RenderResourceID id = RenderResourceID.Intern("raster_clear_target");
        ClearingRasterPass clearPass = new(id, clear);
        CopyReadbackPass copyPass = new(id, readback);
        using RasterTestPipeline pipeline = new(clearPass, copyPass);

        GD.DispatchGraph(pipeline, new RasterView[] { new(size, size) });
        GD.WaitForIdle();

        MappedResourceView<Color> map = GD.Map<Color>(readback, MapMode.Read);
        Assert.Equal(clear, map[(int)size / 2, (int)size / 2], ColorFuzzyComparer.Instance);
        Assert.Equal(clear, map[0, 0], ColorFuzzyComparer.Instance);
        GD.Unmap(readback);
    }
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanRasterPassTests : RasterPassTests<VulkanDeviceCreator> { }
#endif
