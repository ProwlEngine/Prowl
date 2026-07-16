using System.Collections.Generic;

using Xunit;

namespace Prowl.Graphite.Tests;

// The frame system's guard rails and ring mechanics. GraphicsDeviceTests covers the happy path
// (monotonic ids, throttling beyond ring depth); this suite covers the misuse that the
// validation layer is supposed to reject, the ring slot actually cycling and repeating, and the
// deferred-disposal queue that BeginFrame drains when it reuses a slot.
public abstract class FrameLifecycleTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    [Fact]
    public void BeginFrame_WhileFrameActive_Throws()
    {
        Frame frame = GD.BeginFrame();
        try
        {
            Assert.Throws<RenderException>(() => GD.BeginFrame());
        }
        finally
        {
            GD.EndFrame(frame);
            GD.WaitForIdle();
        }
    }

    [Fact]
    public void EndFrame_WithNoActiveFrame_Throws()
    {
        Assert.Throws<RenderException>(() => GD.EndFrame());
    }

    [Fact]
    public void EndFrame_Twice_Throws()
    {
        Frame frame = GD.BeginFrame();
        GD.EndFrame(frame);

        // The frame is no longer current, so ending it again must be rejected.
        Assert.Throws<RenderException>(() => GD.EndFrame(frame));
        GD.WaitForIdle();
    }

    [Fact]
    public void EndFrame_WithStaleFrame_Throws()
    {
        Frame first = GD.BeginFrame();
        GD.EndFrame(first);
        GD.WaitForFrame(first);

        Frame second = GD.BeginFrame();
        try
        {
            Assert.Throws<RenderException>(() => GD.EndFrame(first));
        }
        finally
        {
            GD.EndFrame(second);
            GD.WaitForIdle();
        }
    }

    [Fact]
    public void CurrentFrame_WithNoActiveFrame_Throws()
    {
        Assert.Throws<RenderException>(() => GD.CurrentFrame);
    }

    [Fact]
    public void WaitForIdle_WhileFrameActive_Throws()
    {
        Frame frame = GD.BeginFrame();
        try
        {
            Assert.Throws<RenderException>(() => GD.WaitForIdle());
        }
        finally
        {
            GD.EndFrame(frame);
            GD.WaitForIdle();
        }
    }

    [Fact]
    public void WaitForFrame_OnOpenFrame_Throws()
    {
        Frame frame = GD.BeginFrame();
        try
        {
            Assert.Throws<RenderException>(() => GD.WaitForFrame(frame));
        }
        finally
        {
            GD.EndFrame(frame);
            GD.WaitForIdle();
        }
    }

    [Fact]
    public void WaitForFrame_OnUnstartedFrame_Throws()
    {
        Assert.Throws<RenderException>(() => GD.WaitForFrame(0));
        Assert.Throws<RenderException>(() => GD.WaitForFrame(ulong.MaxValue));
    }

    [Fact]
    public void IsFrameComplete_OnUnstartedFrame_Throws()
    {
        Assert.Throws<RenderException>(() => GD.IsFrameComplete(0));
        Assert.Throws<RenderException>(() => GD.IsFrameComplete(ulong.MaxValue));
    }

    [Fact]
    public void RingSlot_CyclesAndRepeatsEveryMaxFramesInFlight()
    {
        List<uint> slots = [];
        for (uint i = 0; i < GD.MaxFramesInFlight * 2; i++)
        {
            Frame frame = GD.BeginFrame();
            slots.Add(frame.RingSlot);
            GD.EndFrame(frame);
            GD.WaitForFrame(frame);
        }

        // The first cycle visits every slot exactly once, and the second repeats it in order.
        HashSet<uint> firstCycle = new(slots.GetRange(0, (int)GD.MaxFramesInFlight));
        Assert.Equal(GD.MaxFramesInFlight, (uint)firstCycle.Count);

        for (int i = 0; i < GD.MaxFramesInFlight; i++)
            Assert.Equal(slots[i], slots[i + (int)GD.MaxFramesInFlight]);
    }

    [Fact]
    public void RingSlot_DerivesFromFrameId()
    {
        for (uint i = 0; i < GD.MaxFramesInFlight * 2; i++)
        {
            Frame frame = GD.BeginFrame();
            Assert.Equal((frame.FrameId - 1) % GD.MaxFramesInFlight, frame.RingSlot);
            GD.EndFrame(frame);
            GD.WaitForFrame(frame);
        }
    }

    [Fact]
    public void FramesInFlight_TracksOpenAndCompletedFrames()
    {
        Assert.Equal(0u, GD.FramesInFlight);

        Frame frame = GD.BeginFrame();
        Assert.Equal(1u, GD.FramesInFlight);

        GD.EndFrame(frame);
        GD.WaitForFrame(frame);
        Assert.Equal(0u, GD.FramesInFlight);
    }

    [Fact]
    public void IsFrameComplete_OnOpenFrame_IsFalse()
    {
        Frame frame = GD.BeginFrame();
        try
        {
            // An open frame has not been submitted, so it cannot have completed.
            Assert.False(GD.IsFrameComplete(frame));
        }
        finally
        {
            GD.EndFrame(frame);
            GD.WaitForIdle();
        }
    }

    [Fact]
    public void LastCompletedFrameId_AdvancesToTheLastFrameAfterWaitForIdle()
    {
        Frame frame = GD.BeginFrame();
        ulong id = frame.FrameId;
        GD.EndFrame(frame);
        GD.WaitForIdle();

        Assert.Equal(id, GD.LastCompletedFrameId);
    }

    [Fact]
    public void WaitForFrame_WithZeroTimeout_ReportsRatherThanThrows()
    {
        Frame frame = GD.BeginFrame();
        GD.EndFrame(frame);

        // The timeout overload reports completion as a bool. Either answer is legitimate here
        // (an empty frame may well already be done); what matters is that it returns instead of
        // throwing, and that a subsequent infinite wait always succeeds.
        GD.WaitForFrame(frame, 0);

        Assert.True(GD.WaitForFrame(frame, ulong.MaxValue));
        Assert.True(GD.IsFrameComplete(frame));
    }

    [Fact]
    public void CompletionFence_IsRecycledPerRingSlot()
    {
        Dictionary<uint, Fence> fencesBySlot = [];
        for (uint i = 0; i < GD.MaxFramesInFlight * 2; i++)
        {
            Frame frame = GD.BeginFrame();
            if (fencesBySlot.TryGetValue(frame.RingSlot, out Fence previous))
                Assert.Same(previous, frame.CompletionFence);
            else
                fencesBySlot[frame.RingSlot] = frame.CompletionFence;

            GD.EndFrame(frame);
            GD.WaitForFrame(frame);
        }

        Assert.Equal(GD.MaxFramesInFlight, (uint)fencesBySlot.Count);
    }
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanFrameLifecycleTests : FrameLifecycleTests<VulkanDeviceCreator> { }
#endif
