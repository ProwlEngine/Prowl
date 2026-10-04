using Xunit;

namespace Prowl.Graphite.Tests;

// CPU writes to a buffer the GPU may still be reading. UpdateBuffer stages the data and submits a
// queue-ordered copy behind a full barrier, so work already submitted keeps seeing the old
// contents and work submitted afterwards sees the new ones. There is no in-flight tracking.
//
// Getting a buffer genuinely in flight requires the GPU to still be busy when the CPU writes, so
// these tests submit a deliberately slow dispatch and then verify the frame really is incomplete
// before racing it. If the GPU wins anyway the test skips rather than reporting a false failure.
public abstract class BufferSafetyTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    // Tuned so the dispatch takes tens of milliseconds: long enough for the CPU to win the race,
    // far short of any driver timeout.
    private const uint SpinIterations = 20_000_000;

    private const uint OldValue = 0x11111111;
    private const uint NewValue = 0x22222222;

    private ComputeProgram CreateProbeProgram()
    {
        ShaderStageDescription stage = TestShaderLoader.LoadCompute(GD.BackendType, "OrphanProbe.slang");
        ResourceLayoutDescription[] layouts =
        [
            new ResourceLayoutDescription
            {
                Set = 0,
                Elements =
                [
                    new ResourceLayoutElementDescription("Params", ResourceKind.UniformBuffer, ShaderStages.Compute, 0)
                    {
                        UniformFields = [new UniformBlockField("Iterations", 0, sizeof(uint), UniformScalarType.Int1)]
                    },
                    new ResourceLayoutElementDescription("Source", ResourceKind.StructuredBufferReadWrite, ShaderStages.Compute, 1),
                    new ResourceLayoutElementDescription("Output", ResourceKind.StructuredBufferReadWrite, ShaderStages.Compute, 2),
                ]
            }
        ];
        return RF.CreateComputeProgram(new ComputeDescription(stage, layouts, 1, 1, 1));
    }

    private DeviceBuffer CreateSourceBuffer()
    {
        DeviceBuffer buffer = RF.CreateBuffer(new BufferDescription(sizeof(uint) * 4, BufferUsage.StructuredBufferReadWrite, sizeof(uint)));
        buffer.Name = "WriteSource";
        GD.UpdateBuffer(buffer, 0, new uint[] { OldValue, 0, 0, 0 });
        return buffer;
    }

    private DeviceBuffer CreateOutputBuffer()
        => RF.CreateBuffer(new BufferDescription(sizeof(uint) * 4, BufferUsage.StructuredBufferReadWrite, sizeof(uint)));

    // Submits a slow dispatch that reads `source`, and returns with the frame ended but still
    // executing on the GPU.
    private ExecutionTask SubmitSlowExecutionReading(DeviceBuffer source, DeviceBuffer output, ComputeProgram program)
    {
        PropertySet props = new();
        props.SetInt("Iterations", (int)SpinIterations);
        props.SetBuffer("Source", source, readOnly: false);
        props.SetBuffer("Output", output, readOnly: false);

        return GD.RunTestGraph(context =>
        {
            CommandBuffer cl = context.GetCommandBuffer();
            cl.SetComputeShader(program);
            cl.SetProperties(props);
            cl.Dispatch(1, 1, 1);
            context.SubmitCommandBuffer(cl);
        });
    }

    [SkippableFact]
    public void WriteWhileInFlight_GpuStillReadsTheOldContents()
    {

        DeviceBuffer source = CreateSourceBuffer();
        DeviceBuffer output = CreateOutputBuffer();
        ComputeProgram program = CreateProbeProgram();

        ExecutionTask id = SubmitSlowExecutionReading(source, output, program);
        Skip.If(GD.IsExecutionComplete(id), "GPU completed the frame before the CPU could race it.");

        GD.UpdateBuffer(source, 0, new uint[] { NewValue, 0, 0, 0 });
        GD.WaitForIdle();

        // The copy is queued behind the in-flight dispatch, so the dispatch must observe the value
        // the buffer held when it was submitted.
        Assert.Equal(OldValue, ReadUInt(output, 0));
    }

    [SkippableFact]
    public void WriteWhileInFlight_LaterFramesSeeTheNewContents()
    {

        DeviceBuffer source = CreateSourceBuffer();
        DeviceBuffer output = CreateOutputBuffer();
        ComputeProgram program = CreateProbeProgram();

        ExecutionTask id = SubmitSlowExecutionReading(source, output, program);
        Skip.If(GD.IsExecutionComplete(id), "GPU completed the frame before the CPU could race it.");

        GD.UpdateBuffer(source, 0, new uint[] { NewValue, 0, 0, 0 });
        GD.WaitForIdle();

        // The write is not lost: a frame submitted after the copy sees the new contents.
        DeviceBuffer secondOutput = CreateOutputBuffer();
        SubmitSlowExecutionReading(source, secondOutput, program);
        GD.WaitForIdle();

        Assert.Equal(NewValue, ReadUInt(secondOutput, 0));
    }

    [SkippableFact]
    public void WriteWhileInFlight_BumpsContentVersion()
    {
        DeviceBuffer source = CreateSourceBuffer();
        DeviceBuffer output = CreateOutputBuffer();
        ComputeProgram program = CreateProbeProgram();

        ExecutionTask id = SubmitSlowExecutionReading(source, output, program);
        Skip.If(GD.IsExecutionComplete(id), "GPU completed the frame before the CPU could race it.");

        uint versionBefore = source.ContentVersion;
        GD.UpdateBuffer(source, 0, new uint[] { NewValue, 0, 0, 0 });

        Assert.NotEqual(versionBefore, source.ContentVersion);
        GD.WaitForIdle();
    }

    private uint ReadUInt(DeviceBuffer buffer, int index)
    {
        DeviceBuffer readback = GetReadback(buffer);
        MappedResourceView<uint> map = GD.Map<uint>(readback, MapMode.Read);
        uint value = map[index];
        GD.Unmap(readback);
        return value;
    }
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanBufferSafetyTests : BufferSafetyTests<VulkanDeviceCreator> { }
#endif
