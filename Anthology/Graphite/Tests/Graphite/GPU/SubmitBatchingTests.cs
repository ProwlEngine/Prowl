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

file sealed class MidExecutionTransferPass : IPass<SubmitBatchingView>
{
    private readonly DeviceBuffer _source;
    private readonly DeviceBuffer _staging;

    public MidExecutionTransferPass(DeviceBuffer source, DeviceBuffer staging)
    {
        _source = source;
        _staging = staging;
    }

    public string Name => "Transfer";

    public void Setup(RenderContextBuilder builder) { }

    public void Render(RenderContext<SubmitBatchingView> context, CommandBuffer cmd)
    {
        CommandBuffer copy = context.GetCommandBuffer("MidExecutionCopy");
        copy.CopyBuffer(_source, 0, _staging, 0, _source.SizeInBytes);
        context.SubmitCommandBuffer(copy);
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

    [Fact]
    public void TransferMidExecution_IsOrderedAfterPriorPasses()
    {
        DeviceBuffer source = CreateValueBuffer(777);
        DeviceBuffer destination = RF.CreateBuffer(new BufferDescription(sizeof(uint), BufferUsage.StructuredBufferReadWrite));
        DeviceBuffer staging = RF.CreateBuffer(new BufferDescription(sizeof(uint), BufferUsage.Staging));

        using RenderPipeline<SubmitBatchingView> pipeline = new([
            new BufferWritePass("PassA", source, destination),
            new MidExecutionTransferPass(destination, staging)]);

        GD.DispatchGraph(pipeline, new SubmitBatchingView[] { new() });
        GD.WaitForIdle();

        Span<uint> map = GD.Map<uint>(staging);
        uint result = map[0];
        GD.Unmap(staging);

        Assert.Equal(777u, result);
    }

    [Fact]
    public void ExecutionWithNoCommandBuffers_StillCompletes()
    {
        ExecutionTask task = GD.BeginExecution();
        GD.CompleteExecution(task);

        Assert.True(GD.WaitForExecution(task, ulong.MaxValue));
        Assert.True(GD.IsExecutionComplete(task));
    }
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanSubmitBatchingTests : SubmitBatchingTests<VulkanDeviceCreator> { }
#endif
