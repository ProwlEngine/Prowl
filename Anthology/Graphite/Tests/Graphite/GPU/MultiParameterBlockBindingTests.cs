using Xunit;

namespace Prowl.Graphite.Tests;

// Regression coverage for a binding-point aliasing bug where two buffers with the same local
// ParameterSet binding could get resolved to the same shader-wide binding by accident
// (i.e set 0, buffer 0 resolving to the same location as set 1, buffer 0)
public abstract class MultiParameterBlockBindingTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    [SkippableFact]
    public void TwoParameterBlocks_EachKeepTheirOwnScalarData()
    {
        Skip.IfNot(GD.Features.ComputeShader);

        DeviceBuffer output = RF.CreateBuffer(new BufferDescription(
            2 * sizeof(uint), BufferUsage.StructuredBufferReadWrite, sizeof(uint)));

        ComputeProgram program = CreateProgram();

        PropertySet props = new();
        props.SetInt("valueA", 111);
        props.SetInt("valueB", 222);
        props.SetBuffer("Output", output, readOnly: false);

        Frame frame = GD.BeginFrame();
        CommandBuffer cl = RF.CreateCommandBuffer();
        cl.Begin();
        cl.SetComputeShader(program);
        cl.SetProperties(props);
        cl.Dispatch(1, 1, 1);
        cl.End();

        frame.SubmitCommands(cl);
        GD.EndFrame(frame);
        GD.WaitForIdle();

        DeviceBuffer readback = GetReadback(output);
        MappedResourceView<uint> map = GD.Map<uint>(readback, MapMode.Read);
        uint valueA = map[0];
        uint valueB = map[1];
        GD.Unmap(readback);

        // If BlockA and BlockB alias onto the same GL binding point, whichever is bound last
        // wins for both reads: valueA and valueB come out equal (both 111 or both 222) instead
        // of each block keeping its own value.
        Assert.Equal(111u, valueA);
        Assert.Equal(222u, valueB);
    }

    private ComputeProgram CreateProgram()
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
public class VulkanMultiParameterBlockBindingTests : MultiParameterBlockBindingTests<VulkanDeviceCreator> { }
#endif
#if TEST_D3D11
[Trait("Backend", "D3D11")]
[Collection("GPU Tests")]
public class D3D11MultiParameterBlockBindingTests : MultiParameterBlockBindingTests<D3D11DeviceCreator> { }
#endif
