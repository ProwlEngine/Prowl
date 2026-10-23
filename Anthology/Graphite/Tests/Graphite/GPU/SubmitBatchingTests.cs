using System;
using Prowl.Graphite.Vk;

using Xunit;

namespace Prowl.Graphite.Tests;

file static class BufferWrites
{
    public static void Add(FrameGraph graph, string name, DeviceBuffer source, DeviceBuffer destination)
        => graph.AddPass(name, _ => { }, context => context.Command.CopyBuffer(source, 0, destination, 0, destination.SizeInBytes));
}

public abstract class SubmitBatchingTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    private DeviceBuffer CreateValueBuffer(uint value)
    {
        DeviceBuffer buffer = RF.CreateBuffer(new BufferDescription(sizeof(uint), BufferUsage.StructuredBufferReadWrite));
        GD.UpdateBuffer(buffer, 0, new[] { value });
        return buffer;
    }

    [Fact]
    public void MultiPassGraph_IssuesOneQueueSubmit()
    {
        VkGraphicsDevice vk = (VkGraphicsDevice)GD;

        DeviceBuffer sourceA = CreateValueBuffer(1);
        DeviceBuffer sourceB = CreateValueBuffer(2);
        DeviceBuffer sourceC = CreateValueBuffer(3);
        DeviceBuffer destination = RF.CreateBuffer(new BufferDescription(sizeof(uint), BufferUsage.StructuredBufferReadWrite));

        using FrameGraph graph = new(GD, "Test");
        BufferWrites.Add(graph, "PassA", sourceA, destination);
        BufferWrites.Add(graph, "PassB", sourceB, destination);
        BufferWrites.Add(graph, "PassC", sourceC, destination);

        int before = vk.GraphicsQueueSubmitCount;

        graph.Execute();
        GD.WaitForIdle();

        Assert.Equal(1, vk.GraphicsQueueSubmitCount - before);
    }

    [Fact]
    public void MultiPassGraph_PreservesPassOrdering()
    {
        DeviceBuffer sourceA = CreateValueBuffer(11);
        DeviceBuffer sourceB = CreateValueBuffer(22);
        DeviceBuffer sourceC = CreateValueBuffer(33);
        DeviceBuffer destination = RF.CreateBuffer(new BufferDescription(sizeof(uint), BufferUsage.StructuredBufferReadWrite));

        using FrameGraph graph = new(GD, "Test");
        BufferWrites.Add(graph, "PassA", sourceA, destination);
        BufferWrites.Add(graph, "PassB", sourceB, destination);
        BufferWrites.Add(graph, "PassC", sourceC, destination);

        graph.Execute();
        GD.WaitForIdle();

        DeviceBuffer readback = GetReadback(destination);
        Span<uint> map = GD.Map<uint>(readback);
        uint result = map[0];
        GD.Unmap(readback);

        Assert.Equal(33u, result);
    }
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanSubmitBatchingTests : SubmitBatchingTests<VulkanDeviceCreator> { }
#endif
