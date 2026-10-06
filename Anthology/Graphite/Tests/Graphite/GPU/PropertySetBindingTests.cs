using System;

using Xunit;

namespace Prowl.Graphite.Tests;

public abstract class PropertySetBindingTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    private const uint Side = 16;
    private const uint Count = Side * Side;

    [SkippableFact]
    public void ReadOnlyUniformBuffer_FeedsContentsAndIgnoresScalarWrites()
    {

        // The buffer already carries the correct dimensions. Read-only binding must feed those
        // contents to the kernel and ignore the conflicting scalar writes, so the copy succeeds.
        DeviceBuffer ubo = RF.CreateBuffer(new BufferDescription(16, BufferUsage.UniformBuffer));
        GD.UpdateBuffer(ubo, 0, new uint[] { Side, Side, 0, 0 });

        float[] result = RunCompute((props, source, destination) =>
        {
            props.SetBuffer("Params", ubo);
            props.SetInt("Width", 1);
            props.SetInt("Height", 1);
            props.SetBuffer("Source", source);
            props.SetBuffer("Destination", destination);
        });

        AssertCopiedSource(result);
    }

    private void AssertCopiedSource(float[] destination)
    {
        for (int i = 0; i < Count; i++)
        {
            Assert.Equal(i, destination[i]);
        }
    }

    // Runs BasicComputeTest with a caller-configured PropertySet and returns the Destination
    // buffer contents. Source is seeded with 0..Count-1.
    private float[] RunCompute(Action<PropertySet, DeviceBuffer, DeviceBuffer> configure)
    {
        DeviceBuffer source = RF.CreateBuffer(new BufferDescription(
            Count * sizeof(float), BufferUsage.StructuredBufferReadWrite));
        DeviceBuffer destination = RF.CreateBuffer(new BufferDescription(
            Count * sizeof(float), BufferUsage.StructuredBufferReadWrite));

        float[] initial = new float[Count];
        for (int i = 0; i < Count; i++) initial[i] = i;
        GD.UpdateBuffer(source, 0, initial);

        ComputeProgram program = CreateBasicComputeProgram();

        PropertySet props = new();
        configure(props, source, destination);

        GD.RunTestGraph((context, cl) =>
        {
            cl.SetComputeShader(program);
            cl.SetProperties(props);
            cl.Dispatch(Side / 16, Side / 16, 1);
        });
        GD.WaitForIdle();

        DeviceBuffer readback = GetReadback(destination);
        Span<float> map = GD.Map<float>(readback);
        float[] result = new float[Count];
        for (int i = 0; i < Count; i++) result[i] = map[i];
        GD.Unmap(readback);
        return result;
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
public class VulkanPropertySetBindingTests : PropertySetBindingTests<VulkanDeviceCreator> { }
#endif
