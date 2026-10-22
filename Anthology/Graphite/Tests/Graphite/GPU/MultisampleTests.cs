using System;

using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Tests;

file sealed class MultisamplePass(GraphicsProgram program, DeviceBuffer triangle, Texture resolved, Action<Texture> drawn) : IInstancePass
{
    public GraphKind Kind => TestViews.Kind;

    private static readonly RenderResourceID s_color = RenderResourceID.Intern("Msaa");
    private TextureHandle _color;

    public string Name => "Msaa";

    public void Setup(InstanceContextBuilder builder)
        => _color = builder.DeclareOutputTexture(s_color, new GraphTextureDesc
        {
            SizeMode = TextureSizeMode.InstanceRelative,
            Scale = 1f,
            ColorFormats = [PixelFormat.R32_G32_B32_A32_Float],
            SampleCount = TextureSampleCount.Count4,
        }, usage: TextureState.Attachment | TextureState.TransferSrc);

    public void Render(InstanceContext context, CommandBuffer cmd)
    {
        RenderTexture color = context.GetRenderTexture(_color);
        cmd.SetFramebuffer(color.Framebuffer, new TargetLoadStoreOps(AttachmentOps.Clear(Color.Black), AttachmentOps.Loaded));
        cmd.SetFullViewport();
        cmd.SetShader(program);
        cmd.SetVertexSource(new VertexSource(PrimitiveTopology.TriangleList).SetBuffer("POSITION", triangle));
        cmd.Draw(3);
        cmd.ResolveTexture(color.ColorTextures[0], resolved);
        drawn(color.ColorTextures[0]);
    }
}

// MSAA graph textures: attachments with several samples per pixel, resolved into a single sample texture.
public abstract class MultisampleTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    [Fact]
    public void GraphTextures_WithASampleCount_RenderMultisampled_AndResolveEdgesToPartialCoverage()
    {
        GraphicsProgram program = RF.CreateGraphicsProgram(new ShaderDescription(TestShaderLoader.LoadGraphics(GD.BackendType, "SolidQuads.glsl"))
        {
            BlendState = BlendStateDescription.SingleOverrideBlend,
            DepthStencilState = DepthStencilStateDescription.Disabled,
            RasterizerState = RasterizerStateDescription.CullNone,
            VertexLayouts = [new VertexLayoutDescription(0, 8, new VertexElementDescription("POSITION", VertexElementFormat.Float2))],
            ResourceLayouts = [],
        });
        DeviceBuffer triangle = RF.CreateBuffer(new BufferDescription(24, BufferUsage.VertexBuffer));
        GD.UpdateBuffer(triangle, 0, new[] { new Float2(-1, -1), new Float2(1, -1), new Float2(-1, 1) });
        Texture resolved = RF.CreateTexture(TextureDescription.Texture2D(8, 8, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Sampled));
        TextureSampleCount drawnSamples = TextureSampleCount.Count1;

        using RenderGraph graph = TestViews.Build([new MultisamplePass(program, triangle, resolved, texture => drawnSamples = texture.SampleCount)]);
        GD.DispatchGraph(graph, TestViews.Single(graph, new GraphExtent(8, 8)));
        GD.WaitForIdle();

        Assert.Equal(TextureSampleCount.Count4, drawnSamples);
        TexelData<Color> map = ReadTexture<Color>(resolved);
        bool partial = false;
        for (uint y = 0; y < 8; y++)
        {
            for (uint x = 0; x < 8; x++)
                partial |= map[x, y].R > 0.1f && map[x, y].R < 0.9f;
        }
        Assert.True(partial, "No pixel along the triangle's edge resolved to partial coverage.");
    }
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanMultisampleTests : MultisampleTests<VulkanDeviceCreator> { }
#endif
