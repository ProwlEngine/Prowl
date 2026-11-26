using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Tests;

public abstract class TransientTexturePoolTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    private const PixelFormat ColorFormat = PixelFormat.R32_G32_B32_A32_Float;

    private static Texture RentColor(GraphicsDevice device, ExecutionTask task, in RenderTextureDescription desc)
        => device.RentGraphTransientRenderTexture(task, desc).ColorTextures[0];

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

        RenderTextureDescription reuseDesc = new(128, 128, ColorFormat, depth: true);

        ExecutionTask f1 = GD.BeginExecution();
        Texture reusedFirst = RentColor(GD, f1, reuseDesc);
        GD.CompleteExecution(f1);
        GD.WaitForExecution(f1);

        ExecutionTask f2 = GD.BeginExecution();
        Texture reusedSecond = RentColor(GD, f2, reuseDesc);
        GD.CompleteExecution(f2);
        GD.WaitForIdle();

        // Once the renting frame's fence has signaled, the bundle returns to the reuseDesc-keyed
        // free-list and a later rent of an equal reuseDesc hands back the very same physical texture.
        Assert.Same(reusedFirst, reusedSecond);
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
    public void TransientTexturePool_EvictsEntriesUnusedForRetentionWindow()
    {
        ExecutionTask first = GD.BeginExecution();
        RenderTexture old = GD.RentGraphTransientRenderTexture(first, new RenderTextureDescription(8, 8, PixelFormat.R8_G8_B8_A8_UNorm, false));
        GD.CompleteExecution(first);
        GD.WaitForIdle();

        for (ulong i = 0; i <= ExecutionPool<RenderTextureDescription, RenderTexture>.RetentionExecutions; i++)
            GD.CompleteExecution(GD.BeginExecution());
        GD.WaitForIdle();

        ExecutionTask last = GD.BeginExecution();
        GD.RentGraphTransientRenderTexture(last, new RenderTextureDescription(16, 16, PixelFormat.R8_G8_B8_A8_UNorm, false));
        GD.CompleteExecution(last);
        GD.WaitForIdle();

        Assert.True(old.ColorTextures[0].IsDisposed);
    }
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanTransientTexturePoolTests : TransientTexturePoolTests<VulkanDeviceCreator> { }
#endif
