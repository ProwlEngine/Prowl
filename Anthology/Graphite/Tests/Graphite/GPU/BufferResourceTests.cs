#nullable enable

using Prowl.Graphite.RenderGraph;

using Xunit;

namespace Prowl.Graphite.Tests;

// Coverage for graph buffer resources (Phase B): GetOutputBuffer / GetInputBuffer declarations, the
// BufferHandle resolution/caching seam through RenderContext.GetRenderBuffer, cross-pass sharing of one
// transient buffer, and a compute pass writing a graph buffer that is copied back for verification.

file readonly struct BufferView : IRenderView
{
    public BufferView(uint width, uint height)
    {
        PixelWidth = width;
        PixelHeight = height;
    }

    public uint PixelWidth { get; }
    public uint PixelHeight { get; }
}

file sealed class BufferWriterPass : IPass<BufferView>
{
    private readonly RenderResourceID _id;
    private readonly GraphBufferDesc _desc;
    private BufferHandle _handle;

    public BufferWriterPass(RenderResourceID id, GraphBufferDesc desc)
    {
        _id = id;
        _desc = desc;
    }

    public string Name => "Writer";
    public DeviceBuffer? Resolved { get; private set; }

    public void Setup(RenderContextBuilder builder) => _handle = builder.GetOutputBuffer(_id, _desc);

    public void Render(RenderContext<BufferView> context) => Resolved = context.GetRenderBuffer(_handle);
}

file sealed class BufferReaderPass : IPass<BufferView>
{
    private readonly RenderResourceID _id;
    private BufferHandle _handle;

    public BufferReaderPass(RenderResourceID id) => _id = id;

    public string Name => "Reader";
    public DeviceBuffer? Resolved { get; private set; }

    public void Setup(RenderContextBuilder builder) => _handle = builder.GetInputBuffer(_id);

    public void Render(RenderContext<BufferView> context) => Resolved = context.GetRenderBuffer(_handle);
}

file sealed class NoOpBufferPresentPass : IPresentPass<BufferView>
{
    public string Name => "Present";
    public void Setup(PresentContextBuilder builder) { }
    public void Present(RenderContext<BufferView> context) { }
}

file sealed class ComputeWriteReadbackPass : IPass<BufferView>
{
    private readonly RenderResourceID _id;
    private readonly GraphBufferDesc _desc;
    private readonly ComputeProgram _compute;
    private readonly DeviceBuffer _source;
    private readonly DeviceBuffer _readback;
    private readonly uint _side;
    private BufferHandle _handle;

    public ComputeWriteReadbackPass(
        RenderResourceID id, GraphBufferDesc desc, ComputeProgram compute,
        DeviceBuffer source, DeviceBuffer readback, uint side)
    {
        _id = id;
        _desc = desc;
        _compute = compute;
        _source = source;
        _readback = readback;
        _side = side;
    }

    public string Name => "ComputeWriteReadback";

    public void Setup(RenderContextBuilder builder) => _handle = builder.GetOutputBuffer(_id, _desc);

    public void Render(RenderContext<BufferView> context)
    {
        DeviceBuffer destination = context.GetRenderBuffer(_handle);

        PropertySet props = new();
        props.SetInt("Width", (int)_side);
        props.SetInt("Height", (int)_side);
        props.SetBuffer("Source", _source, readOnly: false);
        props.SetBuffer("Destination", destination, readOnly: false);

        CommandBuffer cl = context.GetCommandBuffer(Name);
        cl.SetComputeShader(_compute);
        cl.SetProperties(props);
        cl.Dispatch(1, 1, 1);
        cl.CopyBuffer(destination, 0, _readback, 0, destination.SizeInBytes);
        context.SubmitCommandBuffer(cl);
    }
}

file sealed class BufferHistoryPass : IPass<BufferView>
{
    private readonly RenderResourceID _id;
    private readonly uint _sizeInBytes;
    private readonly DeviceBuffer _source;
    private readonly DeviceBuffer _readback;
    private BufferHandle _handle;

    public BufferHistoryPass(RenderResourceID id, uint sizeInBytes, DeviceBuffer source, DeviceBuffer readback)
    {
        _id = id;
        _sizeInBytes = sizeInBytes;
        _source = source;
        _readback = readback;
    }

    public string Name => "History";

    public void Setup(RenderContextBuilder builder)
        => _handle = builder.GetOutputBuffer(_id, GraphBufferDesc.Structured(_sizeInBytes / 4, 4), history: 1);

    public void Render(RenderContext<BufferView> context)
    {
        DeviceBuffer current = context.GetRenderBuffer(_handle, 0);
        DeviceBuffer previous = context.GetRenderBuffer(_handle, 1);

        CommandBuffer cl = context.GetCommandBuffer(Name);
        cl.CopyBuffer(_source, 0, current, 0, _sizeInBytes);
        cl.CopyBuffer(previous, 0, _readback, 0, _sizeInBytes);
        context.SubmitCommandBuffer(cl);
    }
}

