using System;
using Xunit;

namespace Prowl.Graphite.Tests;

// Behavioral coverage for uniform data flowing through multiple render-graph passes within a single
// execution
public abstract class UniformArenaTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    [SkippableFact]
    public void SharedPropertySet_AcrossSeparatePassesInOneExecution_EachPassCorrect()
    {

        const int n = 4;
        ComputeProgram program = CreateTwoBlockProgram();
        DeviceBuffer[] outputs = new DeviceBuffer[n];
        for (int i = 0; i < n; i++) outputs[i] = CreateOutput();

        // One PropertySet instance carries the loose uniforms for every pass, the way per-view
        // uniforms get reused across a graph's passes. Only the output buffer changes per pass.
        PropertySet shared = new();
        shared.SetInt("valueA", 555);
        shared.SetInt("valueB", 666);

        GD.RunTestGraphPasses(n, (context, cl, i) =>
        {
            cl.SetComputeShader(program);
            shared.SetBuffer("Output", outputs[i]);
            cl.SetProperties(shared);
            cl.Dispatch(1, 1, 1);
        });
        GD.WaitForIdle();

        for (int i = 0; i < n; i++)
        {
            uint[] r = Read(outputs[i]);
            Assert.Equal(555u, r[0]);
            Assert.Equal(666u, r[1]);
        }
    }

    [SkippableFact]
    public void SetPropertiesSecondSet_SwapsInSameValueViaDifferentEntryObject_StillDispatchesCorrectly()
    {

        ComputeProgram program = CreateTwoBlockProgram();
        DeviceBuffer output1 = CreateOutput();
        DeviceBuffer output2 = CreateOutput();

        PropertySet props = new();
        props.SetInt("valueA", 42);
        props.SetInt("valueB", 100);
        props.SetBuffer("Output", output1);

        PropertySet other = new();
        other.SetInt("valueA", 42);
        other.SetInt("valueB", 200);
        other.SetBuffer("Output", output2);

        GD.RunTestGraphPasses(2, (context, cl, i) =>
        {
            cl.SetComputeShader(program);
            cl.SetProperties(props);
            if (i == 1)
                cl.SetProperties(other);
            cl.Dispatch(1, 1, 1);
        });
        GD.WaitForIdle();

        uint[] r1 = Read(output1);
        uint[] r2 = Read(output2);
        Assert.Equal(42u, r1[0]);
        Assert.Equal(100u, r1[1]);
        Assert.Equal(42u, r2[0]);
        Assert.Equal(200u, r2[1]);
    }

    // ---- helpers ----

    private DeviceBuffer CreateOutput()
        => RF.CreateBuffer(new BufferDescription(2 * sizeof(uint), BufferUsage.StructuredBufferReadWrite));

    private uint[] Read(DeviceBuffer output)
    {
        DeviceBuffer readback = GetReadback(output);
        Span<uint> map = GD.Map<uint>(readback);
        uint[] result = [map[0], map[1]];
        GD.Unmap(readback);
        return result;
    }

    private ComputeProgram CreateTwoBlockProgram()
    {
        ShaderStageDescription stage = TestShaderLoader.LoadCompute(GD.BackendType, "MultiParameterBlockBindingTest.slang");
        ResourceLayoutDescription[] layouts =
        [
            new ResourceLayoutDescription
            {
                Set = 0,
                Elements =
                [
                    new ResourceLayoutElementDescription("BlockA", ResourceKind.UniformBuffer, ShaderStages.Compute, 0)
                    {
                        UniformFields = [new UniformBlockField("valueA", 0, sizeof(uint), UniformScalarType.Int1)]
                    },
                    new ResourceLayoutElementDescription("Output", ResourceKind.StructuredBufferReadWrite, ShaderStages.Compute, 1)
                    {
                    },
                ]
            },
            new ResourceLayoutDescription
            {
                Set = 1,
                Elements =
                [
                    new ResourceLayoutElementDescription("BlockB", ResourceKind.UniformBuffer, ShaderStages.Compute, 0)
                    {
                        UniformFields = [new UniformBlockField("valueB", 0, sizeof(uint), UniformScalarType.Int1)]
                    },
                ]
            }
        ];
        return RF.CreateComputeProgram(new ComputeDescription(stage, layouts, 1, 1, 1));
    }
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanUniformArenaTests : UniformArenaTests<VulkanDeviceCreator> { }
#endif
