using System;
using Xunit;

namespace Prowl.Graphite.Tests;

public abstract class ExplicitWritableUniformBufferTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    private const uint Side = 16;
    private const uint Count = Side * Side;

    [SkippableFact]
    public void CallerOwnedBuffer_IsBoundAsIs_LooseUniformsNeverWriteIntoIt()
    {
        ComputeProgram program = CreateProgram(TwoFields());
        DeviceBuffer ubo = RF.CreateBuffer(new BufferDescription(16, BufferUsage.UniformBuffer));
        GD.UpdateBuffer(ubo, 0, new uint[] { Side, Side, 0, 0 });

        DeviceBuffer source = RF.CreateBuffer(new BufferDescription(Count * sizeof(float), BufferUsage.StructuredBufferReadWrite));
        DeviceBuffer destination = RF.CreateBuffer(new BufferDescription(Count * sizeof(float), BufferUsage.StructuredBufferReadWrite));
        float[] seed = new float[Count];
        for (int i = 0; i < Count; i++) seed[i] = i;
        GD.UpdateBuffer(source, 0, seed);

        PropertySet props = new();
        props.SetUniformBuffer("Params", ubo);
        props.SetInt("Width", 3);
        props.SetInt("Height", 5);
        props.SetBuffer("Source", source);
        props.SetBuffer("Destination", destination);

        GD.RunTestGraph((context, cl) =>
        {
            cl.SetComputeShader(program);
            cl.SetProperties(props);
            cl.Dispatch(1, 1, 1);
        });
        GD.WaitForIdle();

        AssertCopiedSource(seed, destination);

        DeviceBuffer readback = GetReadback(ubo);
        Span<uint> map = GD.Map<uint>(readback);
        Assert.Equal(Side, map[0]);
        Assert.Equal(Side, map[1]);
        GD.Unmap(readback);
    }

    // ---- helpers ----

    private void AssertCopiedSource(float[] seed, DeviceBuffer destination)
    {
        DeviceBuffer readback = GetReadback(destination);
        Span<float> map = GD.Map<float>(readback);
        for (int i = 0; i < seed.Length; i++)
            Assert.Equal(seed[i], map[i]);
        GD.Unmap(readback);
    }

    private static UniformBlockField[] TwoFields() =>
    [
        new UniformBlockField("Width", 0, sizeof(uint), UniformScalarType.Int1),
        new UniformBlockField("Height", sizeof(uint), sizeof(uint), UniformScalarType.Int1),
    ];

    private ComputeProgram CreateProgram(UniformBlockField[] fields)
    {
        ShaderStageDescription stage = TestShaderLoader.LoadCompute(GD.BackendType, "BasicComputeTest.glsl");
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
