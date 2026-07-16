using System;
using System.Collections.Generic;

using Xunit;

namespace Prowl.Graphite.Tests;

// The per-frame transient bump allocator (Frame.AllocateTransient). GraphicsDeviceTests covers
// the trivial path and the single-allocation-over-hard-cap throw; this suite covers what the
// allocator actually has to get right: offset alignment, genuine non-overlap, the head resetting
// per frame, and the overflow spill path (growth rule, cumulative caps, the one-shot soft-cap
// warning) which had no coverage at all.
//
// The overflow tests need a device with a small primary transient buffer, so they build their
// own isolated device rather than using the shared one.
public abstract class TransientAllocationTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    private const uint PrimarySize = 4096;

    private GraphicsDevice CreateIsolatedDevice(GraphicsDeviceOptions options) => GD.BackendType switch
    {
        GraphicsBackend.Vulkan => GraphicsDevice.CreateVulkan(options),
        _ => throw new NotSupportedException(),
    };

    // A device whose primary transient buffer is PrimarySize, so overflow is reachable in a test.
    private GraphicsDevice CreateSmallTransientDevice(uint softCap, uint hardCap)
        => CreateIsolatedDevice(new GraphicsDeviceOptions(true)
        {
            EnableProfiling = true,
            TransientBufferInitialSize = PrimarySize,
            TransientBufferSoftCapBytes = softCap,
            TransientBufferHardCapBytes = hardCap,
        });

    [Fact]
    public void AllocateTransient_OffsetsAreAlignedForUniformBinding()
    {
        uint alignment = GD.UniformBufferMinOffsetAlignment;

        Frame frame = GD.BeginFrame();
        try
        {
            // Deliberately unaligned sizes: the allocator must round the head up each time,
            // otherwise the returned offset is illegal as a dynamic UBO offset.
            foreach (uint size in new uint[] { 1, 7, 33, 100, 255, 3 })
            {
                DeviceBufferRange range = GD.AllocateTransient(size);
                Assert.Equal(0u, range.Offset % alignment);
            }
        }
        finally
        {
            GD.EndFrame(frame);
            GD.WaitForIdle();
        }
    }

    [Fact]
    public void AllocateTransient_RunOfAllocations_NeverOverlap()
    {
        List<DeviceBufferRange> ranges = [];

        Frame frame = GD.BeginFrame();
        try
        {
            for (uint i = 0; i < 64; i++)
                ranges.Add(GD.AllocateTransient(64 + i));
        }
        finally
        {
            GD.EndFrame(frame);
            GD.WaitForIdle();
        }

        // Every pair sharing a backing buffer must occupy disjoint byte ranges. The existing
        // suite only compares two allocations for an identical offset, which an overlapping
        // allocator would still pass.
        for (int i = 0; i < ranges.Count; i++)
        {
            for (int j = i + 1; j < ranges.Count; j++)
            {
                if (ranges[i].Buffer != ranges[j].Buffer) continue;

                bool disjoint = ranges[i].Offset + ranges[i].SizeInBytes <= ranges[j].Offset
                    || ranges[j].Offset + ranges[j].SizeInBytes <= ranges[i].Offset;
                Assert.True(disjoint, $"ranges {i} and {j} overlap in the same buffer");
            }
        }
    }

    [Fact]
    public void AllocateTransient_HeadResetsEveryFrame()
    {
        Dictionary<uint, DeviceBuffer> primaryBySlot = [];

        for (uint i = 0; i < GD.MaxFramesInFlight * 2; i++)
        {
            Frame frame = GD.BeginFrame();
            DeviceBufferRange first = GD.AllocateTransient(256);

            // The bump head restarts at 0 for every frame, so the frame's first allocation is
            // always at offset 0 regardless of how much the previous frame consumed.
            Assert.Equal(0u, first.Offset);

            // A ring slot keeps its primary buffer across cycles; it is reused, not reallocated.
            if (primaryBySlot.TryGetValue(frame.RingSlot, out DeviceBuffer previous))
                Assert.Same(previous, first.Buffer);
            else
                primaryBySlot[frame.RingSlot] = first.Buffer;

            GD.AllocateTransient(1024);
            GD.EndFrame(frame);
            GD.WaitForFrame(frame);
        }
    }

    [Fact]
    public void AllocateTransient_MemoryIsWritableAndDistinct()
    {
        // Proves the ranges are backed by real, non-aliasing memory: two allocations written
        // through the mapped pointer must read back independently.
        using DeviceBuffer staging = GD.ResourceFactory.CreateBuffer(
            new BufferDescription(sizeof(uint) * 2, BufferUsage.Staging));

        Frame frame = GD.BeginFrame();
        DeviceBufferRange a = GD.AllocateTransient(sizeof(uint));
        DeviceBufferRange b = GD.AllocateTransient(sizeof(uint));

        WriteUInt(a, 0xAAAAAAAA);
        WriteUInt(b, 0xBBBBBBBB);

        CommandBuffer cl = RF.CreateCommandBuffer();
        cl.Begin();
        cl.CopyBuffer(a.Buffer, a.Offset, staging, 0, sizeof(uint));
        cl.CopyBuffer(b.Buffer, b.Offset, staging, sizeof(uint), sizeof(uint));
        cl.End();
        frame.SubmitCommands(cl);
        GD.EndFrame(frame);
        GD.WaitForIdle();

        MappedResourceView<uint> map = GD.Map<uint>(staging, MapMode.Read);
        uint first = map[0];
        uint second = map[1];
        GD.Unmap(staging);

        Assert.Equal(0xAAAAAAAA, first);
        Assert.Equal(0xBBBBBBBB, second);
    }

    private void WriteUInt(DeviceBufferRange range, uint value)
    {
        MappedResource mapped = GD.Map(range.Buffer, MapMode.Write);
        unsafe { *(uint*)((byte*)mapped.Data + range.Offset) = value; }
        GD.Unmap(range.Buffer);
    }

    [Fact]
    public void AllocateTransient_PastPrimaryCapacity_SpillsToAnOverflowBuffer()
    {
        using GraphicsDevice device = CreateSmallTransientDevice(64 * 1024, 1024 * 1024);

        Frame frame = device.BeginFrame();
        try
        {
            DeviceBufferRange primary = device.AllocateTransient(PrimarySize);
            Assert.Equal(0u, primary.Offset);

            // The primary is now exactly full, so this must land in a fresh overflow buffer.
            DeviceBufferRange spilled = device.AllocateTransient(256);

            Assert.NotSame(primary.Buffer, spilled.Buffer);
            Assert.Equal(0u, spilled.Offset);
            Assert.True(spilled.Buffer.SizeInBytes >= 256);
        }
        finally
        {
            device.EndFrame(frame);
            device.WaitForIdle();
        }
    }

    [Fact]
    public void AllocateTransient_OverflowBuffer_GrowsToAtLeastDoubleThePrimary()
    {
        using GraphicsDevice device = CreateSmallTransientDevice(64 * 1024, 1024 * 1024);

        Frame frame = device.BeginFrame();
        try
        {
            device.AllocateTransient(PrimarySize);
            DeviceBufferRange spilled = device.AllocateTransient(16);

            // Growth rule is max(requested, primary * 2): a small spill still doubles, so the
            // next few allocations do not each spawn another buffer.
            Assert.True(spilled.Buffer.SizeInBytes >= PrimarySize * 2);
        }
        finally
        {
            device.EndFrame(frame);
            device.WaitForIdle();
        }
    }

    [Fact]
    public void AllocateTransient_LargerThanPrimary_SucceedsUnderHardCap()
    {
        using GraphicsDevice device = CreateSmallTransientDevice(64 * 1024, 1024 * 1024);

        Frame frame = device.BeginFrame();
        try
        {
            // A single request no primary could ever satisfy is served by a right-sized overflow.
            DeviceBufferRange range = device.AllocateTransient(PrimarySize * 4);

            Assert.Equal(0u, range.Offset);
            Assert.True(range.Buffer.SizeInBytes >= PrimarySize * 4);
        }
        finally
        {
            device.EndFrame(frame);
            device.WaitForIdle();
        }
    }

    [Fact]
    public void AllocateTransient_SubsequentSpillsReuseTheOverflowBuffer()
    {
        using GraphicsDevice device = CreateSmallTransientDevice(64 * 1024, 1024 * 1024);

        Frame frame = device.BeginFrame();
        try
        {
            device.AllocateTransient(PrimarySize);
            DeviceBufferRange first = device.AllocateTransient(64);
            DeviceBufferRange second = device.AllocateTransient(64);

            // Once spilled, the overflow buffer becomes the active one and keeps bump-allocating
            // rather than creating a buffer per allocation.
            Assert.Same(first.Buffer, second.Buffer);
            Assert.True(second.Offset >= first.Offset + first.SizeInBytes);
        }
        finally
        {
            device.EndFrame(frame);
            device.WaitForIdle();
        }
    }

    [Fact]
    public void AllocateTransient_CumulativeAcrossOverflows_TripsHardCap()
    {
        // Hard cap sits above any single allocation here, so it can only be reached by the
        // running total across several overflow buffers. Soft and hard are equal because the
        // device raises the hard cap to meet the soft cap when the soft cap is the larger one.
        using GraphicsDevice device = CreateSmallTransientDevice(16 * 1024, 16 * 1024);

        Frame frame = device.BeginFrame();
        try
        {
            device.AllocateTransient(PrimarySize);
            device.AllocateTransient(PrimarySize * 2);

            Assert.Throws<RenderException>(() => device.AllocateTransient(PrimarySize * 2));
        }
        finally
        {
            device.EndFrame(frame);
            device.WaitForIdle();
        }
    }

    [Fact]
    public void AllocateTransient_SoftCap_WarnsExactlyOncePerDevice()
    {
        using GraphicsDevice device = CreateSmallTransientDevice(PrimarySize, 1024 * 1024);

        List<string> warnings = [];
        device.OnWarning = message => warnings.Add(message);

        Frame frame = device.BeginFrame();
        try
        {
            device.AllocateTransient(PrimarySize);
            device.AllocateTransient(PrimarySize * 2);
            device.AllocateTransient(PrimarySize * 2);
            device.AllocateTransient(PrimarySize * 2);
        }
        finally
        {
            device.EndFrame(frame);
            device.WaitForIdle();
        }

        // The soft cap is advisory: it must warn, but latch so it cannot spam a frame loop.
        Assert.Single(warnings);
        Assert.Contains("soft cap", warnings[0]);
    }

    [Fact]
    public void AllocateTransient_SoftCapWarning_DoesNotRepeatOnLaterFrames()
    {
        using GraphicsDevice device = CreateSmallTransientDevice(PrimarySize, 1024 * 1024);

        List<string> warnings = [];
        device.OnWarning = message => warnings.Add(message);

        for (int i = 0; i < 3; i++)
        {
            Frame frame = device.BeginFrame();
            device.AllocateTransient(PrimarySize);
            device.AllocateTransient(PrimarySize * 2);
            device.EndFrame(frame);
            device.WaitForIdle();
        }

        Assert.Single(warnings);
    }
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanTransientAllocationTests : TransientAllocationTests<VulkanDeviceCreator> { }
#endif
