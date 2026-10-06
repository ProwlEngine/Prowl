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

    [StructLayout(LayoutKind.Sequential)]
    private struct SinkVertex
    {
        public Float3 A;
        public Float4 B;
        public Float2 C;
        public Float4 D;
    }

    [SkippableFact]
    public void GraphicsPrograms_WithSameShader_ShareDescriptorCache()
    {
        Skip.IfNot(GD.BackendType == GraphicsBackend.Vulkan);

        ShaderStageDescription[] stages = TestShaderLoader.LoadGraphics(GD.BackendType, "VertexLayoutTestShader.slang");
        ShaderDescription description = new(stages)
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
        };

        GraphicsProgram first = Inner.CreateGraphicsProgram(description);
        description.BlendState = BlendStateDescription.SingleAlphaBlend;
        GraphicsProgram second = Inner.CreateGraphicsProgram(description);

        Vk.VkGraphicsProgram vkFirst = Assert.IsType<Vk.VkGraphicsProgram>(first);
        Vk.VkGraphicsProgram vkSecond = Assert.IsType<Vk.VkGraphicsProgram>(second);
        Assert.Same(vkFirst.DescriptorCache, vkSecond.DescriptorCache);
        Assert.Equal(vkFirst.PipelineLayout, vkSecond.PipelineLayout);

        first.Dispose();
        second.Dispose();
    }
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanDisposalTests : DisposalTests<VulkanDeviceCreator> { }
#endif
