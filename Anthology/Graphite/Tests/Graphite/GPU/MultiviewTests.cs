using System;

using Prowl.Graphite.RenderGraph;
using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Tests;

file readonly struct MultiviewView : IRenderView
{
    public Framebuffer? Target => null;
    public uint PixelWidth => 4;
    public uint PixelHeight => 4;
    public int ViewId => 0;
}

file sealed class MultiviewPass(GraphicsProgram program, Texture left, Texture right, Action<RenderTexture> written) : IPass
{
    private static readonly RenderResourceID s_eyes = RenderResourceID.Intern("Eyes");
    private TextureHandle _eyes;

    public string Name => "Eyes";

    public void Setup(RenderContextBuilder builder)
        => _eyes = builder.DeclareOutputTexture(s_eyes, new GraphTextureDesc
        {
            SizeMode = TextureSizeMode.ViewRelative,
            Scale = 1f,
            ColorFormats = [PixelFormat.R32_G32_B32_A32_Float],
            ArrayLayers = 2,
        }, usage: TextureState.Attachment | TextureState.TransferSrc);

    public void Render(RenderContext context, CommandBuffer cmd)
    {
        RenderTexture eyes = context.GetRenderTexture(_eyes);
        cmd.SetFramebuffer(eyes.Framebuffer, new TargetLoadStoreOps(AttachmentOps.Clear(Color.Black), AttachmentOps.Loaded));
        cmd.SetFullViewport();
        cmd.SetShader(program);
        cmd.SetVertexSource(VertexSource.None);
        cmd.Draw(3);
        cmd.CopyTexture(eyes.ColorTextures[0], 0, 0, 0, 0, 0, left, 0, 0, 0, 0, 0, 4, 4, 1, 1);
        cmd.CopyTexture(eyes.ColorTextures[0], 0, 0, 0, 0, 1, right, 0, 0, 0, 0, 0, 4, 4, 1, 1);
        written(eyes);
    }
}

// Multiview: one draw renders every layer of an array target, each seeing its own gl_ViewIndex.
public abstract class MultiviewTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    private static readonly Color s_red = new(1f, 0f, 0f, 1f);
    private static readonly Color s_green = new(0f, 1f, 0f, 1f);


    [Fact]
    public void OneDraw_IntoAFramebufferOfTwoViews_RendersEachLayerWithItsViewIndex()
    {
        Assert.True(GD.Features.Multiview);
        Texture eyes = RF.CreateTexture(TextureDescription.Texture2D(4, 4, 1, 2, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.RenderTarget | TextureUsage.Sampled));
        Framebuffer fb = RF.CreateFramebuffer(new FramebufferDescription(null, eyes) { ViewCount = 2 });
        GraphicsProgram program = Program();

        GD.RunTestGraph((_, cl) =>
        {
            cl.SetFramebuffer(fb, new TargetLoadStoreOps(AttachmentOps.Clear(Color.Black), AttachmentOps.Loaded));
            cl.SetFullViewport();
            cl.SetShader(program);
            cl.SetVertexSource(VertexSource.None);
            cl.Draw(3);
        });
        GD.WaitForIdle();

        Assert.Equal(2u, fb.OutputDescription.ViewCount);
        Assert.Equal(s_red, ReadTexture<Color>(eyes, 0, 0)[1, 1], ColorFuzzyComparer.Instance);
        Assert.Equal(s_green, ReadTexture<Color>(eyes, 0, 1)[1, 1], ColorFuzzyComparer.Instance);
    }


    [Fact]
    public void GraphTextures_WithArrayLayers_RenderEveryLayerInOneDraw()
    {
        GraphicsProgram program = Program();
        Texture left = RF.CreateTexture(TextureDescription.Texture2D(4, 4, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Sampled));
        Texture right = RF.CreateTexture(TextureDescription.Texture2D(4, 4, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Sampled));
        uint layers = 0;

        using RenderPipeline pipeline = new([new MultiviewPass(program, left, right, eyes =>
        {
            layers = eyes.ColorTextures[0].ArrayLayers;
        })]);
        GD.DispatchGraph(pipeline, new IRenderView[] { new MultiviewView() });
        GD.WaitForIdle();

        Assert.Equal(2u, layers);
        Assert.Equal(s_red, ReadTexture<Color>(left)[1, 1], ColorFuzzyComparer.Instance);
        Assert.Equal(s_green, ReadTexture<Color>(right)[1, 1], ColorFuzzyComparer.Instance);
    }


    [Fact]
    public void Framebuffers_WithMoreViewsThanLayers_Throw()
    {
        Texture eyes = RF.CreateTexture(TextureDescription.Texture2D(4, 4, 1, 2, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.RenderTarget));

        Assert.Throws<RenderException>(() => RF.CreateFramebuffer(new FramebufferDescription(
            null, [new FramebufferAttachment(eyes, 1)]) { ViewCount = 2 }));
    }


    private GraphicsProgram Program()
        => RF.CreateGraphicsProgram(new ShaderDescription(TestShaderLoader.LoadGraphics(GD.BackendType, "MultiviewColor.glsl"))
        {
            BlendState = BlendStateDescription.SingleOverrideBlend,
            DepthStencilState = DepthStencilStateDescription.Disabled,
            RasterizerState = RasterizerStateDescription.CullNone,
            VertexLayouts = [],
            ResourceLayouts = [],
        });
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanMultiviewTests : MultiviewTests<VulkanDeviceCreator> { }
#endif
