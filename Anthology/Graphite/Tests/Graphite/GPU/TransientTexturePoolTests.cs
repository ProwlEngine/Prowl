using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Tests;

// The device-level transient render-texture pool (GraphicsDevice.RentGraphTransientRenderTexture).
// Covers the recycle model that mirrors the old RenderTexture.GetTemporaryRT: desc-keyed reuse once a frame's fence signals, no reuse while a
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

    private static Texture RentColor(GraphicsDevice device, ExecutionTask task, in RenderTextureDescription desc)
        => device.RentGraphTransientRenderTexture(task, desc).ColorTextures[0];

    private static Framebuffer RentFramebuffer(GraphicsDevice device, ExecutionTask task, in RenderTextureDescription desc)
        => device.RentGraphTransientRenderTexture(task, desc).Framebuffer;

    [Fact]
    public void Rent_WithNullExecution_Throws()
    {
        RenderTextureDescription desc = new(64, 64, ColorFormat, depth: false);
        Assert.Throws<ArgumentNullException>(() => GD.RentGraphTransientRenderTexture(null, desc));
    }

    [Fact]
    public void Rent_AfterFrameCompletes_ReusesTheSameUnderlyingTexture()
    {
        RenderTextureDescription desc = new(128, 128, ColorFormat, depth: true);

        ExecutionTask f1 = GD.BeginExecution();
        Texture first = RentColor(GD, f1, desc);
        GD.CompleteExecution(f1);
        GD.WaitForExecution(f1);

        ExecutionTask f2 = GD.BeginExecution();
        Texture second = RentColor(GD, f2, desc);
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
            Texture first = RentColor(GD, task, desc);
            Texture second = RentColor(GD, task, desc);

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
            Texture a = RentColor(GD, task, new RenderTextureDescription(64, 64, ColorFormat, depth: false));
            Texture b = RentColor(GD, task, new RenderTextureDescription(128, 64, ColorFormat, depth: false));
            Texture c = RentColor(GD, task, new RenderTextureDescription(64, 64, PixelFormat.R8_G8_B8_A8_UNorm, depth: false));
            Texture d = RentColor(GD, task, new RenderTextureDescription(64, 64, ColorFormat, depth: true));

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
            Texture tex = RentColor(GD, task, desc);

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
            Framebuffer withDepth = RentFramebuffer(GD, task, new RenderTextureDescription(64, 64, twoColors, depth: true));
            Assert.Equal(2, withDepth.ColorTargets.Count);
            Assert.NotNull(withDepth.DepthTarget);
            Assert.Equal(ColorFormat, withDepth.ColorTargets[0].Target.Format);
            Assert.Equal(PixelFormat.R8_G8_B8_A8_UNorm, withDepth.ColorTargets[1].Target.Format);

            Framebuffer noDepth = RentFramebuffer(GD, task, new RenderTextureDescription(64, 64, ColorFormat, depth: false));
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
    public void RenderTexture_NameGetter_ReturnsNameAndPropagatesToAttachments()
    {
        RenderTexture target = RF.CreateRenderTexture(new RenderTextureDescription(8, 8, ColorFormat, depth: true));
        try
        {
            target.Name = "Scene";
            Assert.Equal("Scene", target.Name);
            Assert.Equal("Scene Color[0]", target.ColorTextures[0].Name);
            Assert.Equal("Scene Depth", target.DepthTexture!.Name);
        }
        finally
        {
            target.Dispose();
        }
    }

    [Fact]
    public void RenderTexture_Dispose_SetsIsDisposedAndFreesAttachments()
    {
        RenderTexture target = RF.CreateRenderTexture(new RenderTextureDescription(8, 8, ColorFormat, depth: true));
        Assert.False(target.IsDisposed);

        target.Dispose();
        target.Dispose();

        Assert.True(target.IsDisposed);
        Assert.True(target.ColorTextures[0].IsDisposed);
        Assert.True(target.DepthTexture!.IsDisposed);
        Assert.True(target.Framebuffer.IsDisposed);
    }

    [Fact]
    public void RenderTexture_ExplicitDepthFormat_IsUsedAndReportedInDesc()
    {
        PixelFormat[] colors = [ColorFormat];
        RenderTexture target = RF.CreateRenderTexture(new RenderTextureDescription(8, 8, colors, PixelFormat.D32_Float_S8_UInt));
        try
        {
            Assert.Equal(PixelFormat.D32_Float_S8_UInt, target.DepthTexture!.Format);
            Assert.Equal(PixelFormat.D32_Float_S8_UInt, target.Desc.DepthFormat);
        }
        finally
        {
            target.Dispose();
        }
    }

    [Fact]
    public void RenderTextureDescription_DepthFormat_DistinguishesDescs()
    {
        PixelFormat[] colors = [ColorFormat];
        RenderTextureDescription deviceDefault = new(8, 8, colors, true);
        RenderTextureDescription explicitFormat = new(8, 8, colors, PixelFormat.D32_Float_S8_UInt);

        Assert.NotEqual(deviceDefault, explicitFormat);
    }

    [Fact]
    public void RenderTexture_WrappingFramebuffer_DoesNotFreeAttachmentsOnDispose()
    {
        RenderTexture owner = RF.CreateRenderTexture(new RenderTextureDescription(8, 8, ColorFormat, depth: true));
        try
        {
            RenderTexture wrapper = new(owner.Framebuffer);
            Assert.Equal(PixelFormat.R32_G32_B32_A32_Float, wrapper.Desc.ColorFormats[0]);
            Assert.Equal(owner.DepthTexture!.Format, wrapper.Desc.DepthFormat);

            wrapper.Dispose();

            Assert.True(wrapper.IsDisposed);
            Assert.False(owner.ColorTextures[0].IsDisposed);
            Assert.False(owner.Framebuffer.IsDisposed);
        }
        finally
        {
            owner.Dispose();
        }
    }

    [Fact]
    public void RentFramebuffer_DepthOnlyBundle_Succeeds()
    {
        ExecutionTask task = GD.BeginExecution();
        try
        {
            Framebuffer depthOnly = RentFramebuffer(GD, task, new RenderTextureDescription(64, 64, Array.Empty<PixelFormat>(), depth: true));
            Assert.Empty(depthOnly.ColorTargets);
            Assert.NotNull(depthOnly.DepthTarget);
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
            Texture tex = RentColor(GD, task, desc);
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
            fb = RentFramebuffer(GD, context.Task, desc);

            CommandBuffer cl = context.GetCommandBuffer();
            cl.SetFramebuffer(fb);
            cl.ClearColorTarget(0, Color.Red);
            context.SubmitCommandBuffer(cl);
        });
        GD.WaitForIdle();

        Texture colorTarget = fb.ColorTargets[0].Target;
        TexelData<Color> view = ReadTexture<Color>(colorTarget);
        for (int i = 0; i < view.Length; i++)
            Assert.Equal(Color.Red, view[i]);
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
                    local[i] = RentColor(GD, task, desc);
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
        Texture rented = RentColor(device, task, desc);
        Framebuffer framebuffer = RentFramebuffer(device, task, desc);
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
