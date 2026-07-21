using Xunit;

namespace Prowl.Graphite.Tests;

// Resource binding spread across multiple descriptor sets. MultiParameterBlockBindingTests covers
// exactly one shape -- two sets, one uniform block each -- which leaves the interesting cases
// untested: a structured buffer that does not live in set 0, a texture and sampler pair in a
// third set, and the descriptor-set cache having to tell near-identical bindings apart.
//
// The layouts below are hand-written to match CrossSetBinding.slang, mirroring the other binding
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
        ShaderStageDescription stage = TestShaderLoader.LoadCompute(GD.BackendType, "CrossSetBinding.slang");
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
            OutputCount * sizeof(uint), BufferUsage.StructuredBufferReadWrite, sizeof(uint)));

    private DeviceBuffer CreateInput(uint value)
    {
        DeviceBuffer buffer = RF.CreateBuffer(new BufferDescription(
            4 * sizeof(uint), BufferUsage.StructuredBufferReadOnly, sizeof(uint)));
        GD.UpdateBuffer(buffer, 0, new uint[] { value, 0, 0, 0 });
        return buffer;
    }

    // A 1x1 texture whose red channel is `red`, so a sample at any UV returns it.
    private Texture CreateSolidTexture(byte red)
    {
        Texture texture = RF.CreateTexture(TextureDescription.Texture2D(
            1, 1, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
        GD.UpdateTexture(texture, new byte[] { red, 0, 0, 255 }, 0, 0, 0, 1, 1, 1, 0, 0);
        return texture;
    }

    private uint[] Run(PropertySet props, DeviceBuffer output, ComputeProgram program)
    {
        GD.RunTestGraph(context =>
        {
            CommandBuffer cl = context.GetCommandBuffer();
            cl.SetComputeShader(program);
            cl.SetProperties(props);
            cl.Dispatch(1, 1, 1);
            context.SubmitCommandBuffer(cl);
        });
        GD.WaitForIdle();

        return Read(output);
    }

    private uint[] Read(DeviceBuffer output)
    {
        DeviceBuffer readback = GetReadback(output);
        MappedResourceView<uint> map = GD.Map<uint>(readback, MapMode.Read);
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
        props.SetBuffer("Output", output, readOnly: false);
        props.SetBuffer("Input", input, readOnly: true);
        props.SetTexture("Tex", texture, GD.LinearSampler);
        return props;
    }

    [SkippableFact]
    public void ResourcesInEverySet_ResolveIndependently()
    {
        Skip.IfNot(GD.Features.ComputeShader);

        DeviceBuffer output = CreateOutput();
        DeviceBuffer input = CreateInput(555);
        Texture texture = CreateSolidTexture(128);
        ComputeProgram program = CreateProgram();

        uint[] result = Run(BuildProps(output, input, texture), output, program);

        // Each set keeps its own uniform: aliasing would make these collapse onto one value.
        Assert.Equal(101u, result[0]);
        Assert.Equal(202u, result[1]);
        Assert.Equal(303u, result[3]);

        // A read-only structured buffer bound to set 1 rather than set 0.
        Assert.Equal(555u, result[2]);

        // A texture and sampler pair bound to set 2.
        Assert.Equal(128u, result[4]);
    }

    [SkippableFact]
    public void StructuredBufferOutsideSetZero_ReadsItsOwnContents()
    {
        Skip.IfNot(GD.Features.ComputeShader);

        DeviceBuffer output = CreateOutput();
        DeviceBuffer input = CreateInput(4242);
        Texture texture = CreateSolidTexture(0);
        ComputeProgram program = CreateProgram();

        uint[] result = Run(BuildProps(output, input, texture), output, program);

        Assert.Equal(4242u, result[2]);
    }

    [SkippableFact]
    public void SwappingOneResourceInOneSet_LeavesTheOtherSetsIntact()
    {
        Skip.IfNot(GD.Features.ComputeShader);

        DeviceBuffer output = CreateOutput();
        DeviceBuffer firstInput = CreateInput(111);
        DeviceBuffer secondInput = CreateInput(999);
        Texture texture = CreateSolidTexture(64);
        ComputeProgram program = CreateProgram();

        uint[] first = Run(BuildProps(output, firstInput, texture), output, program);
        Assert.Equal(111u, first[2]);

        // Only set 1's structured buffer changes. The descriptor cache must key on it and leave
        // the other sets alone rather than reuse a stale set or rebuild everything wrong.
        uint[] second = Run(BuildProps(output, secondInput, texture), output, program);

        Assert.Equal(999u, second[2]);
        Assert.Equal(101u, second[0]);
        Assert.Equal(303u, second[3]);
        Assert.Equal(64u, second[4]);
    }

    [SkippableFact]
    public void IdenticalBindings_ReusedAcrossFrames_StayCorrect()
    {
        Skip.IfNot(GD.Features.ComputeShader);

        DeviceBuffer output = CreateOutput();
        DeviceBuffer input = CreateInput(777);
        Texture texture = CreateSolidTexture(200);
        ComputeProgram program = CreateProgram();

        // Repeated identical bindings hit the descriptor-set cache. A cached set is reused as-is
        // and never rewritten, so a bad identity would surface as a wrong result here.
        for (int i = 0; i < GD.MaxExecutingTasks * 3; i++)
        {
            uint[] result = Run(BuildProps(output, input, texture), output, program);
            Assert.Equal(777u, result[2]);
            Assert.Equal(200u, result[4]);
            Assert.Equal(101u, result[0]);
        }
    }

    [SkippableFact]
    public void AlternatingBindings_DoNotAliasInTheDescriptorCache()
    {
        Skip.IfNot(GD.Features.ComputeShader);

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
    }

    [SkippableFact]
    public void TwoDispatchesInOneCommandBuffer_SeeTheirOwnProperties()
    {
        Skip.IfNot(GD.Features.ComputeShader);

        DeviceBuffer firstOutput = CreateOutput();
        DeviceBuffer secondOutput = CreateOutput();
        DeviceBuffer input = CreateInput(1);
        Texture texture = CreateSolidTexture(1);
        ComputeProgram program = CreateProgram();

        // Rebinding between dispatches in a single recording must take effect for the second
        // dispatch rather than both observing whichever set was bound last.
        GD.RunTestGraph(context =>
        {
            CommandBuffer cl = context.GetCommandBuffer();
            cl.SetComputeShader(program);
            cl.SetProperties(BuildProps(firstOutput, input, texture, valueA: 7));
            cl.Dispatch(1, 1, 1);
            cl.SetProperties(BuildProps(secondOutput, input, texture, valueA: 8));
            cl.Dispatch(1, 1, 1);
            context.SubmitCommandBuffer(cl);
        });
        GD.WaitForIdle();

        Assert.Equal(7u, Read(firstOutput)[0]);
        Assert.Equal(8u, Read(secondOutput)[0]);
    }

    [SkippableFact]
    public void SubRangesOfOneBuffer_BindAsDistinctResources()
    {
        Skip.IfNot(GD.Features.ComputeShader);

        DeviceBuffer output = CreateOutput();
        Texture texture = CreateSolidTexture(0);
        ComputeProgram program = CreateProgram();

        uint stride = GD.StructuredBufferMinOffsetAlignment;
        DeviceBuffer input = RF.CreateBuffer(new BufferDescription(
            stride * 2, BufferUsage.StructuredBufferReadOnly, sizeof(uint)));

        uint[] contents = new uint[stride * 2 / sizeof(uint)];
        contents[0] = 1234;
        contents[stride / sizeof(uint)] = 5678;
        GD.UpdateBuffer(input, 0, contents);

        // Same buffer, different windows. The descriptor cache identity includes the range's
        // offset and size, so these must not collapse onto one cached set.
        PropertySet first = BuildProps(output, input, texture);
        first.SetBuffer("Input", new DeviceBufferRange(input, 0, stride), readOnly: true);
        Assert.Equal(1234u, Run(first, output, program)[2]);

        PropertySet second = BuildProps(output, input, texture);
        second.SetBuffer("Input", new DeviceBufferRange(input, stride, stride), readOnly: true);
        Assert.Equal(5678u, Run(second, output, program)[2]);
    }

    [SkippableFact]
    public void ClearProperties_DropsPreviouslyBoundValues()
    {
        Skip.IfNot(GD.Features.ComputeShader);

        DeviceBuffer output = CreateOutput();
        DeviceBuffer input = CreateInput(1);
        Texture texture = CreateSolidTexture(1);
        ComputeProgram program = CreateProgram();

        GD.UpdateBuffer(output, 0, new uint[OutputCount]);

        PropertySet props = BuildProps(output, input, texture, valueA: 4321);

        GD.RunTestGraph(context =>
        {
            CommandBuffer cl = context.GetCommandBuffer();
            cl.SetComputeShader(program);
            cl.SetProperties(props);
            cl.ClearProperties();
            cl.SetProperties(BuildProps(output, input, texture, valueA: 1));
            cl.Dispatch(1, 1, 1);
            context.SubmitCommandBuffer(cl);
        });
        GD.WaitForIdle();

        // The cleared set must not leak back into the dispatch.
        Assert.Equal(1u, Read(output)[0]);
    }

    [SkippableFact]
    public void MissingProperty_ReportsItsSetAndBinding()
    {
        Skip.IfNot(GD.Features.ComputeShader);

        DeviceBuffer output = CreateOutput();
        DeviceBuffer input = CreateInput(1);
        Texture texture = CreateSolidTexture(1);
        ComputeProgram program = CreateProgram();

        uint reportedSet = uint.MaxValue;
        MissingPropertyHandler previous = GD.OnMissingProperty;
        GD.OnMissingProperty = (shader, compute, name, kind, set, binding) =>
        {
            if (name == (PropertyID)"Input") reportedSet = set;
        };

        try
        {
            // Input lives in set 1, so the handler must say so rather than defaulting to set 0.
            PropertySet props = new();
            props.SetInt("valueA", 1);
            props.SetInt("valueB", 2);
            props.SetInt("valueC", 3);
            props.SetBuffer("Output", output, readOnly: false);
            props.SetTexture("Tex", texture, GD.LinearSampler);
            Run(props, output, program);
        }
        finally
        {
            GD.OnMissingProperty = previous;
        }

        Assert.Equal(1u, reportedSet);
    }

    [SkippableFact]
    public void MissingTexture_SubstitutesADefaultRatherThanCrashing()
    {
        Skip.IfNot(GD.Features.ComputeShader);

        DeviceBuffer output = CreateOutput();
        DeviceBuffer input = CreateInput(321);
        ComputeProgram program = CreateProgram();

        // No texture bound at all: the backend substitutes its null texture so the dispatch still
        // runs and the rest of the sets still resolve.
        PropertySet props = new();
        props.SetInt("valueA", 11);
        props.SetInt("valueB", 22);
        props.SetInt("valueC", 33);
        props.SetBuffer("Output", output, readOnly: false);
        props.SetBuffer("Input", input, readOnly: true);

        uint[] result = Run(props, output, program);

        Assert.Equal(11u, result[0]);
        Assert.Equal(321u, result[2]);
    }
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanCrossSetBindingTests : CrossSetBindingTests<VulkanDeviceCreator> { }
#endif
