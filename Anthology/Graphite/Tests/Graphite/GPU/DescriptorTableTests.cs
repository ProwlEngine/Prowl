using System;

using Prowl.Graphite.RenderGraph;
using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Tests;

// Descriptor tables: bindless sampling by index from a partially written table, rewriting a slot between frames,
// buffers in a table, and the checks for missing or mismatched tables and illegal layouts.
public abstract class DescriptorTableTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    private const uint Size = 8;

    private static readonly Float2[] s_vertices =
    [
        new(-1, -1), new(0, -1), new(0, 1), new(-1, 1),
        new(0, -1), new(1, -1), new(1, 1), new(0, 1),
    ];

    private static readonly uint[] s_indices = [0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7];

    private static readonly ResourceLayoutDescription s_tableLayout = new(0,
        new ResourceLayoutElementDescription("Textures", ResourceKind.TextureReadOnly, ShaderStages.Fragment, 0) { ArrayCount = 4 },
        new ResourceLayoutElementDescription("Point", ResourceKind.Sampler, ShaderStages.Fragment, 1),
        new ResourceLayoutElementDescription("Tints", ResourceKind.StructuredBufferReadOnly, ShaderStages.Fragment, 2))
    {
        Table = true,
    };


    [Fact]
    public void Draws_SampleTheSlotTheirIndexPicks_FromAPartiallyWrittenTable()
    {
        Assert.True(GD.Features.DescriptorIndexing);
        DescriptorTable table = Table(white: true);
        table.SetTexture("Textures", 1, Solid(255, 0, 0));
        table.SetTexture("Textures", 3, Solid(0, 0, 255));

        (Color left, Color right) = Render(table, leftIndex: 1, rightIndex: 3);

        Assert.Equal(new Color(1f, 0f, 0f, 1f), left, ColorFuzzyComparer.Instance);
        Assert.Equal(new Color(0f, 0f, 1f, 1f), right, ColorFuzzyComparer.Instance);
    }


    [Fact]
    public void Slots_RewrittenAfterAFrame_AreReadByTheNextOne()
    {
        DescriptorTable table = Table(white: true);
        table.SetTexture("Textures", 0, Solid(255, 0, 0));
        Render(table, 0, 0);

        table.SetTexture("Textures", 0, Solid(0, 255, 0));
        (Color left, _) = Render(table, 0, 0);

        Assert.Equal(new Color(0f, 1f, 0f, 1f), left, ColorFuzzyComparer.Instance);
    }


    [Fact]
    public void Buffers_InTheTable_ReachTheShader()
    {
        DescriptorTable table = Table(white: false);
        table.SetTexture("Textures", 0, Solid(255, 255, 255));

        (Color left, _) = Render(table, 0, 0);

        Assert.Equal(new Color(0.5f, 0.5f, 0.5f, 1f), left, ColorFuzzyComparer.Instance);
    }


    [Fact]
    public void Draws_WithoutTheTableBound_Throw()
    {
        GraphicsProgram program = Program(s_tableLayout);
        Assert.Throws<RenderException>(() => Draw(program, null, cl => cl.DrawIndexed(6, 1, 0, 0, 0)));
    }


    [Fact]
    public void Draws_WithATableOfAnotherLayout_Throw()
    {
        ResourceLayoutDescription other = s_tableLayout with
        {
            Elements = [s_tableLayout.Elements[0] with { ArrayCount = 8 }, s_tableLayout.Elements[1], s_tableLayout.Elements[2]],
        };
        DescriptorTable table = RF.CreateDescriptorTable(other);

        Assert.Throws<RenderException>(() => Draw(Program(s_tableLayout), table, cl => cl.DrawIndexed(6, 1, 0, 0, 0)));
    }


    [Fact]
    public void Arrays_OutsideATableSet_Throw()
    {
        Assert.Throws<RenderException>(() => Program(s_tableLayout with { Table = false }));
    }


    [Fact]
    public void UniformBuffers_InATable_Throw()
    {
        ResourceLayoutDescription withUniforms = new(0, new ResourceLayoutElementDescription("Block", ResourceKind.UniformBuffer, ShaderStages.Fragment, 0)) { Table = true };
        Assert.Throws<RenderException>(() => RF.CreateDescriptorTable(withUniforms));
    }


    [Fact]
    public void Writes_PastTheSlotsOrOfTheWrongKind_Throw()
    {
        DescriptorTable table = RF.CreateDescriptorTable(s_tableLayout);
        Assert.Throws<RenderException>(() => table.SetTexture("Textures", 4, Solid(0, 0, 0)));
        Assert.Throws<RenderException>(() => table.SetSampler("Textures", 0, RF.CreateSampler(SamplerDescription.Point)));
        Assert.Throws<RenderException>(() => table.SetTexture("Missing", 0, Solid(0, 0, 0)));
    }


    private DescriptorTable Table(bool white)
    {
        DescriptorTable table = RF.CreateDescriptorTable(s_tableLayout);
        DeviceBuffer tints = RF.CreateBuffer(new BufferDescription(16, BufferUsage.StructuredBufferReadOnly));
        GD.UpdateBuffer(tints, 0, white ? new Float4(1, 1, 1, 1) : new Float4(0.5f, 0.5f, 0.5f, 1));
        table.SetSampler("Point", 0, RF.CreateSampler(SamplerDescription.Point));
        table.SetBuffer("Tints", 0, tints);
        return table;
    }


    private Texture Solid(byte r, byte g, byte b)
    {
        Texture texture = RF.CreateTexture(TextureDescription.Texture2D(1, 1, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
        GD.UpdateTexture(texture, new byte[] { r, g, b, 255 }, new TextureRegion(0, 0, 0, 1, 1, 1));
        return texture;
    }


    private GraphicsProgram Program(ResourceLayoutDescription layout)
        => RF.CreateGraphicsProgram(new ShaderDescription(TestShaderLoader.LoadGraphics(GD.BackendType, "BindlessQuads.glsl"))
        {
            BlendState = BlendStateDescription.SingleOverrideBlend,
            DepthStencilState = DepthStencilStateDescription.Disabled,
            RasterizerState = RasterizerStateDescription.CullNone,
            VertexLayouts = [new VertexLayoutDescription(0, 8, new VertexElementDescription("POSITION", VertexElementFormat.Float2))],
            ResourceLayouts = [layout],
            PushConstants = new PushConstantRange(ShaderStages.Fragment, 4),
        });


    private (Color Left, Color Right) Render(DescriptorTable table, uint leftIndex, uint rightIndex)
    {
        GraphicsProgram program = Program(s_tableLayout);
        Texture target = Draw(program, table, cl =>
        {
            cl.SetPushConstants(leftIndex);
            cl.DrawIndexed(6, 1, 0, 0, 0);
            cl.SetPushConstants(rightIndex);
            cl.DrawIndexed(6, 1, 6, 0, 0);
        });

        TexelData<Color> map = ReadTexture<Color>(target);
        return (map[Size / 4, Size / 2], map[Size * 3 / 4, Size / 2]);
    }


    private Texture Draw(GraphicsProgram program, DescriptorTable? table, Action<CommandBuffer> draw)
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
            cl.SetShader(program);
            cl.SetDescriptorTable(0, table);
            draw(cl);
        });
        GD.WaitForIdle();
        return target;
    }
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanDescriptorTableTests : DescriptorTableTests<VulkanDeviceCreator> { }
#endif
