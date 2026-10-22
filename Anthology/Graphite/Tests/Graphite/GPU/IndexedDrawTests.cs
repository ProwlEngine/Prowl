using System;

using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Tests;

// Indexed draws over two quads, the left and right half of the target: explicit index ranges, several
// draws per indirect call, and draw counts the GPU reads from a buffer.
public abstract class IndexedDrawTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    private const uint Size = 8;

    private static readonly Float2[] s_vertices =
    [
        new(-1, -1), new(0, -1), new(0, 1), new(-1, 1),
        new(0, -1), new(1, -1), new(1, 1), new(0, 1),
    ];

    private static readonly uint[] s_indices = [0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7];

    private static readonly IndirectDrawIndexedArguments s_leftQuad = new() { IndexCount = 6, InstanceCount = 1, FirstIndex = 0 };
    private static readonly IndirectDrawIndexedArguments s_rightQuad = new() { IndexCount = 6, InstanceCount = 1, FirstIndex = 6 };


    [Fact]
    public void DrawIndexed_WithAnIndexCount_DrawsOnlyThatRange()
    {
        (bool left, bool right) = Render(cl => cl.DrawIndexed(6, 1, 6, 0, 0));

        Assert.False(left);
        Assert.True(right);
    }


    [Fact]
    public void DrawIndexed_WithoutAnIndexCount_DrawsTheWholeIndexBuffer()
    {
        (bool left, bool right) = Render(cl => cl.DrawIndexed());

        Assert.True(left);
        Assert.True(right);
    }


    [Fact]
    public void DrawIndexedIndirect_IssuesEveryDrawOfTheCall()
    {
        Assert.True(GD.Features.MultiDrawIndirect);
        DeviceBuffer args = Arguments(s_leftQuad, s_rightQuad);

        (bool left, bool right) = Render(cl => cl.DrawIndexedIndirect(args, 0, 2, Stride));

        Assert.True(left);
        Assert.True(right);
    }


    [Fact]
    public void DrawIndexedIndirectCount_IssuesTheCountFromTheBuffer()
    {
        Assert.True(GD.Features.DrawIndirectCount);
        DeviceBuffer args = Arguments(s_leftQuad, s_rightQuad);
        DeviceBuffer count = Count(1);

        (bool left, bool right) = Render(cl => cl.DrawIndexedIndirectCount(args, 0, count, 0, 2, Stride));

        Assert.True(left);
        Assert.False(right);
    }


    [Fact]
    public void DrawIndexedIndirectCount_StopsAtTheMaximum()
    {
        DeviceBuffer args = Arguments(s_leftQuad, s_rightQuad);
        DeviceBuffer count = Count(2);

        (bool left, bool right) = Render(cl => cl.DrawIndexedIndirectCount(args, 0, count, 0, 1, Stride));

        Assert.True(left);
        Assert.False(right);
    }


    [Fact]
    public void DrawIndirectCount_WithACountBufferMissingTheIndirectUsage_Throws()
    {
        DeviceBuffer args = Arguments(s_leftQuad);
        DeviceBuffer count = RF.CreateBuffer(new BufferDescription(16, BufferUsage.StructuredBufferReadOnly));

        Assert.Throws<RenderException>(() => Render(cl => cl.DrawIndexedIndirectCount(args, 0, count, 0, 1, Stride)));
    }


    private static unsafe uint Stride => (uint)sizeof(IndirectDrawIndexedArguments);


    private DeviceBuffer Arguments(params IndirectDrawIndexedArguments[] commands)
    {
        DeviceBuffer buffer = RF.CreateBuffer(new BufferDescription(Stride * (uint)commands.Length, BufferUsage.IndirectBuffer));
        GD.UpdateBuffer(buffer, 0, commands);
        return buffer;
    }


    private DeviceBuffer Count(uint count)
    {
        DeviceBuffer buffer = RF.CreateBuffer(new BufferDescription(16, BufferUsage.IndirectBuffer));
        GD.UpdateBuffer(buffer, 0, new[] { count, 0u, 0u, 0u });
        return buffer;
    }


    private (bool Left, bool Right) Render(Action<CommandBuffer> draw)
    {
        Texture target = RF.CreateTexture(TextureDescription.Texture2D(
            Size, Size, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.RenderTarget | TextureUsage.Sampled));
        Framebuffer fb = RF.CreateFramebuffer(new FramebufferDescription(null, target));

        GraphicsProgram program = RF.CreateGraphicsProgram(new ShaderDescription(TestShaderLoader.LoadGraphics(GD.BackendType, "SolidQuads.glsl"))
        {
            BlendState = BlendStateDescription.SingleOverrideBlend,
            DepthStencilState = DepthStencilStateDescription.Disabled,
            RasterizerState = RasterizerStateDescription.CullNone,
            VertexLayouts = [new VertexLayoutDescription(0, 8, new VertexElementDescription("POSITION", VertexElementFormat.Float2))],
            ResourceLayouts = [],
        });

        DeviceBuffer vb = RF.CreateBuffer(new BufferDescription((uint)(s_vertices.Length * 8), BufferUsage.VertexBuffer));
        GD.UpdateBuffer(vb, 0, s_vertices);
        DeviceBuffer ib = RF.CreateBuffer(new BufferDescription((uint)(s_indices.Length * 4), BufferUsage.IndexBuffer));
        GD.UpdateBuffer(ib, 0, s_indices);

        VertexSource source = new VertexSource(PrimitiveTopology.TriangleList)
            .SetBuffer("POSITION", vb)
            .SetIndexBuffer(ib, IndexFormat.UInt32, (uint)s_indices.Length);

        Submit(cl =>
        {
            cl.SetFramebuffer(fb, new TargetLoadStoreOps(AttachmentOps.Clear(Color.Black), AttachmentOps.Loaded));
            cl.SetFullViewport();
            cl.SetShader(program);
            cl.SetVertexSource(source);
            draw(cl);
        });

        TexelData<Color> map = ReadTexture<Color>(target);
        return (map[Size / 4, Size / 2].R > 0.5f, map[Size * 3 / 4, Size / 2].R > 0.5f);
    }


    private void Submit(Action<CommandBuffer> record)
    {
        GD.RunTestGraph((context, cl) => record(cl));
        GD.WaitForIdle();
    }
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanIndexedDrawTests : IndexedDrawTests<VulkanDeviceCreator> { }
#endif
