using Xunit;

namespace Prowl.Graphite.Tests;

public abstract class ExplicitWritableUniformBufferTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    private const uint Side = 16;
    private const uint Count = Side * Side;

    [SkippableFact]
    public void RepeatedDispatches_UnchangedExplicitUniforms_AllCorrect()
    {

        const int n = 5;
        ComputeProgram program = CreateProgram(TwoFields());
        DeviceBuffer ubo = RF.CreateBuffer(new BufferDescription(16, BufferUsage.UniformBuffer));
        GD.UpdateBuffer(ubo, 0, new uint[] { 0, 0, 0, 0 });

        DeviceBuffer[] sources = new DeviceBuffer[n];
        DeviceBuffer[] destinations = new DeviceBuffer[n];
        float[][] seeds = new float[n][];

        PropertySet props = new();
        props.SetUniformBuffer("Params", ubo);
        props.SetInt("Width", (int)Side);
        props.SetInt("Height", (int)Side);

        GD.RunTestGraph(context =>
        {
            CommandBuffer cl = context.GetCommandBuffer();
            cl.SetComputeShader(program);
            for (int i = 0; i < n; i++)
            {
                sources[i] = RF.CreateBuffer(new BufferDescription(Count * sizeof(float), BufferUsage.StructuredBufferReadWrite, sizeof(float)));
                destinations[i] = RF.CreateBuffer(new BufferDescription(Count * sizeof(float), BufferUsage.StructuredBufferReadWrite, sizeof(float)));

                float[] seed = new float[Count];
                for (int j = 0; j < Count; j++) seed[j] = i * 1000 + j;
                seeds[i] = seed;
                GD.UpdateBuffer(sources[i], 0, seed);

                props.SetBuffer("Source", sources[i], readOnly: false);
                props.SetBuffer("Destination", destinations[i], readOnly: false);
                cl.SetProperties(props);
                cl.Dispatch(1, 1, 1);
            }
            context.SubmitCommandBuffer(cl);
        });
        GD.WaitForIdle();

        for (int i = 0; i < n; i++)
            AssertCopiedSource(seeds[i], destinations[i]);
    }

    [SkippableFact]
    public void ChangingUniform_BetweenDispatches_TakesEffect()
    {

        ComputeProgram program = CreateProgram(TwoFields());
        DeviceBuffer ubo = RF.CreateBuffer(new BufferDescription(16, BufferUsage.UniformBuffer));
        GD.UpdateBuffer(ubo, 0, new uint[] { 0, 0, 0, 0 });

        DeviceBuffer source = RF.CreateBuffer(new BufferDescription(Count * sizeof(float), BufferUsage.StructuredBufferReadWrite, sizeof(float)));
        DeviceBuffer destination = RF.CreateBuffer(new BufferDescription(Count * sizeof(float), BufferUsage.StructuredBufferReadWrite, sizeof(float)));

        PropertySet props = new();
        props.SetUniformBuffer("Params", ubo);
        props.SetBuffer("Source", source, readOnly: false);
        props.SetBuffer("Destination", destination, readOnly: false);

        GD.RunTestGraph(context =>
        {
            CommandBuffer cl = context.GetCommandBuffer();
            cl.SetComputeShader(program);

            props.SetInt("Width", 3);
            props.SetInt("Height", 5);
            cl.SetProperties(props);
            cl.Dispatch(1, 1, 1);

            props.SetInt("Width", 11);
            props.SetInt("Height", 22);
            cl.SetProperties(props);
            cl.Dispatch(1, 1, 1);

            context.SubmitCommandBuffer(cl);
        });
        GD.WaitForIdle();

        DeviceBuffer readback = GetReadback(ubo);
        MappedResourceView<uint> map = GD.Map<uint>(readback, MapMode.Read);
        uint width = map[0];
        uint height = map[1];
        GD.Unmap(readback);

        Assert.Equal(11u, width);
        Assert.Equal(22u, height);
    }

    [SkippableFact]
    public void UnsetBytes_InExplicitBuffer_AreLeftIntact()
    {

        ComputeProgram program = CreateProgram(TwoFields());
        DeviceBuffer ubo = RF.CreateBuffer(new BufferDescription(16, BufferUsage.UniformBuffer));
        uint sentinel = 0xAAAAAAAA;
        GD.UpdateBuffer(ubo, 0, new uint[] { sentinel, sentinel, sentinel, sentinel });

        DeviceBuffer source = RF.CreateBuffer(new BufferDescription(Count * sizeof(float), BufferUsage.StructuredBufferReadWrite, sizeof(float)));
        DeviceBuffer destination = RF.CreateBuffer(new BufferDescription(Count * sizeof(float), BufferUsage.StructuredBufferReadWrite, sizeof(float)));
        float[] seed = new float[Count];
        for (int i = 0; i < Count; i++) seed[i] = i;
        GD.UpdateBuffer(source, 0, seed);

        PropertySet props = new();
        props.SetUniformBuffer("Params", ubo);
        props.SetInt("Width", (int)Side);
        props.SetInt("Height", (int)Side);
        props.SetBuffer("Source", source, readOnly: false);
        props.SetBuffer("Destination", destination, readOnly: false);

        GD.RunTestGraph(context =>
        {
            CommandBuffer cl = context.GetCommandBuffer();
            cl.SetComputeShader(program);
            cl.SetProperties(props);
            cl.Dispatch(1, 1, 1);
            cl.Dispatch(1, 1, 1);
            context.SubmitCommandBuffer(cl);
        });
        GD.WaitForIdle();

        AssertCopiedSource(seed, destination);

        DeviceBuffer readback = GetReadback(ubo);
        MappedResourceView<uint> map = GD.Map<uint>(readback, MapMode.Read);
        Assert.Equal(Side, map[0]);
        Assert.Equal(Side, map[1]);
        Assert.Equal(sentinel, map[2]);
        Assert.Equal(sentinel, map[3]);
        GD.Unmap(readback);
    }

    [SkippableFact]
    public void NonContiguousSetFields_UnsetBytesZeroed_BlockWrittenWhole()
    {

        ComputeProgram program = CreateProgram(ThreeFields());
        DeviceBuffer ubo = RF.CreateBuffer(new BufferDescription(16, BufferUsage.UniformBuffer));
        uint sentinel = 0xAAAAAAAA;
        GD.UpdateBuffer(ubo, 0, new uint[] { sentinel, sentinel, sentinel, sentinel });

        DeviceBuffer source = RF.CreateBuffer(new BufferDescription(Count * sizeof(float), BufferUsage.StructuredBufferReadWrite, sizeof(float)));
        DeviceBuffer destination = RF.CreateBuffer(new BufferDescription(Count * sizeof(float), BufferUsage.StructuredBufferReadWrite, sizeof(float)));
        float[] seed = new float[Count];
        for (int i = 0; i < Count; i++) seed[i] = i;
        GD.UpdateBuffer(source, 0, seed);

        PropertySet props = new();
        props.SetUniformBuffer("Params", ubo);
        props.SetInt("Width", (int)Side);
        props.SetInt("Padding1", 777);
        props.SetBuffer("Source", source, readOnly: false);
        props.SetBuffer("Destination", destination, readOnly: false);

        GD.RunTestGraph(context =>
        {
            CommandBuffer cl = context.GetCommandBuffer();
            cl.SetComputeShader(program);
            cl.SetProperties(props);
            cl.Dispatch(1, 1, 1);
            context.SubmitCommandBuffer(cl);
        });
        GD.WaitForIdle();

        AssertCopiedSource(seed, destination);

        DeviceBuffer readback = GetReadback(ubo);
        MappedResourceView<uint> map = GD.Map<uint>(readback, MapMode.Read);
        Assert.Equal(Side, map[0]);
        Assert.Equal(0u, map[1]);
        Assert.Equal(777u, map[2]);
        Assert.Equal(sentinel, map[3]);
        GD.Unmap(readback);
    }

    // ---- helpers ----

    private void AssertCopiedSource(float[] seed, DeviceBuffer destination)
    {
        DeviceBuffer readback = GetReadback(destination);
        MappedResourceView<float> map = GD.Map<float>(readback, MapMode.Read);
        for (int i = 0; i < seed.Length; i++)
            Assert.Equal(seed[i], map[i]);
        GD.Unmap(readback);
    }

    private static UniformBlockField[] TwoFields() =>
    [
        new UniformBlockField("Width", 0, sizeof(uint), UniformScalarType.Int1),
        new UniformBlockField("Height", sizeof(uint), sizeof(uint), UniformScalarType.Int1),
    ];

    private static UniformBlockField[] ThreeFields() =>
    [
        new UniformBlockField("Width", 0, sizeof(uint), UniformScalarType.Int1),
        new UniformBlockField("Height", sizeof(uint), sizeof(uint), UniformScalarType.Int1),
        new UniformBlockField("Padding1", 2 * sizeof(uint), sizeof(uint), UniformScalarType.Int1),
    ];

    private ComputeProgram CreateProgram(UniformBlockField[] fields)
    {
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
                        UniformFields = fields
                    },
                    new ResourceLayoutElementDescription("Source", ResourceKind.StructuredBufferReadWrite, ShaderStages.Compute, 1),
                    new ResourceLayoutElementDescription("Destination", ResourceKind.StructuredBufferReadWrite, ShaderStages.Compute, 2),
                ]
            }
        ];
        return RF.CreateComputeProgram(new ComputeDescription(stage, layouts, 16, 16, 1));
    }
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanExplicitWritableUniformBufferTests : ExplicitWritableUniformBufferTests<VulkanDeviceCreator> { }
#endif
