using System;

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

        CommandBuffer cl = RF.CreateCommandBuffer();
        cl.Begin();
        cl.SetFramebuffer(fb);
        cl.ClearColorTarget(0, Color.Red);
        cl.End();
        { Frame _f = GD.BeginFrame(); _f.SubmitCommands(cl); GD.EndFrame(_f); }
        GD.WaitForIdle();

        Texture staging = RF.CreateTexture(
            TextureDescription.Texture2D(1024, 1024, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Staging));

        cl.Begin();
        cl.CopyTexture(
            colorTarget, 0, 0, 0, 0, 0,
            staging, 0, 0, 0, 0, 0,
            1024, 1024, 1, 1);
        cl.End();
        { Frame _f = GD.BeginFrame(); _f.SubmitCommands(cl); GD.EndFrame(_f); }
        GD.WaitForIdle();

        MappedResourceView<Color> view = GD.Map<Color>(staging, MapMode.Read);
        for (int i = 0; i < view.Count; i++)
        {
            Assert.Equal(Color.Red, view[i]);
        }
        GD.Unmap(staging);
    }

    [Fact]
    public void NoDepthTarget_ClearDepth_Fails()
    {
        Texture colorTarget = RF.CreateTexture(
            TextureDescription.Texture2D(1024, 1024, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.RenderTarget));
        Framebuffer fb = RF.CreateFramebuffer(new FramebufferDescription(null, colorTarget));

        CommandBuffer cl = RF.CreateCommandBuffer();
        cl.Begin();
        cl.SetFramebuffer(fb);
        Assert.Throws<RenderException>(() => cl.ClearDepthStencil(1f));
    }

    [Fact]
    public void NoColorTarget_ClearColor_Fails()
    {
        Texture depthTarget = RF.CreateTexture(
            TextureDescription.Texture2D(1024, 1024, 1, 1, PixelFormat.R16_UNorm, TextureUsage.DepthStencil));
        Framebuffer fb = RF.CreateFramebuffer(new FramebufferDescription(depthTarget));

        CommandBuffer cl = RF.CreateCommandBuffer();
        cl.Begin();
        cl.SetFramebuffer(fb);
        Assert.Throws<RenderException>(() => cl.ClearColorTarget(0, Color.Red));
    }

    [Fact]
    public void ClearColorTarget_OutOfRange_Fails()
    {
        TextureDescription desc = TextureDescription.Texture2D(
            1024, 1024, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.RenderTarget);
        Texture colorTarget0 = RF.CreateTexture(desc);
        Texture colorTarget1 = RF.CreateTexture(desc);
        Framebuffer fb = RF.CreateFramebuffer(new FramebufferDescription(null, colorTarget0, colorTarget1));

        CommandBuffer cl = RF.CreateCommandBuffer();
        cl.Begin();
        cl.SetFramebuffer(fb);
        cl.ClearColorTarget(0, Color.Red);
        cl.ClearColorTarget(1, Color.Red);
        Assert.Throws<RenderException>(() => cl.ClearColorTarget(2, Color.Red));
        Assert.Throws<RenderException>(() => cl.ClearColorTarget(3, Color.Red));
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
                new FramebufferDescription(null, [new FramebufferAttachmentDescription(testTex, 0, level)]));
        }

        CommandBuffer cl = RF.CreateCommandBuffer();
        cl.Begin();
        for (uint level = 0; level < 11; level++)
        {
            cl.SetFramebuffer(framebuffers[level]);
            cl.ClearColorTarget(0, new Color(level, level, level, 1));
        }
        cl.End();
        { Frame _f = GD.BeginFrame(); _f.SubmitCommands(cl); GD.EndFrame(_f); }
        GD.WaitForIdle();

        Texture readback = RF.CreateTexture(
            TextureDescription.Texture2D(1024, 1024, 11, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Staging));
        cl.Begin();
        cl.CopyTexture(testTex, readback);
        cl.End();
        { Frame _f = GD.BeginFrame(); _f.SubmitCommands(cl); GD.EndFrame(_f); }
        GD.WaitForIdle();

        uint mipWidth = 1024;
        uint mipHeight = 1024;
        for (uint level = 0; level < 11; level++)
        {
            MappedResourceView<Color> readView = GD.Map<Color>(readback, MapMode.Read, level);
            for (uint y = 0; y < mipHeight; y++)
                for (uint x = 0; x < mipWidth; x++)
                {
                    Assert.Equal(new Color(level, level, level, 1), readView[x, y]);
                }

            GD.Unmap(readback, level);
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
        Assert.Null(output.DepthAttachment);
        Assert.Single(output.ColorAttachments);
        Assert.Equal(PixelFormat.R8_G8_B8_A8_UNorm, output.ColorAttachments[0].Format);
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
        Assert.NotNull(output.DepthAttachment);
        Assert.Equal(PixelFormat.R16_UNorm, output.DepthAttachment.Value.Format);
        Assert.Equal(PixelFormat.R8_G8_B8_A8_UNorm, output.ColorAttachments[0].Format);
    }
}

public abstract class SwapchainFramebufferTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    [Fact]
    public void ClearSwapchainFramebuffer_Succeeds()
    {
        CommandBuffer cl = RF.CreateCommandBuffer();
        cl.Begin();
        cl.SetFramebuffer(GD.SwapchainFramebuffer);
        cl.ClearColorTarget(0, Color.Red);
        cl.ClearDepthStencil(1f);
        cl.End();
    }
}

#if TEST_OPENGL
[Trait("Backend", "OpenGL")]
[Collection("GPU Tests")]
public class OpenGLFramebufferTests : FramebufferTests<OpenGLDeviceCreator> { }
[Trait("Backend", "OpenGL")]
[Collection("GPU Tests")]
public class OpenGLSwapchainFramebufferTests : SwapchainFramebufferTests<OpenGLDeviceCreator> { }
#endif
#if TEST_OPENGLES
[Trait("Backend", "OpenGLES")]
[Collection("GPU Tests")]
public class OpenGLESFramebufferTests : FramebufferTests<OpenGLESDeviceCreator> { }
[Trait("Backend", "OpenGLES")]
[Collection("GPU Tests")]
public class OpenGLESSwapchainFramebufferTests : SwapchainFramebufferTests<OpenGLESDeviceCreator> { }
#endif
#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanFramebufferTests : FramebufferTests<VulkanDeviceCreator> { }
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanSwapchainFramebufferTests : SwapchainFramebufferTests<VulkanDeviceCreatorWithMainSwapchain> { }
#endif
#if TEST_D3D11
[Trait("Backend", "D3D11")]
[Collection("GPU Tests")]
public class D3D11FramebufferTests : FramebufferTests<D3D11DeviceCreator> { }
[Trait("Backend", "D3D11")]
[Collection("GPU Tests")]
public class D3D11SwapchainFramebufferTests : SwapchainFramebufferTests<D3D11DeviceCreatorWithMainSwapchain> { }
#endif
