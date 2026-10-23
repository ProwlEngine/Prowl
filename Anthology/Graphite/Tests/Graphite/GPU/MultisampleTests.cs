using System;

using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Tests;

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

        using FrameGraph graph = new(GD, "Test");
        TextureHandle msaa = graph.Create("Msaa", new GraphTextureDesc
        {
            Width = 8,
            Height = 8,
            ColorFormats = [PixelFormat.R32_G32_B32_A32_Float],
            SampleCount = TextureSampleCount.Count4,
        });
        graph.AddPass("Msaa", pass => pass.Write(msaa, TextureState.Attachment | TextureState.TransferSrc), context =>
        {
            CommandBuffer cmd = context.Command;
            RenderTexture color = context.Get(msaa);
            cmd.SetFramebuffer(color.Framebuffer, new TargetLoadStoreOps(AttachmentOps.Clear(Color.Black), AttachmentOps.Loaded));
            cmd.SetFullViewport();
            cmd.SetShader(program);
            cmd.SetVertexSource(new VertexSource(PrimitiveTopology.TriangleList).SetBuffer("POSITION", triangle));
            cmd.Draw(3);
            cmd.ResolveTexture(color.ColorTextures[0], resolved);
            drawnSamples = color.ColorTextures[0].SampleCount;
        });
        graph.Execute();
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
