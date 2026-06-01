using Xunit;

namespace Prowl.Veldrid.Tests;

// Core-path compute test: dispatches BasicComputeTest, which copies Source into Destination
// and doubles Source in place. Exercises the ComputeProgram + PropertySet (uniforms + RW
// structured buffers bound by name) + Dispatch path, with a staging readback.
public abstract class ComputeCoreTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    [Fact]
    public void BasicComputeTest_CopiesAndDoublesSource()
    {
        const uint width = 16;
        const uint height = 16;
        const uint count = width * height;

        DeviceBuffer source = RF.CreateBuffer(new BufferDescription(
            count * sizeof(float), BufferUsage.StructuredBufferReadWrite, sizeof(float)));
        DeviceBuffer destination = RF.CreateBuffer(new BufferDescription(
            count * sizeof(float), BufferUsage.StructuredBufferReadWrite, sizeof(float)));

        float[] initial = new float[count];
        for (int i = 0; i < count; i++)
        {
            initial[i] = i;
        }
        GD.UpdateBuffer(source, 0, initial);

        ComputeProgram program = CreateBasicComputeProgram();

        PropertySet props = new();
        props.SetInt("Width", (int)width);
        props.SetInt("Height", (int)height);
        props.SetBuffer("Source", source, readOnly: false);
        props.SetBuffer("Destination", destination, readOnly: false);

        CommandBuffer cl = RF.CreateCommandBuffer();
        cl.Begin();
        cl.SetComputeShader(program);
        cl.SetProperties(props);
        cl.Dispatch(width / 16, height / 16, 1);
        cl.End();

        Frame frame = GD.BeginFrame();
        frame.SubmitCommands(cl);
        GD.EndFrame(frame);
        GD.WaitForIdle();

        DeviceBuffer destReadback = GetReadback(destination);
        DeviceBuffer srcReadback = GetReadback(source);

        MappedResourceView<float> dst = GD.Map<float>(destReadback, MapMode.Read);
        MappedResourceView<float> src = GD.Map<float>(srcReadback, MapMode.Read);
        for (int i = 0; i < count; i++)
        {
            Assert.Equal(initial[i], dst[i]);
            Assert.Equal(initial[i] * 2.0f, src[i]);
        }
        GD.Unmap(destReadback);
        GD.Unmap(srcReadback);
    }

    private ComputeProgram CreateBasicComputeProgram()
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

        return RF.CreateComputeProgram(new ComputeDescription(stage, layouts, 16, 16, 1));
    }
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanComputeCoreTests : ComputeCoreTests<VulkanDeviceCreator> { }
#endif
#if TEST_D3D11
[Trait("Backend", "D3D11")]
[Collection("GPU Tests")]
public class D3D11ComputeCoreTests : ComputeCoreTests<D3D11DeviceCreator> { }
#endif
#if TEST_OPENGL
[Trait("Backend", "OpenGL")]
[Collection("GPU Tests")]
public class OpenGLComputeCoreTests : ComputeCoreTests<OpenGLDeviceCreator> { }
#endif
