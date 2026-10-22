using System;

using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Tests;

// Push constants reaching draws and dispatches: per draw values, values kept across a program change,
// partial writes at an offset, and the size checks.
public abstract class PushConstantTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    private const uint Size = 8;

    private static readonly Float2[] s_vertices =
    [
        new(-1, -1), new(0, -1), new(0, 1), new(-1, 1),
        new(0, -1), new(1, -1), new(1, 1), new(0, 1),
    ];

    private static readonly uint[] s_indices = [0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7];


    [Fact]
    public void Draws_ReadTheValuesSetBeforeThem()
    {
        GraphicsProgram program = ColorProgram();

        (Color left, Color right) = Render((cl, _) =>
        {
            cl.SetShader(program);
            cl.SetPushConstants(new Float4(1, 0, 0, 1));
            cl.DrawIndexed(6, 1, 0, 0, 0);
            cl.SetPushConstants(new Float4(0, 1, 0, 1));
            cl.DrawIndexed(6, 1, 6, 0, 0);
        });

        Assert.Equal(new Color(1f, 0f, 0f, 1f), left, ColorFuzzyComparer.Instance);
        Assert.Equal(new Color(0f, 1f, 0f, 1f), right, ColorFuzzyComparer.Instance);
    }


    [Fact]
    public void Values_AreKeptAcrossAProgramChange()
    {
        GraphicsProgram first = ColorProgram();
        GraphicsProgram second = ColorProgram(new RasterizerStateDescription(FaceCullMode.None, FrontFace.CounterClockwise, true, false));

        (Color left, Color right) = Render((cl, _) =>
        {
            cl.SetPushConstants(new Float4(0, 0, 1, 1));
            cl.SetShader(first);
            cl.DrawIndexed(6, 1, 0, 0, 0);
            cl.SetShader(second);
            cl.DrawIndexed(6, 1, 6, 0, 0);
        });

        Assert.Equal(new Color(0f, 0f, 1f, 1f), left, ColorFuzzyComparer.Instance);
        Assert.Equal(new Color(0f, 0f, 1f, 1f), right, ColorFuzzyComparer.Instance);
    }


    [Fact]
    public void Dispatch_ReadsTheValues_IncludingAWriteAtAnOffset()
    {
        DeviceBuffer output = RF.CreateBuffer(new BufferDescription(16, BufferUsage.StructuredBufferReadWrite));
        ComputeProgram program = RF.CreateComputeProgram(new ComputeDescription(
            TestShaderLoader.LoadCompute(GD.BackendType, "PushToBuffer.glsl"),
            [new ResourceLayoutDescription(new ResourceLayoutElementDescription("Output", ResourceKind.StructuredBufferReadWrite, ShaderStages.Compute, 0))],
            1, 1, 1)
        {
            PushConstants = new PushConstantRange(ShaderStages.Compute, 16),
        });

        PropertySet properties = new();
        properties.SetBuffer("Output", output);

        GD.RunTestGraph((_, cl) =>
        {
            cl.SetComputeShader(program);
            cl.SetProperties(properties);
            cl.SetPushConstants(new Int4(1, 2, 3, 4));
            cl.SetPushConstants(40u, 8);
            cl.Dispatch(1, 1, 1);
        });
        GD.WaitForIdle();

        DeviceBuffer readback = GetReadback(output);
        Span<uint> map = GD.Map<uint>(readback);
        Assert.Equal([1u, 2u, 40u, 4u], map[..4].ToArray());
        GD.Unmap(readback);
    }


    [Fact]
    public void Writes_PastTheCapacity_Throw()
    {
        Assert.Throws<RenderException>(() => GD.RunTestGraph((_, cl) => cl.SetPushConstants(new Float4x4(), CommandBuffer.PushConstantCapacity - 32)));
    }


    [Fact]
    public void Programs_WithAnOversizedBlock_Throw()
    {
        Assert.Throws<RenderException>(() => RF.CreateComputeProgram(new ComputeDescription(
            TestShaderLoader.LoadCompute(GD.BackendType, "PushToBuffer.glsl"),
            [new ResourceLayoutDescription(new ResourceLayoutElementDescription("Output", ResourceKind.StructuredBufferReadWrite, ShaderStages.Compute, 0))],
            1, 1, 1)
        {
            PushConstants = new PushConstantRange(ShaderStages.Compute, CommandBuffer.PushConstantCapacity + 4),
        }));
    }


    private GraphicsProgram ColorProgram(RasterizerStateDescription? raster = null)
        => RF.CreateGraphicsProgram(new ShaderDescription(TestShaderLoader.LoadGraphics(GD.BackendType, "PushColorQuads.glsl"))
        {
            BlendState = BlendStateDescription.SingleOverrideBlend,
            DepthStencilState = DepthStencilStateDescription.Disabled,
            RasterizerState = raster ?? RasterizerStateDescription.CullNone,
            VertexLayouts = [new VertexLayoutDescription(0, 8, new VertexElementDescription("POSITION", VertexElementFormat.Float2))],
            ResourceLayouts = [],
            PushConstants = new PushConstantRange(ShaderStages.Fragment, 16),
        });


    private (Color Left, Color Right) Render(Action<CommandBuffer, VertexSource> draw)
    {
        Texture target = RF.CreateTexture(TextureDescription.Texture2D(
            Size, Size, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.RenderTarget | TextureUsage.Sampled));
        Framebuffer fb = RF.CreateFramebuffer(new FramebufferDescription(null, target));

        DeviceBuffer vb = RF.CreateBuffer(new BufferDescription((uint)(s_vertices.Length * 8), BufferUsage.VertexBuffer));
        GD.UpdateBuffer(vb, 0, s_vertices);
        DeviceBuffer ib = RF.CreateBuffer(new BufferDescription((uint)(s_indices.Length * 4), BufferUsage.IndexBuffer));
        GD.UpdateBuffer(ib, 0, s_indices);

        VertexSource source = new VertexSource(PrimitiveTopology.TriangleList)
            .SetBuffer("POSITION", vb)
            .SetIndexBuffer(ib, IndexFormat.UInt32, (uint)s_indices.Length);

        GD.RunTestGraph((_, cl) =>
        {
            cl.SetFramebuffer(fb, new TargetLoadStoreOps(AttachmentOps.Clear(Color.Black), AttachmentOps.Loaded));
            cl.SetFullViewport();
            cl.SetVertexSource(source);
            draw(cl, source);
        });
        GD.WaitForIdle();

        TexelData<Color> map = ReadTexture<Color>(target);
        return (map[Size / 4, Size / 2], map[Size * 3 / 4, Size / 2]);
    }
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanPushConstantTests : PushConstantTests<VulkanDeviceCreator> { }
#endif
