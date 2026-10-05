using System;
using Prowl.Graphite.RenderGraph;
using Prowl.Graphite.Vk;

using Xunit;

namespace Prowl.Graphite.Tests;

file readonly struct SubmitBatchingView : IRenderView
{
    public uint PixelWidth => 32;
    public uint PixelHeight => 32;
    public int ViewId => 0;
}

file sealed class BufferWritePass : IPass<SubmitBatchingView>
{
    private readonly string _name;
    private readonly DeviceBuffer _source;
    private readonly DeviceBuffer _destination;

    public BufferWritePass(string name, DeviceBuffer source, DeviceBuffer destination)
    {
        _name = name;
        _source = source;
        _destination = destination;
    }

    public string Name => _name;

    public void Setup(RenderContextBuilder builder) { }

    public void Render(RenderContext<SubmitBatchingView> context, CommandBuffer cl)
    {
        cl.CopyBuffer(_source, 0, _destination, 0, _destination.SizeInBytes);
    }
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

        using RenderPipeline<SubmitBatchingView> pipeline = new([
            new BufferWritePass("PassA", sourceA, destination),
            new BufferWritePass("PassB", sourceB, destination),
            new BufferWritePass("PassC", sourceC, destination)]);

        int before = vk.GraphicsQueueSubmitCount;

        GD.DispatchGraph(pipeline, new SubmitBatchingView[] { new() });
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

        using RenderPipeline<SubmitBatchingView> pipeline = new([
            new BufferWritePass("PassA", sourceA, destination),
            new BufferWritePass("PassB", sourceB, destination),
            new BufferWritePass("PassC", sourceC, destination)]);

        GD.DispatchGraph(pipeline, new SubmitBatchingView[] { new() });
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
