using Prowl.Graphite.RenderGraph;
using Prowl.Graphite.Vk;

using Xunit;

namespace Prowl.Graphite.Tests;

file readonly struct SubmitBatchingView : IRenderView
{
    public uint PixelWidth => 32;
    public uint PixelHeight => 32;
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

    public void Render(RenderContext<SubmitBatchingView> context)
    {
        CommandBuffer cl = context.GetCommandBuffer(_name);
        cl.CopyBuffer(_source, 0, _destination, 0, _destination.SizeInBytes);
        context.SubmitCommandBuffer(cl);
    }
}

file sealed class NoOpSubmitBatchingPresentPass : IPresentPass<SubmitBatchingView>
{
    public string Name => "SubmitBatchingPresent";
    public void Setup(PresentContextBuilder builder) { }
    public void Present(RenderContext<SubmitBatchingView> context) { }
}

file sealed class MidExecutionTransferPresentPass : IPresentPass<SubmitBatchingView>
{
    private readonly DeviceBuffer _source;
    private readonly DeviceBuffer _staging;

    public MidExecutionTransferPresentPass(DeviceBuffer source, DeviceBuffer staging)
    {
        _source = source;
        _staging = staging;
    }

    public string Name => "TransferPresent";

    public void Setup(PresentContextBuilder builder) { }

    public void Present(RenderContext<SubmitBatchingView> context)
    {
        TransferCommandBuffer transfer = context.GetTransferCommandBuffer("MidExecutionTransfer");
        transfer.Begin();
        transfer.CopyBuffer(_source, 0, _staging, 0, _source.SizeInBytes);
        transfer.End();
        context.SubmitTransferCommandBuffer(transfer);
    }
}

file sealed class SubmitBatchingPipeline : RenderPipeline<SubmitBatchingView>
{
    private readonly IPass<SubmitBatchingView>[] _passes;
    private readonly IPresentPass<SubmitBatchingView> _present;

    public SubmitBatchingPipeline(IPresentPass<SubmitBatchingView> present, params IPass<SubmitBatchingView>[] passes)
    {
        _present = present;
        _passes = passes;
    }

    protected override void InitializePasses()
    {
        foreach (IPass<SubmitBatchingView> pass in _passes)
            AddPass(pass);
        SetPresentPass(_present);
    }
}

public abstract class SubmitBatchingTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    private DeviceBuffer CreateValueBuffer(uint value)
    {
        DeviceBuffer buffer = RF.CreateBuffer(new BufferDescription(sizeof(uint), BufferUsage.StructuredBufferReadWrite, sizeof(uint)));
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
        DeviceBuffer destination = RF.CreateBuffer(new BufferDescription(sizeof(uint), BufferUsage.StructuredBufferReadWrite, sizeof(uint)));

        using SubmitBatchingPipeline pipeline = new(
            new NoOpSubmitBatchingPresentPass(),
            new BufferWritePass("PassA", sourceA, destination),
            new BufferWritePass("PassB", sourceB, destination),
            new BufferWritePass("PassC", sourceC, destination));

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
        DeviceBuffer destination = RF.CreateBuffer(new BufferDescription(sizeof(uint), BufferUsage.StructuredBufferReadWrite, sizeof(uint)));

        using SubmitBatchingPipeline pipeline = new(
            new NoOpSubmitBatchingPresentPass(),
            new BufferWritePass("PassA", sourceA, destination),
            new BufferWritePass("PassB", sourceB, destination),
            new BufferWritePass("PassC", sourceC, destination));

        GD.DispatchGraph(pipeline, new SubmitBatchingView[] { new() });
        GD.WaitForIdle();

        DeviceBuffer readback = GetReadback(destination);
        MappedResourceView<uint> map = GD.Map<uint>(readback, MapMode.Read);
        uint result = map[0];
        GD.Unmap(readback);

        Assert.Equal(33u, result);
    }

    [Fact]
    public void TransferMidExecution_IsOrderedAfterPriorPasses()
    {
        DeviceBuffer source = CreateValueBuffer(777);
        DeviceBuffer destination = RF.CreateBuffer(new BufferDescription(sizeof(uint), BufferUsage.StructuredBufferReadWrite, sizeof(uint)));
        DeviceBuffer staging = RF.CreateBuffer(new BufferDescription(sizeof(uint), BufferUsage.Staging));

        using SubmitBatchingPipeline pipeline = new(
            new MidExecutionTransferPresentPass(destination, staging),
            new BufferWritePass("PassA", source, destination));

        GD.DispatchGraph(pipeline, new SubmitBatchingView[] { new() });
        GD.WaitForIdle();

        MappedResourceView<uint> map = GD.Map<uint>(staging, MapMode.Read);
        uint result = map[0];
        GD.Unmap(staging);

        Assert.Equal(777u, result);
    }

    [Fact]
    public void ExecutionWithNoCommandBuffers_StillSignalsCompletionFence()
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
