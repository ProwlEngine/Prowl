using Prowl.Graphite.Vk;

using Xunit;

namespace Prowl.Graphite.Tests;

public abstract class FrameLifecycleTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
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

    [Fact]
    public void NativeCommandBuffers_AreReusedAcrossExecutions()
    {
        VkGraphicsDevice vk = (VkGraphicsDevice)GD;
        DeviceBuffer buffer = RF.CreateBuffer(new BufferDescription(16, BufferUsage.VertexBuffer));

        void Cycle()
        {
            GD.RunTestGraph((context, cl) => { });
            RF.CreateTexture(TextureDescription.Texture2D(4, 4, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.RenderTarget)).Dispose();
            GD.Record(transfer => transfer.UpdateBuffer(buffer, 0, new uint[] { 1, 2, 3, 4 })).Wait();
            GD.WaitForIdle();
        }

        for (int i = 0; i < GD.MaxExecutingTasks; i++)
            Cycle();
        int warm = vk.AllocatedCommandBufferCount;

        for (int i = 0; i < 32; i++)
            Cycle();

        Assert.Equal(warm, vk.AllocatedCommandBufferCount);

        // Each graph rents one command buffer, reclaimed and handed out again once its ring slot is reused
        int graphs = (int)GD.MaxExecutingTasks * 8;
        for (int i = 0; i < graphs; i++)
        {
            GD.RunTestGraph((context, cl) =>
            {
            });
            GD.WaitForIdle();
        }

        Assert.True(vk.PooledGraphCommandBufferCount <= GD.MaxExecutingTasks + 1,
            $"Expected graph command buffers to be recycled to ~{GD.MaxExecutingTasks}, but {vk.PooledGraphCommandBufferCount} were allocated over {graphs} graphs.");
    }
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanFrameLifecycleTests : FrameLifecycleTests<VulkanDeviceCreator> { }
#endif
