using System;
using System.Collections.Generic;

using Xunit;

namespace Prowl.Graphite.Tests;

// End-to-end coverage of the PropertySet binding API through CommandBuffer.SetProperties. The
// value-type plumbing is covered by CPU/PropertySetTests; this suite verifies the binding
// actually reaches the GPU: transient vs. read-only vs. writable uniform buffers, structured
// buffers, ApplyOther merging, and the missing-property handler. Everything runs through the
// BasicComputeTest kernel (Destination[i] = Source[i]; Source[i] *= 2) so results are
// deterministic and easy to read back.
public abstract class PropertySetBindingTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    private const uint Side = 16;
    private const uint Count = Side * Side;

    [SkippableFact]
    public void TransientUniforms_BackScalarWrites()
    {
        Skip.IfNot(GD.Features.ComputeShader);

        float[] result = RunCompute((props, source, destination) =>
        {
            props.SetInt("Width", (int)Side);
            props.SetInt("Height", (int)Side);
            props.SetBuffer("Source", source, readOnly: false);
            props.SetBuffer("Destination", destination, readOnly: false);
        });

        AssertCopiedSource(result);
    }

    [SkippableFact]
    public void ReadOnlyUniformBuffer_FeedsContentsAndIgnoresScalarWrites()
    {
        Skip.IfNot(GD.Features.ComputeShader);

        // The buffer already carries the correct dimensions. Read-only binding must feed those
        // contents to the kernel and ignore the conflicting scalar writes, so the copy succeeds.
        DeviceBuffer ubo = RF.CreateBuffer(new BufferDescription(16, BufferUsage.UniformBuffer));
        GD.UpdateBuffer(ubo, 0, new uint[] { Side, Side, 0, 0 });

        float[] result = RunCompute((props, source, destination) =>
        {
            props.SetBuffer("Params", ubo, readOnly: true);
            props.SetInt("Width", 1);
            props.SetInt("Height", 1);
            props.SetBuffer("Source", source, readOnly: false);
            props.SetBuffer("Destination", destination, readOnly: false);
        });

        AssertCopiedSource(result);
    }

    [SkippableFact]
    public void WritableUniformBuffer_UsesProvidedBufferAsBackingStorage()
    {
        Skip.IfNot(GD.Features.ComputeShader);

        // The UBO starts zeroed; the SetInt calls must write the dimensions into this very buffer.
        DeviceBuffer ubo = RF.CreateBuffer(new BufferDescription(16, BufferUsage.UniformBuffer));
        GD.UpdateBuffer(ubo, 0, new uint[] { 0, 0, 0, 0 });

        float[] result = RunCompute((props, source, destination) =>
        {
            props.SetBuffer("Params", ubo, readOnly: false);
            props.SetInt("Width", (int)Side);
            props.SetInt("Height", (int)Side);
            props.SetBuffer("Source", source, readOnly: false);
            props.SetBuffer("Destination", destination, readOnly: false);
        });

        AssertCopiedSource(result);

        // The writes landed in the user-provided buffer, not a transient one.
        DeviceBuffer readback = GetReadback(ubo);
        MappedResourceView<uint> map = GD.Map<uint>(readback, MapMode.Read);
        Assert.Equal(Side, map[0]);
        Assert.Equal(Side, map[1]);
        GD.Unmap(readback);
    }

    [SkippableFact]
    public void ApplyOther_MergesEntriesFromBothSets()
    {
        Skip.IfNot(GD.Features.ComputeShader);

        float[] result = RunCompute((props, source, destination) =>
        {
            PropertySet other = new();
            other.SetInt("Height", (int)Side);
            other.SetBuffer("Source", source, readOnly: false);
            other.SetBuffer("Destination", destination, readOnly: false);

            props.SetInt("Width", (int)Side);
            props.ApplyOther(other);
        });

        AssertCopiedSource(result);
    }

    [SkippableFact]
    public void MissingProperty_InvokesHandler()
    {
        Skip.IfNot(GD.Features.ComputeShader);

        HashSet<PropertyID> missing = [];
        MissingPropertyHandler? previous = GD.OnMissingProperty;
        GD.OnMissingProperty = (shader, compute, name, kind, set, binding) => missing.Add(name);

        try
        {
            // Deliberately omit Destination; the backend substitutes a default and notifies.
            RunCompute((props, source, destination) =>
            {
                props.SetInt("Width", (int)Side);
                props.SetInt("Height", (int)Side);
                props.SetBuffer("Source", source, readOnly: false);
            });
        }
        finally
        {
            GD.OnMissingProperty = previous;
        }

        Assert.Contains((PropertyID)"Destination", missing);
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
            Count * sizeof(float), BufferUsage.StructuredBufferReadWrite, sizeof(float)));
        DeviceBuffer destination = RF.CreateBuffer(new BufferDescription(
            Count * sizeof(float), BufferUsage.StructuredBufferReadWrite, sizeof(float)));

        float[] initial = new float[Count];
        for (int i = 0; i < Count; i++) initial[i] = i;
        GD.UpdateBuffer(source, 0, initial);

        ComputeProgram program = CreateBasicComputeProgram();

        PropertySet props = new();
        configure(props, source, destination);

        // The frame must be open while recording: property binding allocates transient memory.
        Frame frame = GD.BeginFrame();
        CommandBuffer cl = RF.CreateCommandBuffer();
        cl.Begin();
        cl.SetComputeShader(program);
        cl.SetProperties(props);
        cl.Dispatch(Side / 16, Side / 16, 1);
        cl.End();

        frame.SubmitCommands(cl);
        GD.EndFrame(frame);
        GD.WaitForIdle();

        DeviceBuffer readback = GetReadback(destination);
        MappedResourceView<float> map = GD.Map<float>(readback, MapMode.Read);
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
#if TEST_D3D11
[Trait("Backend", "D3D11")]
[Collection("GPU Tests")]
public class D3D11PropertySetBindingTests : PropertySetBindingTests<D3D11DeviceCreator> { }
#endif
#if TEST_OPENGL
[Trait("Backend", "OpenGL")]
[Collection("GPU Tests")]
public class OpenGLPropertySetBindingTests : PropertySetBindingTests<OpenGLDeviceCreator> { }
#endif
