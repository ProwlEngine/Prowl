using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Tests;

// The device-level transient render-texture pool (GraphicsDevice.RentTransientTexture /
// RentTransientFramebuffer). Covers the recycle model that mirrors the old
// RenderTexture.GetTemporaryRT: desc-keyed reuse once a frame's fence signals, no reuse while a
// bundle is still in flight, honoring of every desc field, real render-target usability, the
// thread-safety of the shared physical free-list, and leak-free disposal.
//
// The disposal test needs a device it can tear down on its own, so it builds an isolated device
// rather than using the shared one.
public abstract class TransientTexturePoolTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    private const PixelFormat ColorFormat = PixelFormat.R32_G32_B32_A32_Float;

    private GraphicsDevice CreateIsolatedDevice() => GD.BackendType switch
    {
        GraphicsBackend.Vulkan => GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true)),
        _ => throw new NotSupportedException(),
    };

    [Fact]
    public void Rent_WithNullExecution_Throws()
    {
        RenderTextureDescription desc = new(64, 64, ColorFormat, depth: false);
        Assert.Throws<ArgumentNullException>(() => GD.RentTransientTexture(null, desc));
    }

    [Fact]
    public void Rent_AfterFrameCompletes_ReusesTheSameUnderlyingTexture()
    {
        RenderTextureDescription desc = new(128, 128, ColorFormat, depth: true);

        ExecutionTask f1 = GD.BeginExecution();
        Texture first = GD.RentTransientTexture(f1, desc);
        GD.CompleteExecution(f1);
        GD.WaitForExecution(f1);

        ExecutionTask f2 = GD.BeginExecution();
        Texture second = GD.RentTransientTexture(f2, desc);
        GD.CompleteExecution(f2);
        GD.WaitForIdle();

        // Once the renting frame's fence has signaled, the bundle returns to the desc-keyed
        // free-list and a later rent of an equal desc hands back the very same physical texture.
        Assert.Same(first, second);
    }

    [Fact]
    public void Rent_SameDescWhileInFlight_ReturnsADifferentTexture()
    {
        RenderTextureDescription desc = new(96, 96, ColorFormat, depth: false);

        ExecutionTask task = GD.BeginExecution();
        try
        {
            Texture first = GD.RentTransientTexture(task, desc);
            Texture second = GD.RentTransientTexture(task, desc);

            // The first bundle is still owned by the open frame, so it cannot be recycled; the
            // second rent must allocate a fresh bundle.
            Assert.NotSame(first, second);
        }
        finally
        {
            GD.CompleteExecution(task);
            GD.WaitForIdle();
        }
    }

    [Fact]
    public void Rent_DistinctDescriptions_ReturnDistinctTextures()
    {
        ExecutionTask task = GD.BeginExecution();
        try
        {
            Texture a = GD.RentTransientTexture(task, new RenderTextureDescription(64, 64, ColorFormat, depth: false));
            Texture b = GD.RentTransientTexture(task, new RenderTextureDescription(128, 64, ColorFormat, depth: false));
            Texture c = GD.RentTransientTexture(task, new RenderTextureDescription(64, 64, PixelFormat.R8_G8_B8_A8_UNorm, depth: false));
            Texture d = GD.RentTransientTexture(task, new RenderTextureDescription(64, 64, ColorFormat, depth: true));

            Assert.NotSame(a, b);
            Assert.NotSame(a, c);
            Assert.NotSame(a, d);
            Assert.NotSame(b, c);
            Assert.NotSame(c, d);
        }
        finally
        {
            GD.CompleteExecution(task);
            GD.WaitForIdle();
        }
    }

    [Fact]
    public void Rent_HonorsDimensionsAndFormat()
    {
        RenderTextureDescription desc = new(200, 120, PixelFormat.R8_G8_B8_A8_UNorm, depth: false);

        ExecutionTask task = GD.BeginExecution();
        try
        {
            Texture tex = GD.RentTransientTexture(task, desc);

            Assert.Equal(200u, tex.Width);
            Assert.Equal(120u, tex.Height);
            Assert.Equal(PixelFormat.R8_G8_B8_A8_UNorm, tex.Format);
            Assert.Equal(TextureSampleCount.Count1, tex.SampleCount);
            Assert.True((tex.Usage & TextureUsage.RenderTarget) != 0);
            Assert.True((tex.Usage & TextureUsage.Sampled) != 0);
        }
        finally
        {
            GD.CompleteExecution(task);
            GD.WaitForIdle();
        }
    }

    [Fact]
    public void RentFramebuffer_HonorsColorCountAndDepthPresence()
    {
        PixelFormat[] twoColors = [ColorFormat, PixelFormat.R8_G8_B8_A8_UNorm];

        ExecutionTask task = GD.BeginExecution();
        try
        {
            Framebuffer withDepth = GD.RentTransientFramebuffer(task, new RenderTextureDescription(64, 64, twoColors, depth: true));
            Assert.Equal(2, withDepth.ColorTargets.Count);
            Assert.NotNull(withDepth.DepthTarget);
            Assert.Equal(ColorFormat, withDepth.ColorTargets[0].Target.Format);
            Assert.Equal(PixelFormat.R8_G8_B8_A8_UNorm, withDepth.ColorTargets[1].Target.Format);

            Framebuffer noDepth = GD.RentTransientFramebuffer(task, new RenderTextureDescription(64, 64, ColorFormat, depth: false));
            Assert.Single(noDepth.ColorTargets);
            Assert.Null(noDepth.DepthTarget);
        }
        finally
        {
            GD.CompleteExecution(task);
            GD.WaitForIdle();
        }
    }

    [Fact]
    public void RentFramebuffer_DepthOnlyBundle_Succeeds()
    {
        ExecutionTask task = GD.BeginExecution();
        try
        {
            Framebuffer depthOnly = GD.RentTransientFramebuffer(task, new RenderTextureDescription(64, 64, Array.Empty<PixelFormat>(), depth: true));
            Assert.Empty(depthOnly.ColorTargets);
            Assert.NotNull(depthOnly.DepthTarget);

            // The single-texture entry point cannot serve a color-less bundle.
            Assert.Throws<RenderException>(() => GD.RentTransientTexture(task, new RenderTextureDescription(64, 64, Array.Empty<PixelFormat>(), depth: true)));
        }
        finally
        {
            GD.CompleteExecution(task);
            GD.WaitForIdle();
        }
    }

    [Fact]
    public void Rent_HonorsSampleCount()
    {
        TextureSampleCount limit = GD.GetSampleCountLimit(ColorFormat, depthFormat: false);
        if (limit == TextureSampleCount.Count1)
            return;

        RenderTextureDescription desc = new(64, 64, ColorFormat, depth: false, TextureSampleCount.Count2);

        ExecutionTask task = GD.BeginExecution();
        try
        {
            Texture tex = GD.RentTransientTexture(task, desc);
            Assert.Equal(TextureSampleCount.Count2, tex.SampleCount);
        }
        finally
        {
            GD.CompleteExecution(task);
            GD.WaitForIdle();
        }
    }

    [Fact]
    public void RentedFramebuffer_IsUsableAsARenderTarget()
    {
        const uint size = 64;
        RenderTextureDescription desc = new(size, size, ColorFormat, depth: false);

        Framebuffer fb = null;
        GD.RunTestGraph(context =>
        {
            fb = GD.RentTransientFramebuffer(context.Task, desc);

            CommandBuffer cl = context.GetCommandBuffer();
            cl.SetFramebuffer(fb);
            cl.ClearColorTarget(0, Color.Red);
            context.SubmitCommandBuffer(cl);
        });
        GD.WaitForIdle();

        Texture colorTarget = fb.ColorTargets[0].Target;
        Texture staging = RF.CreateTexture(
            TextureDescription.Texture2D(size, size, 1, 1, ColorFormat, TextureUsage.Staging));

        GD.RunTestGraph(context =>
        {
            CommandBuffer copy = context.GetCommandBuffer();
            copy.CopyTexture(colorTarget, staging);
            context.SubmitCommandBuffer(copy);
        });
        GD.WaitForIdle();

        MappedResourceView<Color> view = GD.Map<Color>(staging, MapMode.Read);
        for (int i = 0; i < view.Count; i++)
            Assert.Equal(Color.Red, view[i]);
        GD.Unmap(staging);
    }

    [Fact]
    public void Rent_ConcurrentCalls_NeverHandOutTheSameLiveTexture()
    {
        const int threadCount = 8;
        const int perThread = 32;
        RenderTextureDescription desc = new(64, 64, ColorFormat, depth: false);

        ExecutionTask task = GD.BeginExecution();
        try
        {
            Texture[][] results = new Texture[threadCount][];

            Parallel.For(0, threadCount, t =>
            {
                Texture[] local = new Texture[perThread];
                for (int i = 0; i < perThread; i++)
                    local[i] = GD.RentTransientTexture(task, desc);
                results[t] = local;
            });

            HashSet<Texture> seen = new(ReferenceEqualityComparer.Instance);
            int total = 0;
            foreach (Texture[] local in results)
            {
                foreach (Texture tex in local)
                {
                    total++;
                    // Nothing rented within the still-open frame can be recycled, so every rent
                    // across every thread must be a unique live texture.
                    Assert.True(seen.Add(tex), "the same live texture was handed to two callers");
                }
            }

            Assert.Equal(threadCount * perThread, total);
        }
        finally
        {
            GD.CompleteExecution(task);
            GD.WaitForIdle();
        }
    }

    [Fact]
    public void Dispose_ReleasesPooledTextures()
    {
        GraphicsDevice device = CreateIsolatedDevice();

        RenderTextureDescription desc = new(64, 64, ColorFormat, depth: true);

        ExecutionTask task = device.BeginExecution();
        Texture rented = device.RentTransientTexture(task, desc);
        Framebuffer framebuffer = device.RentTransientFramebuffer(task, desc);
        device.CompleteExecution(task);
        device.WaitForIdle();

        Assert.False(rented.IsDisposed);
        Assert.False(framebuffer.IsDisposed);

        device.Dispose();

        // Disposing the device disposes the whole pool: every backing texture and framebuffer,
        // whether it was in-flight or free, is released.
        Assert.True(rented.IsDisposed);
        Assert.True(framebuffer.IsDisposed);
    }
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanTransientTexturePoolTests : TransientTexturePoolTests<VulkanDeviceCreator> { }
#endif
