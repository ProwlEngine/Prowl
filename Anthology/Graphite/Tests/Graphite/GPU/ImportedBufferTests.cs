using System;

using Prowl.Graphite.RenderGraph;

using Xunit;

namespace Prowl.Graphite.Tests;

file readonly struct BufferView : IRenderView
{
    public Framebuffer? Target => null;
    public uint PixelWidth => 4;
    public uint PixelHeight => 4;
    public int ViewId => 0;
}

file sealed class WriteValuesPass(DeviceBuffer data, uint[] values) : IPass
{
    private static readonly RenderResourceID s_data = RenderResourceID.Intern("Data");

    public string Name => "WriteValues";

    public void Setup(RenderContextBuilder builder) => builder.DeclareImportedBuffer(s_data, data, BufferAccess.TransferWrite);

    public void Render(RenderContext context, CommandBuffer cmd) => cmd.UpdateBuffer(data, 0, values);
}

file sealed class AddOnePass(DeviceBuffer data, DeviceBuffer result, ComputeProgram program) : IPass
{
    private static readonly RenderResourceID s_data = RenderResourceID.Intern("Data");
    private static readonly RenderResourceID s_result = RenderResourceID.Intern("Result");
    private readonly PropertySet _properties = new();

    public string Name => "AddOne";

    public void Setup(RenderContextBuilder builder)
    {
        builder.DeclareImportedBuffer(s_data, data, BufferAccess.ShaderRead);
        builder.DeclareImportedBuffer(s_result, result, BufferAccess.ShaderWrite);
    }

    public void Render(RenderContext context, CommandBuffer cmd)
    {
        _properties.SetBuffer("Input", context.GetRenderBuffer(new BufferHandle(s_data)));
        _properties.SetBuffer("Output", context.GetRenderBuffer(new BufferHandle(s_result)));
        cmd.SetComputeShader(program);
        cmd.SetProperties(_properties);
        cmd.Dispatch(1, 1, 1);
    }
}

file sealed class TwoImportsPass(DeviceBuffer first, DeviceBuffer second) : IPass
{
    private static readonly RenderResourceID s_data = RenderResourceID.Intern("Data");

    public string Name => "TwoImports";

    public void Setup(RenderContextBuilder builder)
    {
        builder.DeclareImportedBuffer(s_data, first);
        builder.DeclareImportedBuffer(s_data, second);
    }

    public void Render(RenderContext context, CommandBuffer cmd) { }
}

// Buffers that outlive a graph: one pipeline writes an imported buffer and a later pipeline of the same
// execution reads it, and the declaration rules.
public abstract class ImportedBufferTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    [Fact]
    public void ALaterPipeline_ReadsAnImportedBufferAnEarlierOneWrote()
    {
        DeviceBuffer data = RF.CreateBuffer(new BufferDescription(16, BufferUsage.StructuredBufferReadOnly));
        DeviceBuffer result = RF.CreateBuffer(new BufferDescription(16, BufferUsage.StructuredBufferReadWrite));
        ComputeProgram program = RF.CreateComputeProgram(new ComputeDescription(
            TestShaderLoader.LoadCompute(GD.BackendType, "BufferAddOne.glsl"),
            [new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("Input", ResourceKind.StructuredBufferReadOnly, ShaderStages.Compute, 0),
                new ResourceLayoutElementDescription("Output", ResourceKind.StructuredBufferReadWrite, ShaderStages.Compute, 1))],
            4, 1, 1));

        using RenderPipeline world = new([new WriteValuesPass(data, [10, 20, 30, 40])]);
        using RenderPipeline camera = new([new AddOnePass(data, result, program)]);
        IRenderView[] view = [new BufferView()];

        GD.DispatchGraph([new GraphDispatch(world, view), new GraphDispatch(camera, view)]);
        GD.WaitForIdle();

        DeviceBuffer readback = GetReadback(result);
        Span<uint> map = GD.Map<uint>(readback);
        Assert.Equal([11u, 21u, 31u, 41u], map[..4].ToArray());
        GD.Unmap(readback);
    }


    [Fact]
    public void OneId_ImportedAsTwoBuffers_Throws()
    {
        DeviceBuffer first = RF.CreateBuffer(new BufferDescription(16, BufferUsage.StructuredBufferReadOnly));
        DeviceBuffer second = RF.CreateBuffer(new BufferDescription(16, BufferUsage.StructuredBufferReadOnly));

        Assert.ThrowsAny<InvalidOperationException>(() => new RenderPipeline([new TwoImportsPass(first, second)]).Graph);
    }


    [Fact]
    public void ImportedBuffers_ReadOnly_AreInputs_AndNeedNoWriter()
    {
        DeviceBuffer data = RF.CreateBuffer(new BufferDescription(16, BufferUsage.StructuredBufferReadOnly));
        DeviceBuffer result = RF.CreateBuffer(new BufferDescription(16, BufferUsage.StructuredBufferReadWrite));
        ComputeProgram program = RF.CreateComputeProgram(new ComputeDescription(
            TestShaderLoader.LoadCompute(GD.BackendType, "BufferAddOne.glsl"),
            [new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("Input", ResourceKind.StructuredBufferReadOnly, ShaderStages.Compute, 0),
                new ResourceLayoutElementDescription("Output", ResourceKind.StructuredBufferReadWrite, ShaderStages.Compute, 1))],
            4, 1, 1));
        GD.UpdateBuffer(data, 0, new uint[] { 1, 2, 3, 4 });

        using RenderPipeline camera = new([new AddOnePass(data, result, program)]);
        GD.DispatchGraph(camera, new IRenderView[] { new BufferView() });
        GD.WaitForIdle();

        DeviceBuffer readback = GetReadback(result);
        Span<uint> map = GD.Map<uint>(readback);
        Assert.Equal([2u, 3u, 4u, 5u], map[..4].ToArray());
        GD.Unmap(readback);
    }
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanImportedBufferTests : ImportedBufferTests<VulkanDeviceCreator> { }
#endif