file sealed class BufferTestPipeline : RenderPipeline<BufferView>
{
    private readonly IPass<BufferView>[] _passes;

    public BufferTestPipeline(params IPass<BufferView>[] passes) => _passes = passes;

    protected override void InitializePasses()
    {
        foreach (IPass<BufferView> pass in _passes)
            AddPass(pass);

        SetPresentPass(new NoOpBufferPresentPass());
    }
}

public abstract class BufferResourceTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    [Fact]
    public void GetRenderBuffer_WriterAndReaderOfSameResource_ResolveToSameInstance()
    {
        RenderResourceID id = RenderResourceID.Intern("bufres_shared");
        GraphBufferDesc desc = GraphBufferDesc.Structured(16, sizeof(float));
        BufferWriterPass writer = new(id, desc);
        BufferReaderPass reader = new(id);
        using BufferTestPipeline pipeline = new(writer, reader);

        GD.DispatchGraph(pipeline, new BufferView[] { new(64, 64) });
        GD.WaitForIdle();

        Assert.NotNull(writer.Resolved);
        Assert.Same(writer.Resolved, reader.Resolved);
    }

    [SkippableFact]
    public void ComputePass_WritesGraphBuffer_CopiedBackWithExpectedValues()
    {
        Skip.IfNot(GD.Features.ComputeShader);

        const uint side = 16;
        const uint count = side * side;

        DeviceBuffer source = RF.CreateBuffer(new BufferDescription(count * sizeof(float), BufferUsage.StructuredBufferReadWrite, sizeof(float)));
        float[] initial = new float[count];
        for (int i = 0; i < count; i++)
            initial[i] = i;
        GD.UpdateBuffer(source, 0, initial);

        DeviceBuffer readback = RF.CreateBuffer(new BufferDescription(count * sizeof(float), BufferUsage.Staging));

        ShaderStageDescription stage = TestShaderLoader.LoadCompute(GD.BackendType, "BasicComputeTest.slang");
        ResourceLayoutDescription[] layouts =
        [
            new ResourceLayoutDescription
            {
                Set = 0,
                Elements =
                [
                    new ResourceLayoutElementDescription("Params", ResourceKind.UniformBuffer, ShaderStages.Compute, 0)
                    {
                        UniformFields =
                        [
                            new UniformBlockField("Width", 0, sizeof(uint), UniformScalarType.Int1),
                            new UniformBlockField("Height", sizeof(uint), sizeof(uint), UniformScalarType.Int1),
                        ]
                    },
                    new ResourceLayoutElementDescription("Source", ResourceKind.StructuredBufferReadWrite, ShaderStages.Compute, 1),
                    new ResourceLayoutElementDescription("Destination", ResourceKind.StructuredBufferReadWrite, ShaderStages.Compute, 2),
                ]
            }
        ];
        ComputeProgram compute = RF.CreateComputeProgram(new ComputeDescription(stage, layouts, 16, 16, 1));

        GraphBufferDesc desc = GraphBufferDesc.Structured(count, sizeof(float));
        ComputeWriteReadbackPass pass = new(
            RenderResourceID.Intern("bufres_compute_out"), desc, compute, source, readback, side);
        using BufferTestPipeline pipeline = new(pass);

        GD.DispatchGraph(pipeline, new BufferView[] { new(64, 64) });
        GD.WaitForIdle();

        MappedResourceView<float> map = GD.Map<float>(readback, MapMode.Read);
        for (int i = 0; i < count; i++)
            Assert.Equal(i, map[i]);
        GD.Unmap(readback);
    }

    [Fact]
    public void HistoryBuffer_FramesAgo1_ReadsPreviousExecutionsWrite()
    {
        const uint size = 16;
        const int floats = (int)(size / 4);

        DeviceBuffer source = RF.CreateBuffer(new BufferDescription(size, BufferUsage.StructuredBufferReadWrite, 4));
        DeviceBuffer readback = RF.CreateBuffer(new BufferDescription(size, BufferUsage.Staging));
        BufferHistoryPass pass = new(RenderResourceID.Intern("bufres_history"), size, source, readback);
        using BufferTestPipeline pipeline = new(pass);

        float[]? previousValues = null;
        for (int frame = 0; frame < 3; frame++)
        {
            float[] values = new float[floats];
            for (int i = 0; i < floats; i++)
                values[i] = frame * 10 + i + 1;
            GD.UpdateBuffer(source, 0, values);

            GD.DispatchGraph(pipeline, new BufferView[] { new(64, 64) });
            GD.WaitForIdle();

            if (previousValues != null)
            {
                MappedResourceView<float> map = GD.Map<float>(readback, MapMode.Read);
                for (int i = 0; i < floats; i++)
                    Assert.Equal(previousValues[i], map[i]);
                GD.Unmap(readback);
            }

            previousValues = values;
        }
    }
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanBufferResourceTests : BufferResourceTests<VulkanDeviceCreator> { }
#endif
