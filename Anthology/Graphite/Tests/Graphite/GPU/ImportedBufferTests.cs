using System;


using Xunit;

namespace Prowl.Graphite.Tests;

file sealed class WriteValuesPass(DeviceBuffer data, uint[] values) : IExecutionPass
{
    private static readonly RenderResourceID s_data = RenderResourceID.Intern("Data");

    public string Name => "WriteValues";

    public void Setup(RenderContextBuilder builder) => builder.DeclareImportedBuffer(s_data, data, BufferAccess.TransferWrite);

    public void Render(RenderContext context, CommandBuffer cmd) => cmd.UpdateBuffer(data, 0, values);
}

file sealed class AddOnePass(DeviceBuffer data, DeviceBuffer result, ComputeProgram program, GraphKind kind) : IInstancePass
{
    private static readonly RenderResourceID s_data = RenderResourceID.Intern("Data");
    private static readonly RenderResourceID s_result = RenderResourceID.Intern("Result");
    private readonly PropertySet _properties = new();

    public string Name => "AddOne";

    public GraphKind Kind => kind;

    public void Setup(InstanceContextBuilder builder)
    {
        builder.DeclareImportedBuffer(s_data, data, BufferAccess.ShaderRead);
        builder.DeclareImportedBuffer(s_result, result, BufferAccess.ShaderWrite);
    }

    public void Render(InstanceContext context, CommandBuffer cmd)
    {
        _properties.SetBuffer("Input", context.GetRenderBuffer(new BufferHandle(s_data)));
        _properties.SetBuffer("Output", context.GetRenderBuffer(new BufferHandle(s_result)));
        cmd.SetComputeShader(program);
        cmd.SetProperties(_properties);
        cmd.Dispatch(1, 1, 1);
    }
}

file sealed class TwoImportsPass(DeviceBuffer first, DeviceBuffer second) : IExecutionPass
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

file sealed class SlotWritePass(uint[] values) : IInstancePass
{
    public static readonly RenderResourceID Slot = RenderResourceID.Intern("WriteSlot");

    private BufferHandle _slot;

    public string Name => "SlotWrite";

    public GraphKind Kind => TestViews.Kind;

    public void Setup(InstanceContextBuilder builder) => _slot = builder.DeclareImportSlotBuffer(Slot, BufferAccess.TransferWrite);

    public void Render(InstanceContext context, CommandBuffer cmd) => cmd.UpdateBuffer(context.GetRenderBuffer(_slot), 0, values);
}

file sealed class SlotCopyPass(DeviceBuffer readback) : IInstancePass
{
    public static readonly RenderResourceID Slot = RenderResourceID.Intern("CopySlot");

    private TextureHandle _slot;

    public string Name => "SlotCopy";

    public GraphKind Kind => TestViews.Kind;

    public void Setup(InstanceContextBuilder builder) => _slot = builder.DeclareImportSlotTexture(Slot, TextureState.TransferSrc);

    public void Render(InstanceContext context, CommandBuffer cmd)
    {
        Texture texture = context.GetRenderTexture(_slot).ColorTextures[0];
        cmd.CopyTextureToBuffer(texture, readback, 0, TextureRegion.Whole(texture));
    }
}

public abstract class ImportedBufferTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    [Fact]
    public void AChildInstance_ReadsAnImportedBufferTheRootWrote()
    {
        DeviceBuffer data = RF.CreateBuffer(new BufferDescription(16, BufferUsage.StructuredBufferReadOnly));
        DeviceBuffer result = RF.CreateBuffer(new BufferDescription(16, BufferUsage.StructuredBufferReadWrite));
        ComputeProgram program = RF.CreateComputeProgram(new ComputeDescription(
            TestShaderLoader.LoadCompute(GD.BackendType, "BufferAddOne.glsl"),
            [new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("Input", ResourceKind.StructuredBufferReadOnly, ShaderStages.Compute, 0),
                new ResourceLayoutElementDescription("Output", ResourceKind.StructuredBufferReadWrite, ShaderStages.Compute, 1))],
            4, 1, 1));

        GraphKind camera = GraphKind.Intern("Test.Camera");
        using RenderGraph graph = RenderGraph.Build([new WriteValuesPass(data, [10, 20, 30, 40]), new AddOnePass(data, result, program, camera)], [new GraphScope(camera, GraphKind.Root)]);
        GraphInstances instances = graph.CreateInstances();
        instances.Add(camera, 0);

        GD.DispatchGraph(graph, instances);
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

        Assert.ThrowsAny<InvalidOperationException>(() => RenderGraph.Build([new TwoImportsPass(first, second)]));
    }


    [Fact]
    public void ImportSlots_BindADifferentBufferPerInstance()
    {
        DeviceBuffer first = RF.CreateBuffer(new BufferDescription(16, BufferUsage.StructuredBufferReadWrite));
        DeviceBuffer second = RF.CreateBuffer(new BufferDescription(16, BufferUsage.StructuredBufferReadWrite));
        using RenderGraph graph = TestViews.Build([new SlotWritePass([1, 2, 3, 4])]);
        GraphInstances instances = graph.CreateInstances();
        instances.Add(TestViews.Kind, 0, imports: [GraphImport.Of(SlotWritePass.Slot, first)]);
        instances.Add(TestViews.Kind, 1, imports: [GraphImport.Of(SlotWritePass.Slot, second)]);

        GD.DispatchGraph(graph, instances);
        GD.WaitForIdle();

        foreach (DeviceBuffer buffer in new[] { first, second })
        {
            DeviceBuffer readback = GetReadback(buffer);
            Span<uint> map = GD.Map<uint>(readback);
            Assert.Equal([1u, 2u, 3u, 4u], map[..4].ToArray());
            GD.Unmap(readback);
        }
    }

    [Fact]
    public void ImportSlots_BindAPlainTexture()
    {
        Texture texture = RF.CreateTexture(TextureDescription.Texture2D(2, 2, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
        DeviceBuffer readback = CreateTexelReadbackBuffer<uint>(2, 2);
        GD.Record(cmd => cmd.UpdateTexture<uint>(texture, [0xFF00FF00, 0xFF00FF00, 0xFF00FF00, 0xFF00FF00]));
        using RenderGraph graph = TestViews.Build([new SlotCopyPass(readback)]);
        GraphInstances instances = graph.CreateInstances();
        instances.Add(TestViews.Kind, 0, imports: [GraphImport.Of(SlotCopyPass.Slot, texture)]);

        GD.WaitForExecution(GD.DispatchGraph(graph, instances));

        Assert.Equal(0xFF00FF00u, ReadTexels<uint>(readback, 2, 2)[1, 1]);
    }

    [Fact]
    public void ImportSlots_AnInstanceThatBindsNothing_ThrowsNamingTheSlot()
    {
        using RenderGraph graph = TestViews.Build([new SlotWritePass([1])]);

        RenderGraphException ex = Assert.Throws<RenderGraphException>(() => GD.DispatchGraph(graph, TestViews.Single(graph)));

        Assert.Contains("WriteSlot", ex.Message);
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

        using RenderGraph camera = TestViews.Build([new AddOnePass(data, result, program, TestViews.Kind)]);
        GD.DispatchGraph(camera, TestViews.Single(camera, new GraphExtent(4, 4)));
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
