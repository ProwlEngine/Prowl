using Xunit;

namespace Prowl.Graphite.Tests;

// Core-path coverage of the graph-execution/synchronization API that has no upstream equivalent:
// the BeginExecution/CompleteExecution lifecycle, transient ring allocation, fences,
// and disposal.
public abstract class FrameCoreTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    [Fact]
    public void Execution_CompletesAndSignalsFence()
    {
        CommandBuffer cl = RF.CreateCommandBuffer();
        cl.Begin();
        cl.End();
        ExecutionTask task = GD.RunTestGraph(context => context.SubmitCommandBuffer(cl));

        GD.WaitForExecution(task);

        Assert.True(GD.IsExecutionComplete(task));
        Assert.True(task.CompletionFence.Signaled);
    }

    [Fact]
    public void AllocateTransient_WithinExecution_ReturnsUsableRange()
    {
        DeviceBufferRange range = default;
        ExecutionTask task = GD.RunTestGraph(context => range = context.AllocateTransient(256));

        Assert.NotNull(range.Buffer);
        Assert.True(range.SizeInBytes >= 256);

        GD.WaitForExecution(task);
    }

    [Fact]
    public void Fence_ResetClearsSignaledState()
    {
        Fence fence = RF.CreateFence(signaled: true);
        Assert.True(fence.Signaled);

        GD.ResetFence(fence);
        Assert.False(fence.Signaled);
    }

    [Fact]
    public void Dispose_MarksResourceDisposed()
    {
        // Created on the inner factory so the test base does not dispose it a second time.
        DeviceBuffer buffer = GD.ResourceFactory.CreateBuffer(new BufferDescription(64, BufferUsage.VertexBuffer));
        Assert.False(buffer.IsDisposed);

        buffer.Dispose();
        Assert.True(buffer.IsDisposed);
    }
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanFrameCoreTests : FrameCoreTests<VulkanDeviceCreator> { }
#endif
