using System;

using Prowl.Graphite.RenderGraph;
using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Tests;

public abstract class FramebufferTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    [Fact]
    public void NoDepthTarget_ClearAllColors_Succeeds()
    {
        Texture colorTarget = RF.CreateTexture(
            TextureDescription.Texture2D(1024, 1024, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.RenderTarget));
        Framebuffer fb = RF.CreateFramebuffer(new FramebufferDescription(null, colorTarget));

        GD.RunTestGraph(context =>
        {
            CommandBuffer cl = context.GetCommandBuffer();
            cl.SetFramebuffer(fb, new TargetLoadStoreOps(AttachmentOps.Clear(Color.Red), AttachmentOps.Loaded));
            context.SubmitCommandBuffer(cl);
        });
        GD.WaitForIdle();

        TexelData<Color> view = ReadTexture<Color>(colorTarget);
        for (int i = 0; i < view.Length; i++)
        {
            Assert.Equal(Color.Red, view[i]);
        }
    }

    [Fact]
    public void NoDepthTarget_ClearDepth_Fails()
    {
        Texture colorTarget = RF.CreateTexture(
            TextureDescription.Texture2D(1024, 1024, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.RenderTarget));
        Framebuffer fb = RF.CreateFramebuffer(new FramebufferDescription(null, colorTarget));

        GD.RunTestGraph(context =>
        {
            CommandBuffer cl = context.GetCommandBuffer();
            cl.SetFramebuffer(fb);
            Assert.Throws<RenderException>(() => cl.ClearDepthStencil(1f));
        });
    }

    [Fact]
    public void NoColorTarget_ClearColor_Fails()
    {
        Texture depthTarget = RF.CreateTexture(
            TextureDescription.Texture2D(1024, 1024, 1, 1, PixelFormat.R16_UNorm, TextureUsage.DepthStencil));
        Framebuffer fb = RF.CreateFramebuffer(new FramebufferDescription(depthTarget));

        GD.RunTestGraph(context =>
        {
            CommandBuffer cl = context.GetCommandBuffer();
            cl.SetFramebuffer(fb);
            Assert.Throws<RenderException>(() => cl.ClearColorTarget(0, Color.Red));
        });
    }

    [Fact]
    public void ClearColorTarget_OutOfRange_Fails()
    {
        TextureDescription desc = TextureDescription.Texture2D(
            1024, 1024, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.RenderTarget);
        Texture colorTarget0 = RF.CreateTexture(desc);
        Texture colorTarget1 = RF.CreateTexture(desc);
        Framebuffer fb = RF.CreateFramebuffer(new FramebufferDescription(null, colorTarget0, colorTarget1));

        GD.RunTestGraph(context =>
        {
            CommandBuffer cl = context.GetCommandBuffer();
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

        GD.RunTestGraph(context =>
        {
            CommandBuffer cl = context.GetCommandBuffer();
            for (uint level = 0; level < 11; level++)
            {
                cl.SetFramebuffer(framebuffers[level]);
                cl.ClearColorTarget(0, new Color(level, level, level, 1));
            }
            context.SubmitCommandBuffer(cl);
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

    // OutputDescription is no longer carried by a pipeline (see README "Pipeline API"); it is
    // derived from the framebuffer and used by the Vulkan pipeline cache and user-side caching.
    [Fact]
    public void OutputDescription_ColorOnly_HasNoDepth()
    {
        Texture color = RF.CreateTexture(TextureDescription.Texture2D(
            64, 32, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.RenderTarget));
        Framebuffer fb = RF.CreateFramebuffer(new FramebufferDescription(null, color));

        Assert.Equal(64u, fb.Width);
        Assert.Equal(32u, fb.Height);
        Assert.Null(fb.DepthTarget);
        Assert.Single(fb.ColorTargets);

        OutputDescription output = fb.OutputDescription;
        Assert.Null(output.DepthFormat);
        Assert.Single(output.ColorFormats);
        Assert.Equal(PixelFormat.R8_G8_B8_A8_UNorm, output.ColorFormats[0]);
        Assert.Equal(TextureSampleCount.Count1, output.SampleCount);
    }

    [Fact]
    public void OutputDescription_ColorAndDepth_ExposesBoth()
    {
        Texture color = RF.CreateTexture(TextureDescription.Texture2D(
            48, 48, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.RenderTarget));
        Texture depth = RF.CreateTexture(TextureDescription.Texture2D(
            48, 48, 1, 1, PixelFormat.R16_UNorm, TextureUsage.DepthStencil));
        Framebuffer fb = RF.CreateFramebuffer(new FramebufferDescription(depth, color));

        OutputDescription output = fb.OutputDescription;
        Assert.NotNull(output.DepthFormat);
        Assert.Equal(PixelFormat.R16_UNorm, output.DepthFormat.Value);
        Assert.Equal(PixelFormat.R8_G8_B8_A8_UNorm, output.ColorFormats[0]);
    }
}

public abstract class SwapchainFramebufferTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    [Fact]
    public void ClearSwapchainFramebuffer_Succeeds()
    {
        GD.RunTestGraph(context =>
        {
            CommandBuffer cl = context.GetCommandBuffer();
            cl.SetFramebuffer(GD.MainSwapchain.Framebuffer, TargetLoadStoreOps.Clear(Color.Red, 1f));
            context.SubmitCommandBuffer(cl);
        });
    }
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanFramebufferTests : FramebufferTests<VulkanDeviceCreator> { }
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanSwapchainFramebufferTests : SwapchainFramebufferTests<VulkanDeviceCreatorWithMainSwapchain> { }
#endif
