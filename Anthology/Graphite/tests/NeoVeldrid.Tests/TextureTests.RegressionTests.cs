using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Xunit;

namespace NeoVeldrid.Tests
{
    // Regression tests for specific bugs that have been fixed in NeoVeldrid. Each test in this
    // file exists to prevent a particular past bug from coming back; they are not part of the
    // general behavioral coverage in TextureTests.cs and should not be relied on as
    // documentation of the public contract. The test name and a comment above each test should
    // describe the bug it guards against.
    //
    // This is a partial of TextureTestBase<T>, so any test added here is automatically picked
    // up by every per-backend concrete subclass declared at the bottom of TextureTests.cs
    // (VulkanTextureTests, D3D11TextureTests, OpenGLTextureTests, OpenGLESTextureTests).
    public abstract partial class TextureTestBase<T> where T : GraphicsDeviceCreator
    {
        // Regression test for a Vulkan backend bug where R16_G16_Float and R32_G32_Float were
        // mapped to their 4-component VK_FORMAT_..._SFLOAT counterparts, doubling the per-pixel
        // byte size of every texture in those formats. Pure upload-then-readback round-trips
        // could hide the bug because the wrong stride was applied symmetrically on both copies.
        // Clearing the render target exposes it: the clear color carries B and A components
        // that should be discarded for a 2-component target, so any leakage of B/A into the
        // readback proves the underlying texture has the wrong number of components.
        [Fact]
        public void ClearColorTarget_R32_G32_Float_OnlyWritesTwoComponents()
        {
            const uint width = 4;
            const uint height = 1;

            Texture target = RF.CreateTexture(TextureDescription.Texture2D(
                width, height, 1, 1, PixelFormat.R32_G32_Float, TextureUsage.RenderTarget));
            Framebuffer fb = RF.CreateFramebuffer(new FramebufferDescription(null, target));

            CommandList cl = RF.CreateCommandList();
            cl.Begin();
            cl.SetFramebuffer(fb);
            // 999f and 7777f are deliberately distinctive sentinels for the B and A channels.
            // The target is R32_G32_Float, so by the contract those two values must be ignored
            // and every pixel must read back as exactly (3, 5). The sentinels ensure that any
            // accidental leakage of B/A into the readback is loud and unambiguous.
            cl.ClearColorTarget(0, new RgbaFloat(3f, 5f, 999f, 7777f));
            cl.End();
            GD.SubmitCommands(cl);
            GD.WaitForIdle();

            Texture staging = RF.CreateTexture(TextureDescription.Texture2D(
                width, height, 1, 1, PixelFormat.R32_G32_Float, TextureUsage.Staging));
            cl.Begin();
            cl.CopyTexture(target, 0, 0, 0, 0, 0, staging, 0, 0, 0, 0, 0, width, height, 1, 1);
            cl.End();
            GD.SubmitCommands(cl);
            GD.WaitForIdle();

            MappedResourceView<Vector2> view = GD.Map<Vector2>(staging, MapMode.Read);
            try
            {
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        Assert.Equal(new Vector2(3f, 5f), view[x, y]);
                    }
                }
            }
            finally
            {
                GD.Unmap(staging);
            }

            cl.Dispose();
            fb.Dispose();
            staging.Dispose();
            target.Dispose();
        }

        [Fact]
        public void ClearColorTarget_R16_G16_Float_OnlyWritesTwoComponents()
        {
            const uint width = 4;
            const uint height = 1;

            Texture target = RF.CreateTexture(TextureDescription.Texture2D(
                width, height, 1, 1, PixelFormat.R16_G16_Float, TextureUsage.RenderTarget));
            Framebuffer fb = RF.CreateFramebuffer(new FramebufferDescription(null, target));

            CommandList cl = RF.CreateCommandList();
            cl.Begin();
            cl.SetFramebuffer(fb);
            // 999f and 7777f are deliberately distinctive sentinels for the B and A channels:
            // the target is R16_G16_Float, so by the contract those two values must be ignored
            // and every pixel must read back as exactly (1, 2). All four values are exactly
            // representable in IEEE-754 binary16, so the float32 -> float16 narrowing the clear
            // path performs is lossless and would faithfully preserve the sentinels if a bug
            // ever let them leak into the readback.
            cl.ClearColorTarget(0, new RgbaFloat(1f, 2f, 999f, 7777f));
            cl.End();
            GD.SubmitCommands(cl);
            GD.WaitForIdle();

            Texture staging = RF.CreateTexture(TextureDescription.Texture2D(
                width, height, 1, 1, PixelFormat.R16_G16_Float, TextureUsage.Staging));
            cl.Begin();
            cl.CopyTexture(target, 0, 0, 0, 0, 0, staging, 0, 0, 0, 0, 0, width, height, 1, 1);
            cl.End();
            GD.SubmitCommands(cl);
            GD.WaitForIdle();

            MappedResourceView<HalfVector2> view = GD.Map<HalfVector2>(staging, MapMode.Read);
            try
            {
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        Assert.Equal((Half)1f, view[x, y].X);
                        Assert.Equal((Half)2f, view[x, y].Y);
                    }
                }
            }
            finally
            {
                GD.Unmap(staging);
            }

            cl.Dispose();
            fb.Dispose();
            staging.Dispose();
            target.Dispose();
        }

        [StructLayout(LayoutKind.Sequential, Pack = 2)]
        private struct HalfVector2
        {
            public Half X;
            public Half Y;
        }
    }
}
