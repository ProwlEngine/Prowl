using Xunit;

namespace Prowl.Graphite.Tests;

public abstract class FrameLifecycleTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    [Fact]
    public void RingSlot_DerivesFromMonotonicExecutionId()
    {
        ulong previousId = 0;
        for (uint i = 0; i < GD.MaxExecutingTasks * 2; i++)
        {
            ExecutionTask task = GD.BeginExecution();
            Assert.True(task.Id > previousId);
            Assert.Equal((task.Id - 1) % GD.MaxExecutingTasks, task.RingSlot);
            previousId = task.Id;
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
    public void LastCompletedExecutionId_AdvancesToTheLastAfterWaitForIdle()
    {
        ExecutionTask task = GD.BeginExecution();
        ulong id = task.Id;
        GD.CompleteExecution(task);
        GD.WaitForIdle();

        Assert.Equal(id, GD.LastCompletedExecutionId);
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
