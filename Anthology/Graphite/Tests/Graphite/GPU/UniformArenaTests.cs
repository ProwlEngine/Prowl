using Xunit;

namespace Prowl.Graphite.Tests;

// Behavioral coverage for uniform data flowing through multiple render-graph passes within a single
// execution
public abstract class UniformArenaTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    [SkippableFact]
    public void SharedPropertySet_AcrossSeparatePassesInOneExecution_EachPassCorrect()
    {
        Skip.IfNot(GD.Features.ComputeShader);

        const int n = 4;
        ComputeProgram program = CreateTwoBlockProgram();
        DeviceBuffer[] outputs = new DeviceBuffer[n];
        for (int i = 0; i < n; i++) outputs[i] = CreateOutput();

        // One PropertySet instance carries the loose uniforms for every pass, the way per-view
        // uniforms get reused across a graph's passes. Only the output buffer changes per pass.
        PropertySet shared = new();
        shared.SetInt("valueA", 555);
        shared.SetInt("valueB", 666);

        GD.RunTestGraph(context =>
        {
            for (int i = 0; i < n; i++)
            {
                CommandBuffer cl = context.GetCommandBuffer($"Pass{i}");
                cl.SetComputeShader(program);
                shared.SetBuffer("Output", outputs[i], readOnly: false);
                cl.SetProperties(shared);
                cl.Dispatch(1, 1, 1);
                context.SubmitCommandBuffer(cl);
            }
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
    public void TwoProgramsShareSetAndBinding_SecondPassDoesNotSeeFirstProgramsUniform()
    {
        Skip.IfNot(GD.Features.ComputeShader);

        // Two separate program objects built from the same layout, so both declare their uniform
        // block at (set 0, binding 0). If the arena keyed a cached range only by (set, binding)
        // rather than by program identity, the second pass could pick up the first pass's range.
        ComputeProgram programX = CreateTwoBlockProgram();
        ComputeProgram programY = CreateTwoBlockProgram();

        DeviceBuffer outputX = CreateOutput();
        DeviceBuffer outputY = CreateOutput();

        PropertySet propsX = new();
        propsX.SetInt("valueA", 111);
        propsX.SetInt("valueB", 222);
        propsX.SetBuffer("Output", outputX, readOnly: false);

        PropertySet propsY = new();
        propsY.SetInt("valueA", 999);
        propsY.SetInt("valueB", 888);
        propsY.SetBuffer("Output", outputY, readOnly: false);

        GD.RunTestGraph(context =>
        {
            CommandBuffer cl1 = context.GetCommandBuffer("ProgramXPass");
            cl1.SetComputeShader(programX);
            cl1.SetProperties(propsX);
            cl1.Dispatch(1, 1, 1);
            context.SubmitCommandBuffer(cl1);

            CommandBuffer cl2 = context.GetCommandBuffer("ProgramYPass");
            cl2.SetComputeShader(programY);
            cl2.SetProperties(propsY);
            cl2.Dispatch(1, 1, 1);
            context.SubmitCommandBuffer(cl2);
        });
        GD.WaitForIdle();

        uint[] rx = Read(outputX);
        uint[] ry = Read(outputY);
        Assert.Equal(111u, rx[0]);
        Assert.Equal(222u, rx[1]);
        Assert.Equal(999u, ry[0]);
        Assert.Equal(888u, ry[1]);
    }

    [SkippableFact]
    public void ApplyOther_SwapsInSameValueViaDifferentEntryObject_StillDispatchesCorrectly()
    {
        Skip.IfNot(GD.Features.ComputeShader);

        ComputeProgram program = CreateTwoBlockProgram();
        DeviceBuffer output1 = CreateOutput();
        DeviceBuffer output2 = CreateOutput();

        PropertySet props = new();
        props.SetInt("valueA", 42);
        props.SetInt("valueB", 100);
        props.SetBuffer("Output", output1, readOnly: false);

        // A fresh PropertySet carries a brand new PropertyEntry for valueA with the exact same
        // value, plus a genuinely different valueB. ApplyOther merges both into props, swapping
        // the valueA entry's object identity without changing its bytes.
        PropertySet other = new();
        other.SetInt("valueA", 42);
        other.SetInt("valueB", 200);
        other.SetBuffer("Output", output2, readOnly: false);

        GD.RunTestGraph(context =>
        {
            CommandBuffer cl1 = context.GetCommandBuffer("BeforeMerge");
            cl1.SetComputeShader(program);
            cl1.SetProperties(props);
            cl1.Dispatch(1, 1, 1);
            context.SubmitCommandBuffer(cl1);

            props.ApplyOther(other);

            CommandBuffer cl2 = context.GetCommandBuffer("AfterMerge");
            cl2.SetComputeShader(program);
            cl2.SetProperties(props);
            cl2.Dispatch(1, 1, 1);
            context.SubmitCommandBuffer(cl2);
        });
        GD.WaitForIdle();

        uint[] r1 = Read(output1);
        uint[] r2 = Read(output2);
        Assert.Equal(42u, r1[0]);
        Assert.Equal(100u, r1[1]);
        Assert.Equal(42u, r2[0]);
        Assert.Equal(200u, r2[1]);
    }

    [SkippableFact]
    public void AlternatingUniforms_SeparatePassesInOneExecution_EachPassCorrect()
    {
        Skip.IfNot(GD.Features.ComputeShader);

        const int n = 6;
        ComputeProgram program = CreateTwoBlockProgram();
        DeviceBuffer[] outputs = new DeviceBuffer[n];
        for (int i = 0; i < n; i++) outputs[i] = CreateOutput();

        // Each pass rents its own command buffer, so this exercises the same flip-flop as
        // BindingOptimizationTests' AlternatingUniforms case but across passes rather than draws
        // within one recording - a range going stale between passes would surface here.
        GD.RunTestGraph(context =>
        {
            for (int i = 0; i < n; i++)
            {
                bool even = (i % 2) == 0;
                CommandBuffer cl = context.GetCommandBuffer($"Pass{i}");
                cl.SetComputeShader(program);
                PropertySet props = new();
                props.SetInt("valueA", even ? 1 : 9);
                props.SetInt("valueB", even ? 2 : 8);
                props.SetBuffer("Output", outputs[i], readOnly: false);
                cl.SetProperties(props);
                cl.Dispatch(1, 1, 1);
                context.SubmitCommandBuffer(cl);
            }
        });
        GD.WaitForIdle();

        for (int i = 0; i < n; i++)
        {
            bool even = (i % 2) == 0;
            uint[] r = Read(outputs[i]);
            Assert.Equal(even ? 1u : 9u, r[0]);
            Assert.Equal(even ? 2u : 8u, r[1]);
        }
    }

    // ---- helpers ----

    private DeviceBuffer CreateOutput()
        => RF.CreateBuffer(new BufferDescription(2 * sizeof(uint), BufferUsage.StructuredBufferReadWrite, sizeof(uint)));

    private uint[] Read(DeviceBuffer output)
    {
        DeviceBuffer readback = GetReadback(output);
        MappedResourceView<uint> map = GD.Map<uint>(readback, MapMode.Read);
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
                        GLUniformName = "block_BlockAData_0",
                        UniformFields = [new UniformBlockField("valueA", 0, sizeof(uint), UniformScalarType.Int1)]
                    },
                    new ResourceLayoutElementDescription("Output", ResourceKind.StructuredBufferReadWrite, ShaderStages.Compute, 1)
                    {
                        GLUniformName = "StructuredBuffer_uint_t_0"
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
                        GLUniformName = "block_BlockBData_0",
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
