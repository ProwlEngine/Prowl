using System;

using Prowl.Graphite.RenderGraph;
using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Tests;

public abstract class FramebufferTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    [Fact]
    public void ClearColorTarget_OutOfRange_Fails()
    {
        TextureDescription desc = TextureDescription.Texture2D(
            1024, 1024, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.RenderTarget);
        Texture colorTarget0 = RF.CreateTexture(desc);
        Texture colorTarget1 = RF.CreateTexture(desc);
        Framebuffer fb = RF.CreateFramebuffer(new FramebufferDescription(null, colorTarget0, colorTarget1));

        GD.RunTestGraph((context, cl) =>
        {
                        cl.SetFramebuffer(fb, new TargetLoadStoreOps(AttachmentOps.Clear(Color.Red), AttachmentOps.Loaded));
            cl.ClearColorTarget(1, Color.Red);
            Assert.Throws<RenderException>(() => cl.ClearColorTarget(2, Color.Red));
            Assert.Throws<RenderException>(() => cl.ClearColorTarget(3, Color.Red));
        });
    }

    [Fact]
    public void NonZeroMipLevel_ClearColor_Succeeds()
    {
        Texture testTex = RF.CreateTexture(
            TextureDescription.Texture2D(1024, 1024, 11, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.RenderTarget));

        Framebuffer[] framebuffers = new Framebuffer[11];
        for (uint level = 0; level < 11; level++)
        {
            framebuffers[level] = RF.CreateFramebuffer(
                new FramebufferDescription(null, [new FramebufferAttachment(testTex, 0, level)]));
        }

        GD.RunTestGraph((context, cl) =>
        {
            for (uint level = 0; level < 11; level++)
            {
                cl.SetFramebuffer(framebuffers[level]);
                cl.ClearColorTarget(0, new Color(level, level, level, 1));
            }
        });
        GD.WaitForIdle();

        uint mipWidth = 1024;
        uint mipHeight = 1024;
        for (uint level = 0; level < 11; level++)
        {
            TexelData<Color> readView = ReadTexture<Color>(testTex, level);
            for (uint y = 0; y < mipHeight; y++)
                for (uint x = 0; x < mipWidth; x++)
                {
                    Assert.Equal(new Color(level, level, level, 1), readView[x, y]);
                }

            mipWidth = Math.Max(1, mipWidth / 2);
            mipHeight = Math.Max(1, mipHeight / 2);
        }
    }
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanFramebufferTests : FramebufferTests<VulkanDeviceCreator> { }
#endif
