using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Tests;

// Verifies that device resources report disposal correctly and that disposing a dependent does
// not dispose its dependencies. Resources are created on the inner factory so the tracking
// factory in the base class does not dispose them a second time.
public abstract class DisposalTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    private ResourceFactory Inner => GD.ResourceFactory;

    [Fact]
    public void Dispose_Buffer()
    {
        DeviceBuffer b = Inner.CreateBuffer(new BufferDescription(256, BufferUsage.VertexBuffer));
        b.Dispose();
        Assert.True(b.IsDisposed);
    }

    [Fact]
    public void Dispose_TextureAndView()
    {
        Texture t = Inner.CreateTexture(TextureDescription.Texture2D(1, 1, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Sampled));
        TextureView tv = Inner.CreateTextureView(t);
        GD.WaitForIdle();

        tv.Dispose();
        Assert.True(tv.IsDisposed);
        Assert.False(t.IsDisposed);

        t.Dispose();
        Assert.True(t.IsDisposed);
    }

    [Fact]
    public void Dispose_Framebuffer_DoesNotDisposeTarget()
    {
        Texture t = Inner.CreateTexture(TextureDescription.Texture2D(1, 1, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.RenderTarget));
        Framebuffer fb = Inner.CreateFramebuffer(new FramebufferDescription(null, t));
        GD.WaitForIdle();

        fb.Dispose();
        Assert.True(fb.IsDisposed);
        Assert.False(t.IsDisposed);

        t.Dispose();
        Assert.True(t.IsDisposed);
    }

    [Fact]
    public void Dispose_CommandBuffer()
    {
        CommandBuffer cl = Inner.CreateCommandBuffer();
        cl.Dispose();
        Assert.True(cl.IsDisposed);
    }

    [Fact]
    public void Dispose_Sampler()
    {
        Sampler s = Inner.CreateSampler(SamplerDescription.Point);
        s.Dispose();
        Assert.True(s.IsDisposed);
    }

    [Fact]
    public void Dispose_Fence()
    {
        Fence f = Inner.CreateFence(false);
        f.Dispose();
        Assert.True(f.IsDisposed);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SinkVertex
    {
        public Float3 A;
        public Float4 B;
        public Float2 C;
        public Float4 D;
    }

    [Fact]
    public void Dispose_GraphicsProgram()
    {
        ShaderStageDescription[] stages = TestShaderLoader.LoadGraphics(GD.BackendType, "VertexLayoutTestShader.slang");
        GraphicsProgram program = Inner.CreateGraphicsProgram(new ShaderDescription(stages)
        {
            BlendState = BlendStateDescription.SingleOverrideBlend,
            DepthStencilState = DepthStencilStateDescription.Disabled,
            RasterizerState = RasterizerStateDescription.CullNone,
            VertexLayouts =
            [
                new VertexLayoutDescription(0, (uint)Unsafe.SizeOf<SinkVertex>(),
                    new VertexElementDescription("POSITION", VertexElementFormat.Float3),
                    new VertexElementDescription("COLOR0", VertexElementFormat.Float4),
                    new VertexElementDescription("TEXCOORD0", VertexElementFormat.Float2),
                    new VertexElementDescription("COLOR1", VertexElementFormat.Float4))
            ],
        });

        program.Dispose();
        Assert.True(program.IsDisposed);
    }

    [SkippableFact]
    public void Dispose_ComputeProgram()
    {
        Skip.IfNot(GD.Features.ComputeShader);

        ShaderStageDescription stage = TestShaderLoader.LoadCompute(GD.BackendType, "ComputeColoredQuadGenerator.slang");
        ComputeProgram program = Inner.CreateComputeProgram(new ComputeDescription(stage,
            [
                new ResourceLayoutDescription
                {
                    Set = 0,
                    Elements = [new ResourceLayoutElementDescription("OutputVertices", ResourceKind.StructuredBufferReadWrite, ShaderStages.Compute, 0)]
                }
            ], 1, 1, 1));

        program.Dispose();
        Assert.True(program.IsDisposed);
    }
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanDisposalTests : DisposalTests<VulkanDeviceCreator> { }
#endif
#if TEST_D3D11
[Trait("Backend", "D3D11")]
[Collection("GPU Tests")]
public class D3D11DisposalTests : DisposalTests<D3D11DeviceCreator> { }
#endif
