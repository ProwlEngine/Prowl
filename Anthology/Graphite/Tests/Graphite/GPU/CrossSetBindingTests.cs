using System;
using Prowl.Graphite.Vk;

using Xunit;

namespace Prowl.Graphite.Tests;

// The layouts below are hand-written to match CrossSetBinding.glsl, mirroring the other binding
// suites. The compiler's reflected layouts cannot be used here: they carry the right sets and
// binding indices but no UniformFields, so scalar uniforms have nothing to bind by name.
//
// The sampler element deliberately shares the texture element's name, which is how
// SetTexture(name, texture, sampler) feeds the paired sampler slot.
public abstract class CrossSetBindingTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    private const uint OutputCount = 8;

    private ComputeProgram CreateProgram()
    {
        ShaderStageDescription stage = TestShaderLoader.LoadCompute(GD.BackendType, "CrossSetBinding.glsl");
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
                    new ResourceLayoutElementDescription("Output", ResourceKind.StructuredBufferReadWrite, ShaderStages.Compute, 1),
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
                    new ResourceLayoutElementDescription("Input", ResourceKind.StructuredBufferReadOnly, ShaderStages.Compute, 1),
                ]
            },
            new ResourceLayoutDescription
            {
                Set = 2,
                Elements =
                [
                    new ResourceLayoutElementDescription("BlockC", ResourceKind.UniformBuffer, ShaderStages.Compute, 0)
                    {
                        UniformFields = [new UniformBlockField("valueC", 0, sizeof(uint), UniformScalarType.Int1)]
                    },
                    new ResourceLayoutElementDescription("Tex", ResourceKind.TextureReadOnly, ShaderStages.Compute, 1),
                    new ResourceLayoutElementDescription("Tex", ResourceKind.Sampler, ShaderStages.Compute, 2),
                ]
            }
        ];
        return RF.CreateComputeProgram(new ComputeDescription(stage, layouts, 1, 1, 1));
    }

    private DeviceBuffer CreateOutput()
        => RF.CreateBuffer(new BufferDescription(
            OutputCount * sizeof(uint), BufferUsage.StructuredBufferReadWrite));

    private DeviceBuffer CreateInput(uint value)
    {
        DeviceBuffer buffer = RF.CreateBuffer(new BufferDescription(
            4 * sizeof(uint), BufferUsage.StructuredBufferReadOnly));
        GD.UpdateBuffer(buffer, 0, new uint[] { value, 0, 0, 0 });
        return buffer;
    }

    // A 1x1 texture whose red channel is `red`, so a sample at any UV returns it.
    private Texture CreateSolidTexture(byte red)
    {
        Texture texture = RF.CreateTexture(TextureDescription.Texture2D(
            1, 1, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
        GD.UpdateTexture(texture, new byte[] { red, 0, 0, 255 }, new TextureRegion(0, 0, 0, 1, 1, 1));
        return texture;
    }

    private uint[] Run(PropertySet props, DeviceBuffer output, ComputeProgram program)
    {
        GD.RunTestGraph((context, cl) =>
        {
            cl.SetComputeShader(program);
            cl.SetProperties(props);
            cl.Dispatch(1, 1, 1);
        });
        GD.WaitForIdle();

        return Read(output);
    }

    private uint[] Read(DeviceBuffer output)
    {
        DeviceBuffer readback = GetReadback(output);
        Span<uint> map = GD.Map<uint>(readback);
        uint[] result = new uint[OutputCount];
        for (int i = 0; i < OutputCount; i++) result[i] = map[i];
        GD.Unmap(readback);
        return result;
    }

    private PropertySet BuildProps(DeviceBuffer output, DeviceBuffer input, Texture texture,
        uint valueA = 101, uint valueB = 202, uint valueC = 303)
    {
        PropertySet props = new();
        props.SetInt("valueA", (int)valueA);
        props.SetInt("valueB", (int)valueB);
        props.SetInt("valueC", (int)valueC);
        props.SetBuffer("Output", output);
        props.SetBuffer("Input", input);
        props.SetTexture("Tex", texture, GD.LinearSampler);
        return props;
    }

    [Fact]
    public void AlternatingBindings_DoNotAliasInTheDescriptorCache()
    {
        DeviceBuffer output = CreateOutput();
        DeviceBuffer inputA = CreateInput(10);
        DeviceBuffer inputB = CreateInput(20);
        Texture textureA = CreateSolidTexture(30);
        Texture textureB = CreateSolidTexture(40);
        ComputeProgram program = CreateProgram();

        // Two binding shapes alternating on one program: each must resolve to its own cached set
        // every time, never to the other's.
        for (int i = 0; i < 4; i++)
        {
            uint[] a = Run(BuildProps(output, inputA, textureA, valueA: 1, valueB: 2, valueC: 3), output, program);
            Assert.Equal(1u, a[0]);
            Assert.Equal(2u, a[1]);
            Assert.Equal(10u, a[2]);
            Assert.Equal(3u, a[3]);
            Assert.Equal(30u, a[4]);

            uint[] b = Run(BuildProps(output, inputB, textureB, valueA: 4, valueB: 5, valueC: 6), output, program);
            Assert.Equal(4u, b[0]);
            Assert.Equal(5u, b[1]);
            Assert.Equal(20u, b[2]);
            Assert.Equal(6u, b[3]);
            Assert.Equal(40u, b[4]);
        }

        Texture solid = CreateSolidTexture(64);
        uint[] before = Run(BuildProps(output, inputA, solid), output, program);
        Assert.Equal(10u, before[2]);

        // Only set 1's structured buffer changes. The descriptor cache must key on it and leave
        // the other sets alone rather than reuse a stale set or rebuild everything wrong.
        uint[] after = Run(BuildProps(output, inputB, solid), output, program);

        Assert.Equal(20u, after[2]);
        Assert.Equal(101u, after[0]);
        Assert.Equal(303u, after[3]);
        Assert.Equal(64u, after[4]);
    }

    [Fact]
    public void SubRangesOfOneBuffer_BindAsDistinctResources()
    {
        DeviceBuffer output = CreateOutput();
        Texture texture = CreateSolidTexture(0);
        ComputeProgram program = CreateProgram();

        uint stride = GD.StructuredBufferMinOffsetAlignment;
        DeviceBuffer input = RF.CreateBuffer(new BufferDescription(
            stride * 2, BufferUsage.StructuredBufferReadOnly));

        uint[] contents = new uint[stride * 2 / sizeof(uint)];
        contents[0] = 1234;
        contents[stride / sizeof(uint)] = 5678;
        GD.UpdateBuffer(input, 0, contents);

        // Same buffer, different windows. The descriptor cache identity includes the range's
        // offset and size, so these must not collapse onto one cached set.
        PropertySet first = BuildProps(output, input, texture);
        first.SetBuffer("Input", new DeviceBufferRange(input, 0, stride));
        Assert.Equal(1234u, Run(first, output, program)[2]);

        PropertySet second = BuildProps(output, input, texture);
        second.SetBuffer("Input", new DeviceBufferRange(input, stride, stride));
        Assert.Equal(5678u, Run(second, output, program)[2]);
    }
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanCrossSetBindingTests : CrossSetBindingTests<VulkanDeviceCreator> { }
#endif
