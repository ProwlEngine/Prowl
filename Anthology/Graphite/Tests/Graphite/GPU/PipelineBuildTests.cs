using System;
using System.Threading.Tasks;

using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Tests;

// Pipelines built ahead of the first draw, from several threads, and the driver graph cache carried into a new device.
public abstract class PipelineBuildTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    private const uint Size = 4;

    private static readonly Float2[] s_quad = [new(-1, -1), new(1, -1), new(1, 1), new(-1, -1), new(1, 1), new(-1, 1)];


    [Fact]
    public void BuildPipeline_IsUsedByTheFirstDraw()
    {
        GraphicsProgram program = Program(GD);
        (Texture target, Framebuffer fb) = Target(GD);
        long before = GD.Counters.Snapshot().Live(AllocBin.Pipeline);

        program.BuildPipeline(fb.OutputDescription, PrimitiveTopology.TriangleList);
        Assert.Equal(before + 1, GD.Counters.Snapshot().Live(AllocBin.Pipeline));

        Draw(GD, program, fb);

        Assert.Equal(before + 1, GD.Counters.Snapshot().Live(AllocBin.Pipeline));
        Assert.Equal(new Color(1f, 1f, 1f, 1f), ReadTexture<Color>(target)[1, 1], ColorFuzzyComparer.Instance);
    }


    [Fact]
    public void BuildPipeline_FromManyThreadsAtOnce_BuildsItOnce()
    {
        GraphicsProgram program = Program(GD);
        (_, Framebuffer fb) = Target(GD);
        OutputDescription outputs = fb.OutputDescription;
        long before = GD.Counters.Snapshot().Live(AllocBin.Pipeline);

        Parallel.For(0, 16, _ => program.BuildPipeline(outputs, PrimitiveTopology.TriangleList));

        Assert.Equal(before + 1, GD.Counters.Snapshot().Live(AllocBin.Pipeline));
    }


    [Fact]
    public void PipelineCacheData_FromOneDevice_SeedsTheNext()
    {
        GraphicsProgram program = Program(GD);
        (_, Framebuffer fb) = Target(GD);
        program.BuildPipeline(fb.OutputDescription, PrimitiveTopology.TriangleList);

        byte[] data = GD.GetPipelineCacheData();
        Assert.True(data.Length > 32);

        using GraphicsDevice seeded = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true) { PipelineCacheData = data });
        (Texture target, Framebuffer seededFb) = Target(seeded);
        Draw(seeded, Program(seeded), seededFb);
        Assert.Equal(new Color(1f, 1f, 1f, 1f), ReadTexture(seeded, target)[1, 1], ColorFuzzyComparer.Instance);
    }


    [Fact]
    public void PipelineCacheData_ThatIsNotACache_IsIgnored()
    {
        byte[] garbage = new byte[256];
        new Random(7).NextBytes(garbage);

        using GraphicsDevice device = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true) { PipelineCacheData = garbage });
        (Texture target, Framebuffer fb) = Target(device);
        Draw(device, Program(device), fb);

        Assert.Equal(new Color(1f, 1f, 1f, 1f), ReadTexture(device, target)[1, 1], ColorFuzzyComparer.Instance);
    }


    private static TexelData<Color> ReadTexture(GraphicsDevice device, Texture texture)
    {
        uint size = Size * Size * 16;
        DeviceBuffer buffer = device.ResourceFactory.CreateBuffer(new BufferDescription(size, BufferUsage.Staging));
        TextureRegion region = TextureRegion.Whole(texture, 0, 0);
        device.Record(cmd => cmd.CopyTextureToBuffer(texture, buffer, 0, region)).Wait();
        Color[] texels = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, Color>(device.Map(buffer).Slice(0, (int)size)).ToArray();
        device.Unmap(buffer);
        return new TexelData<Color>(texels, Size, Size, 1);
    }


    private static GraphicsProgram Program(GraphicsDevice device)
        => device.ResourceFactory.CreateGraphicsProgram(new ShaderDescription(TestShaderLoader.LoadGraphics(device.BackendType, "SolidQuads.glsl"))
        {
            BlendState = BlendStateDescription.SingleOverrideBlend,
            DepthStencilState = DepthStencilStateDescription.Disabled,
            RasterizerState = RasterizerStateDescription.CullNone,
            VertexLayouts = [new VertexLayoutDescription(0, 8, new VertexElementDescription("POSITION", VertexElementFormat.Float2))],
            ResourceLayouts = [],
        });


    private static (Texture Target, Framebuffer Framebuffer) Target(GraphicsDevice device)
    {
        Texture target = device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
            Size, Size, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.RenderTarget | TextureUsage.Sampled));
        return (target, device.ResourceFactory.CreateFramebuffer(new FramebufferDescription(null, target)));
    }


    private static void Draw(GraphicsDevice device, GraphicsProgram program, Framebuffer fb)
    {
        DeviceBuffer vb = device.ResourceFactory.CreateBuffer(new BufferDescription((uint)(s_quad.Length * 8), BufferUsage.VertexBuffer));
        device.UpdateBuffer(vb, 0, s_quad);
        VertexSource source = new VertexSource(PrimitiveTopology.TriangleList).SetBuffer("POSITION", vb);

        device.RunTestGraph((_, cl) =>
        {
            cl.SetFramebuffer(fb, new TargetLoadStoreOps(AttachmentOps.Clear(Color.Black), AttachmentOps.Loaded));
            cl.SetFullViewport();
            cl.SetShader(program);
            cl.SetVertexSource(source);
            cl.Draw((uint)s_quad.Length);
        });
        device.WaitForIdle();
    }
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanPipelineBuildTests : PipelineBuildTests<VulkanDeviceCreator> { }
#endif
