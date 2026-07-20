using System.Collections.Generic;

using Xunit;

namespace Prowl.Graphite.Tests;

// The graph-execution ring mechanics. GraphicsDeviceTests covers the happy path (monotonic ids,
// throttling beyond ring depth); this suite covers the ring slot actually cycling and repeating,
// the completion fence recycled per slot, in-flight tracking, and the MaxExecutingTasks backstop
// enforcing the ceiling.
public abstract class FrameLifecycleTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    [Fact]
    public void WaitForExecution_OnUnstartedExecution_ReportsWithoutThrowing()
    {
        // A task that was never dispatched by this device is not tracked; a bounded wait must
        // simply report completion rather than block or throw.
        ExecutionTask task = GD.BeginExecution();
        GD.CompleteExecution(task);
        Assert.True(GD.WaitForExecution(task, ulong.MaxValue));
    }

    [Fact]
    public void RingSlot_CyclesAndRepeatsEveryMaxExecutingTasks()
    {
        List<uint> slots = [];
        for (uint i = 0; i < GD.MaxExecutingTasks * 2; i++)
        {
            ExecutionTask task = GD.BeginExecution();
            slots.Add(task.RingSlot);
            GD.CompleteExecution(task);
            GD.WaitForExecution(task);
        }

        // The first cycle visits every slot exactly once, and the second repeats it in order.
        HashSet<uint> firstCycle = new(slots.GetRange(0, (int)GD.MaxExecutingTasks));
        Assert.Equal(GD.MaxExecutingTasks, (uint)firstCycle.Count);

        for (int i = 0; i < GD.MaxExecutingTasks; i++)
            Assert.Equal(slots[i], slots[i + (int)GD.MaxExecutingTasks]);
    }

    [Fact]
    public void RingSlot_DerivesFromExecutionId()
    {
        for (uint i = 0; i < GD.MaxExecutingTasks * 2; i++)
        {
            ExecutionTask task = GD.BeginExecution();
            Assert.Equal((task.Id - 1) % GD.MaxExecutingTasks, task.RingSlot);
            GD.CompleteExecution(task);
            GD.WaitForExecution(task);
        }
    }

    [Fact]
    public void ExecutingTasks_TracksOpenAndCompletedExecutions()
    {
        Assert.Equal(0u, GD.ExecutingTasks);

        ExecutionTask task = GD.BeginExecution();
        Assert.Equal(1u, GD.ExecutingTasks);

        GD.CompleteExecution(task);
        GD.WaitForExecution(task);
        Assert.Equal(0u, GD.ExecutingTasks);
    }

    [Fact]
    public void IsExecutionComplete_OnOpenExecution_IsFalse()
    {
        ExecutionTask task = GD.BeginExecution();
        try
        {
            // An open execution has not been submitted, so it cannot have completed.
            Assert.False(GD.IsExecutionComplete(task));
        }
        finally
        {
            GD.CompleteExecution(task);
            GD.WaitForIdle();
        }
    }

    [Fact]
    public void LastCompletedExecutionId_AdvancesToTheLastAfterWaitForIdle()
    {
        ExecutionTask task = GD.BeginExecution();
        ulong id = task.Id;
        GD.CompleteExecution(task);
        GD.WaitForIdle();

        Assert.Equal(id, GD.LastCompletedExecutionId);
    }

    [Fact]
    public void WaitForExecution_WithZeroTimeout_ReportsRatherThanThrows()
    {
        ExecutionTask task = GD.BeginExecution();
        GD.CompleteExecution(task);

        // The timeout overload reports completion as a bool. Either answer is legitimate here
        // (an empty execution may well already be done); what matters is that it returns instead of
        // throwing, and that a subsequent infinite wait always succeeds.
        GD.WaitForExecution(task, 0);

        Assert.True(GD.WaitForExecution(task, ulong.MaxValue));
        Assert.True(GD.IsExecutionComplete(task));
    }

    [Fact]
    public void CompletionFence_IsRecycledPerRingSlot()
    {
        Dictionary<uint, Fence> fencesBySlot = [];
        for (uint i = 0; i < GD.MaxExecutingTasks * 2; i++)
        {
            ExecutionTask task = GD.BeginExecution();
            if (fencesBySlot.TryGetValue(task.RingSlot, out Fence previous))
                Assert.Same(previous, task.CompletionFence);
            else
                fencesBySlot[task.RingSlot] = task.CompletionFence;

            GD.CompleteExecution(task);
            GD.WaitForExecution(task);
        }

        Assert.Equal(GD.MaxExecutingTasks, (uint)fencesBySlot.Count);
    }

    [Fact]
    public void BeginExecution_NeverExceedsMaxExecutingTasks()
    {
        uint max = GD.MaxExecutingTasks;
        for (uint i = 0; i < max * 3 + 2; i++)
        {
            ExecutionTask task = GD.BeginExecution();

            // The device-side backstop guarantees the ceiling: a Begin past the ring depth blocks on
            // the oldest execution's fence before proceeding, so in-flight count never exceeds max.
            Assert.True(GD.ExecutingTasks <= max, "in-flight executions exceeded MaxExecutingTasks");

            GD.CompleteExecution(task);
        }

        GD.WaitForIdle();
        Assert.Equal(0u, GD.ExecutingTasks);
    }
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanFrameLifecycleTests : FrameLifecycleTests<VulkanDeviceCreator> { }
#endif
