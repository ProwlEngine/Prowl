using System.Collections.Generic;

using Xunit;

namespace Prowl.Graphite.Tests;

// The implicit-reallocation ("orphaning") path on DeviceBuffer. Writing a buffer the GPU may
// still be reading from an earlier frame would be a data race, so the buffer transparently
// retires its native resource, allocates a fresh one, and defers freeing the old one until the
// in-flight frame completes. None of this had coverage.
//
// An orphan is observed through the profiler: one DeviceBuffer briefly backs two live native
// buffers, so the live count for its role rises by one and falls back once the retiring frame's
// ring slot is reused and the deferred-disposal queue drains.
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

    private bool ProfilingEnabled()
    {
        GD.ResetProfile();
        using DeviceBuffer probe = GD.ResourceFactory.CreateBuffer(new BufferDescription(256, BufferUsage.UniformBuffer));
        return GD.GetProfile().Live[AllocBin.DeviceBuffer].Count > 0;
    }

    // Counts live native buffers carrying a given role. The all-buffers gauge is unusable here:
    // UpdateBuffer allocates pooled staging buffers of its own, which would swamp the +1 an
    // orphan contributes. Roles isolate the buffer under test from that traffic.
    private long LiveBuffersOfRole(BufferRoleBin role) => GD.GetProfile().BufferMem[role].Count;

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

    private DeviceBuffer CreateSourceBuffer(bool transientWrites = false)
    {
        BufferDescription description = new(sizeof(uint) * 4, BufferUsage.StructuredBufferReadWrite, sizeof(uint))
        {
            TransientWrites = transientWrites
        };
        DeviceBuffer buffer = RF.CreateBuffer(ref description);
        buffer.Name = "OrphanSource";
        GD.UpdateBuffer(buffer, 0, new uint[] { OldValue, 0, 0, 0 });
        return buffer;
    }

    private DeviceBuffer CreateOutputBuffer()
        => RF.CreateBuffer(new BufferDescription(sizeof(uint) * 4, BufferUsage.StructuredBufferReadWrite, sizeof(uint)));

    // Submits a slow dispatch that reads `source`, and returns with the frame ended but still
    // executing on the GPU.
    private ulong SubmitSlowFrameReading(DeviceBuffer source, DeviceBuffer output, ComputeProgram program)
    {
        PropertySet props = new();
        props.SetInt("Iterations", (int)SpinIterations);
        props.SetBuffer("Source", source, readOnly: false);
        props.SetBuffer("Output", output, readOnly: false);

        Frame frame = GD.BeginFrame();
        CommandBuffer cl = RF.CreateCommandBuffer();
        cl.Begin();
        cl.SetComputeShader(program);
        cl.SetProperties(props);
        cl.Dispatch(1, 1, 1);
        cl.End();
        frame.SubmitCommands(cl);
        ulong id = frame.FrameId;
        GD.EndFrame(frame);
        return id;
    }

    // Runs enough empty frames to cycle the ring back onto `frameId`'s slot, which is what drains
    // the deferred-disposal queue holding the retired native buffer.
    private void CycleRing()
    {
        for (uint i = 0; i < GD.MaxFramesInFlight + 1; i++)
        {
            Frame frame = GD.BeginFrame();
            GD.EndFrame(frame);
        }
        GD.WaitForIdle();
    }

    [SkippableFact]
    public void WriteToInFlightBuffer_OrphansTheNativeResource()
    {
        Skip.IfNot(GD.Features.ComputeShader);
        Skip.IfNot(ProfilingEnabled());

        DeviceBuffer source = CreateSourceBuffer();
        DeviceBuffer output = CreateOutputBuffer();
        ComputeProgram program = CreateProbeProgram();

        ulong id = SubmitSlowFrameReading(source, output, program);
        Skip.If(GD.IsFrameComplete(id), "GPU completed the frame before the CPU could race it.");

        long before = LiveBuffersOfRole(BufferRoleBin.StructuredReadWrite);
        GD.UpdateBuffer(source, 0, new uint[] { NewValue, 0, 0, 0 });
        long after = LiveBuffersOfRole(BufferRoleBin.StructuredReadWrite);

        GD.WaitForIdle();

        // The DeviceBuffer now backs a second native buffer: the retired one the GPU is reading,
        // plus the fresh one that took the CPU's write.
        Assert.Equal(before + 1, after);
    }

    [SkippableFact]
    public void OrphanedBuffer_KeepsItsIdentityAndDescription()
    {
        Skip.IfNot(GD.Features.ComputeShader);

        DeviceBuffer source = CreateSourceBuffer();
        DeviceBuffer output = CreateOutputBuffer();
        ComputeProgram program = CreateProbeProgram();

        ulong id = SubmitSlowFrameReading(source, output, program);
        Skip.If(GD.IsFrameComplete(id), "GPU completed the frame before the CPU could race it.");

        GD.UpdateBuffer(source, 0, new uint[] { NewValue, 0, 0, 0 });
        GD.WaitForIdle();

        // Orphaning swaps the native resource underneath a stable managed identity: callers
        // holding this DeviceBuffer must not observe the swap.
        Assert.False(source.IsDisposed);
        Assert.Equal(sizeof(uint) * 4u, source.SizeInBytes);
        Assert.Equal(BufferUsage.StructuredBufferReadWrite, source.Usage);
    }

    [SkippableFact]
    public void OrphanedBuffer_GpuStillReadsTheOldContents()
    {
        Skip.IfNot(GD.Features.ComputeShader);

        DeviceBuffer source = CreateSourceBuffer();
        DeviceBuffer output = CreateOutputBuffer();
        ComputeProgram program = CreateProbeProgram();

        ulong id = SubmitSlowFrameReading(source, output, program);
        Skip.If(GD.IsFrameComplete(id), "GPU completed the frame before the CPU could race it.");

        GD.UpdateBuffer(source, 0, new uint[] { NewValue, 0, 0, 0 });
        GD.WaitForIdle();

        // The in-flight dispatch reads the retired resource, so it must observe the value the
        // buffer held at submit time, never the CPU's mid-flight write.
        Assert.Equal(OldValue, ReadUInt(output, 0));
    }

    [SkippableFact]
    public void OrphanedBuffer_LaterFramesSeeTheNewContents()
    {
        Skip.IfNot(GD.Features.ComputeShader);

        DeviceBuffer source = CreateSourceBuffer();
        DeviceBuffer output = CreateOutputBuffer();
        ComputeProgram program = CreateProbeProgram();

        ulong id = SubmitSlowFrameReading(source, output, program);
        Skip.If(GD.IsFrameComplete(id), "GPU completed the frame before the CPU could race it.");

        GD.UpdateBuffer(source, 0, new uint[] { NewValue, 0, 0, 0 });
        GD.WaitForIdle();

        // The write is not lost: a frame submitted after the orphan binds the fresh resource.
        DeviceBuffer secondOutput = CreateOutputBuffer();
        SubmitSlowFrameReading(source, secondOutput, program);
        GD.WaitForIdle();

        Assert.Equal(NewValue, ReadUInt(secondOutput, 0));
    }

    [SkippableFact]
    public void OrphanedBuffer_RetiredResourceIsFreedOnceTheRingCycles()
    {
        Skip.IfNot(GD.Features.ComputeShader);
        Skip.IfNot(ProfilingEnabled());

        DeviceBuffer source = CreateSourceBuffer();
        DeviceBuffer output = CreateOutputBuffer();
        ComputeProgram program = CreateProbeProgram();

        long baseline = LiveBuffersOfRole(BufferRoleBin.StructuredReadWrite);

        ulong id = SubmitSlowFrameReading(source, output, program);
        Skip.If(GD.IsFrameComplete(id), "GPU completed the frame before the CPU could race it.");

        GD.UpdateBuffer(source, 0, new uint[] { NewValue, 0, 0, 0 });
        Assert.Equal(baseline + 1, LiveBuffersOfRole(BufferRoleBin.StructuredReadWrite));

        GD.WaitForIdle();
        CycleRing();

        // BeginFrame drains the deferred-disposal queue when it reuses the retiring frame's slot,
        // so the retired native buffer must be gone rather than leaked for the device's lifetime.
        Assert.Equal(baseline, LiveBuffersOfRole(BufferRoleBin.StructuredReadWrite));
    }

    [SkippableFact]
    public void WriteToBufferInAnOpenFrame_DoesNotOrphan()
    {
        Skip.IfNot(GD.Features.ComputeShader);
        Skip.IfNot(ProfilingEnabled());

        DeviceBuffer source = CreateSourceBuffer();
        DeviceBuffer output = CreateOutputBuffer();
        ComputeProgram program = CreateProbeProgram();

        PropertySet props = new();
        props.SetInt("Iterations", 1);
        props.SetBuffer("Source", source, readOnly: false);
        props.SetBuffer("Output", output, readOnly: false);

        Frame frame = GD.BeginFrame();
        CommandBuffer cl = RF.CreateCommandBuffer();
        cl.Begin();
        cl.SetComputeShader(program);
        cl.SetProperties(props);
        cl.Dispatch(1, 1, 1);
        cl.End();

        // Recording marked the buffer in flight, but the frame has not been submitted, so nothing
        // is actually reading it yet and a write must not force a reallocation.
        long before = LiveBuffersOfRole(BufferRoleBin.StructuredReadWrite);
        GD.UpdateBuffer(source, 0, new uint[] { NewValue, 0, 0, 0 });
        long after = LiveBuffersOfRole(BufferRoleBin.StructuredReadWrite);

        frame.SubmitCommands(cl);
        GD.EndFrame(frame);
        GD.WaitForIdle();

        Assert.Equal(before, after);
    }

    [SkippableFact]
    public void WriteToBufferFromACompletedFrame_DoesNotOrphan()
    {
        Skip.IfNot(GD.Features.ComputeShader);
        Skip.IfNot(ProfilingEnabled());

        DeviceBuffer source = CreateSourceBuffer();
        DeviceBuffer output = CreateOutputBuffer();
        ComputeProgram program = CreateProbeProgram();

        SubmitSlowFrameReading(source, output, program);
        GD.WaitForIdle();

        // The GPU is done with the buffer, so a write is safe in place.
        long before = LiveBuffersOfRole(BufferRoleBin.StructuredReadWrite);
        GD.UpdateBuffer(source, 0, new uint[] { NewValue, 0, 0, 0 });
        long after = LiveBuffersOfRole(BufferRoleBin.StructuredReadWrite);

        Assert.Equal(before, after);
    }

    [SkippableFact]
    public void WriteToNeverBoundBuffer_DoesNotOrphan()
    {
        Skip.IfNot(ProfilingEnabled());

        DeviceBuffer buffer = CreateSourceBuffer();

        // Never bound, so it was never marked in flight and cannot be racing anything.
        long before = LiveBuffersOfRole(BufferRoleBin.StructuredReadWrite);
        for (int i = 0; i < 5; i++)
            GD.UpdateBuffer(buffer, 0, new uint[] { NewValue, 0, 0, 0 });
        long after = LiveBuffersOfRole(BufferRoleBin.StructuredReadWrite);

        Assert.Equal(before, after);
    }

    [SkippableFact]
    public void TransientWritesBuffer_OptsOutOfOrphaning()
    {
        Skip.IfNot(GD.Features.ComputeShader);
        Skip.IfNot(ProfilingEnabled());

        DeviceBuffer source = CreateSourceBuffer(transientWrites: true);
        DeviceBuffer output = CreateOutputBuffer();
        ComputeProgram program = CreateProbeProgram();

        ulong id = SubmitSlowFrameReading(source, output, program);
        Skip.If(GD.IsFrameComplete(id), "GPU completed the frame before the CPU could race it.");

        // TransientWrites is the caller asserting they handle the hazard themselves, so the
        // buffer is never marked in flight and the write goes straight through.
        long before = LiveBuffersOfRole(BufferRoleBin.StructuredReadWrite);
        GD.UpdateBuffer(source, 0, new uint[] { NewValue, 0, 0, 0 });
        long after = LiveBuffersOfRole(BufferRoleBin.StructuredReadWrite);

        GD.WaitForIdle();

        Assert.Equal(before, after);
    }

    [SkippableFact]
    public void MapWrite_OnInFlightBuffer_AlsoOrphans()
    {
        Skip.IfNot(GD.Features.ComputeShader);
        Skip.IfNot(ProfilingEnabled());

        // Map(Write) is the other entry point into EnsureWritable and must be guarded exactly
        // like UpdateBuffer. Mapping needs Dynamic, which cannot be combined with a read-write
        // structured usage, so the raced buffer here is the uniform block instead of Source.
        BufferDescription description = new(16, BufferUsage.UniformBuffer | BufferUsage.Dynamic);
        DeviceBuffer paramsBuffer = RF.CreateBuffer(ref description);
        GD.UpdateBuffer(paramsBuffer, 0, new uint[] { SpinIterations, 0, 0, 0 });

        DeviceBuffer source = CreateSourceBuffer();
        DeviceBuffer output = CreateOutputBuffer();
        ComputeProgram program = CreateProbeProgram();

        // Bound read-only so the buffer's own contents drive Iterations rather than a transient.
        PropertySet props = new();
        props.SetBuffer("Params", paramsBuffer, readOnly: true);
        props.SetBuffer("Source", source, readOnly: false);
        props.SetBuffer("Output", output, readOnly: false);

        Frame frame = GD.BeginFrame();
        CommandBuffer cl = RF.CreateCommandBuffer();
        cl.Begin();
        cl.SetComputeShader(program);
        cl.SetProperties(props);
        cl.Dispatch(1, 1, 1);
        cl.End();
        frame.SubmitCommands(cl);
        ulong id = frame.FrameId;
        GD.EndFrame(frame);

        Skip.If(GD.IsFrameComplete(id), "GPU completed the frame before the CPU could race it.");

        long before = LiveBuffersOfRole(BufferRoleBin.Uniform);
        MappedResource mapped = GD.Map(paramsBuffer, MapMode.Write);
        unsafe { *(uint*)mapped.Data = 1; }
        GD.Unmap(paramsBuffer);
        long after = LiveBuffersOfRole(BufferRoleBin.Uniform);

        GD.WaitForIdle();

        Assert.Equal(before + 1, after);
    }

    [SkippableFact]
    public void RepeatedOrphansWithinTheWarningWindow_Warn()
    {
        Skip.IfNot(GD.Features.ComputeShader);

        DeviceBuffer source = CreateSourceBuffer();
        DeviceBuffer output = CreateOutputBuffer();
        ComputeProgram program = CreateProbeProgram();

        List<string> warnings = [];
        GraphicsDeviceWarningHandler previous = GD.OnWarning;
        GD.OnWarning = message => warnings.Add(message);

        try
        {
            // Rewriting an in-flight buffer every frame is the pathological pattern StreamingBuffer
            // exists to replace, so the second and later orphans inside the window must warn.
            for (int i = 0; i < 3; i++)
            {
                ulong id = SubmitSlowFrameReading(source, output, program);
                Skip.If(GD.IsFrameComplete(id), "GPU completed the frame before the CPU could race it.");
                GD.UpdateBuffer(source, 0, new uint[] { NewValue, 0, 0, 0 });
                GD.WaitForIdle();
            }
        }
        finally
        {
            GD.OnWarning = previous;
        }

        Assert.NotEmpty(warnings);
        Assert.Contains("StreamingBuffer", warnings[0]);
    }

    [SkippableFact]
    public void FirstOrphanOfABuffer_DoesNotWarn()
    {
        Skip.IfNot(GD.Features.ComputeShader);

        DeviceBuffer source = CreateSourceBuffer();
        DeviceBuffer output = CreateOutputBuffer();
        ComputeProgram program = CreateProbeProgram();

        List<string> warnings = [];
        GraphicsDeviceWarningHandler previous = GD.OnWarning;
        GD.OnWarning = message => warnings.Add(message);

        try
        {
            ulong id = SubmitSlowFrameReading(source, output, program);
            Skip.If(GD.IsFrameComplete(id), "GPU completed the frame before the CPU could race it.");
            GD.UpdateBuffer(source, 0, new uint[] { NewValue, 0, 0, 0 });
            GD.WaitForIdle();
        }
        finally
        {
            GD.OnWarning = previous;
        }

        // A one-off reallocation is legitimate and must stay quiet; only a repeating pattern is
        // worth complaining about.
        Assert.Empty(warnings);
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
