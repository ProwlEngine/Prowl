#nullable enable

using System;
using System.Collections.Generic;

using Prowl.Graphite.RenderGraph;
using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Tests;

// Coverage for the IProfiler event wiring added this session: pipeline switches, draws/dispatches,
// resource-set binds, barriers, pass begin/end + resource reads, command buffer submission counts,
// and GPU execution timing. Each test builds its own isolated device with a RecordingProfiler
// attached, since IProfiler is set once at device construction.

file sealed class RecordingProfiler : IProfiler
{
    public readonly List<PipelineBindInfo> PipelineSwitches = new();
    public readonly List<DrawCallInfo> Draws = new();
    public readonly List<DispatchCallInfo> Dispatches = new();
    public readonly List<uint> ResourceSetBinds = new();
    public readonly List<(BarrierBin Kind, uint Count)> Barriers = new();
    public readonly List<ProfilerSubmitInfo> Submits = new();
    public readonly List<PassInfo> PassesBegun = new();
    public readonly List<PassInfo> PassesEnded = new();
    public readonly List<(PassInfo Pass, RenderResourceID Resource, DeviceResource Resolved)> PassReads = new();
    public readonly List<(PassInfo? Pass, string BufferName, bool IsTransfer, double Milliseconds)> ExecutionTimes = new();

    public bool RequestExecutionTiming { get; set; }
    public bool RequestCapture => false;

    public void Allocate(AllocBin type, long bytes) { }
    public void Free(AllocBin type, long bytes) { }
    public void AllocateMemory(BufferRoleBin role, long bytes) { }
    public void FreeMemory(BufferRoleBin role, long bytes) { }
    public void Record(BufferOpBin op, long bytes) { }
    public void RecordSwap(SwapBin evt, long bytes) { }

    public void BeginPass(in PassInfo pass) => PassesBegun.Add(pass);
    public void EndPass(in PassInfo pass) => PassesEnded.Add(pass);
    public void RecordPassRead(in PassInfo pass, RenderResourceID resource, DeviceResource resolved)
        => PassReads.Add((pass, resource, resolved));

    public void BeginSample(string name) { }
    public void EndSample() { }

    public void Capture(in PassInfo pass, IReadOnlyList<Framebuffer> passOutputs, TransferCommandBuffer transfer) { }

    public void RecordDraw(in DrawCallInfo info) => Draws.Add(info);
    public void RecordDispatch(in DispatchCallInfo info) => Dispatches.Add(info);
    public void RecordPipelineSwitch(in PipelineBindInfo info) => PipelineSwitches.Add(info);
    public void RecordResourceSetBind(uint setCount) => ResourceSetBinds.Add(setCount);
    public void RecordBarrier(BarrierBin kind, uint count) => Barriers.Add((kind, count));
    public void RecordSubmit(in ProfilerSubmitInfo info) => Submits.Add(info);

    public void RecordExecutionTime(PassInfo? pass, string bufferName, bool isTransfer, double milliseconds)
        => ExecutionTimes.Add((pass, bufferName, isTransfer, milliseconds));
}

file readonly struct ProfilerView : IRenderView
{
    public ProfilerView(uint width, uint height)
    {
        PixelWidth = width;
        PixelHeight = height;
    }

    public uint PixelWidth { get; }
    public uint PixelHeight { get; }
}

file sealed class ClearingRasterPass : RasterPass<ProfilerView>
{
    private readonly RenderResourceID _id;

    public ClearingRasterPass(RenderResourceID id) => _id = id;

    public override string Name => "ProfilerClear";

    public override void Setup(RenderContextBuilder builder)
        => SetTarget(builder, _id, GraphTextureDesc.ViewSized(false, 1f, PixelFormat.R32_G32_B32_A32_Float));

    public override void Render(RenderContext<ProfilerView> context)
    {
        CommandBuffer cmd = context.GetCommandBuffer(Name);
        BindTarget(context, cmd, new Color(0, 0, 0, 1));
        context.SubmitCommandBuffer(cmd);
    }
}

file sealed class ReadingCopyPass : IPass<ProfilerView>
{
    private readonly RenderResourceID _id;
    private readonly Texture _readback;
    private TextureHandle _handle;

    public ReadingCopyPass(RenderResourceID id, Texture readback)
    {
        _id = id;
        _readback = readback;
    }

    public string Name => "ProfilerCopy";

    public void Setup(RenderContextBuilder builder) => _handle = builder.GetInputTexture(_id);

    public void Render(RenderContext<ProfilerView> context)
    {
        RenderTexture target = context.GetRenderTexture(_handle);
        CommandBuffer cmd = context.GetCommandBuffer(Name);
        cmd.CopyTexture(target.ColorTextures[0], _readback);
        context.SubmitCommandBuffer(cmd);
    }
}

file sealed class NoOpProfilerPresentPass : IPresentPass<ProfilerView>
{
    public string Name => "Present";
    public void Setup(PresentContextBuilder builder) { }
    public void Present(RenderContext<ProfilerView> context) { }
}

file sealed class ProfilerTestPipeline : RenderPipeline<ProfilerView>
{
    private readonly IPass<ProfilerView>[] _passes;

    public ProfilerTestPipeline(params IPass<ProfilerView>[] passes) => _passes = passes;

    protected override void InitializePasses()
    {
        foreach (IPass<ProfilerView> pass in _passes)
            AddPass(pass);
        SetPresentPass(new NoOpProfilerPresentPass());
    }
}

public abstract class ProfilerEventsTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    private GraphicsDevice CreateProfiledDevice(IProfiler profiler) => GD.BackendType switch
    {
        GraphicsBackend.Vulkan => GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true) { Profiler = profiler }),
        _ => throw new NotSupportedException(),
    };

    [Fact]
    public void Dispatch_RecordsPipelineSwitchResourceSetBindAndDispatch()
    {
        RecordingProfiler profiler = new();
        using GraphicsDevice device = CreateProfiledDevice(profiler);

        const uint width = 16;
        const uint height = 16;
        const uint count = width * height;

        DeviceBuffer source = device.ResourceFactory.CreateBuffer(new BufferDescription(
            count * sizeof(float), BufferUsage.StructuredBufferReadWrite, sizeof(float)));
        DeviceBuffer destination = device.ResourceFactory.CreateBuffer(new BufferDescription(
            count * sizeof(float), BufferUsage.StructuredBufferReadWrite, sizeof(float)));

        ShaderStageDescription stage = TestShaderLoader.LoadCompute(device.BackendType, "BasicComputeTest.slang");
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
        ComputeProgram program = device.ResourceFactory.CreateComputeProgram(new ComputeDescription(stage, layouts, 16, 16, 1));

        PropertySet props = new();
        props.SetInt("Width", (int)width);
        props.SetInt("Height", (int)height);
        props.SetBuffer("Source", source, readOnly: false);
        props.SetBuffer("Destination", destination, readOnly: false);

        device.RunTestGraph(context =>
        {
            CommandBuffer cl = context.GetCommandBuffer();
            cl.SetComputeShader(program);
            cl.SetProperties(props);
            cl.Dispatch(1, 1, 1);
            context.SubmitCommandBuffer(cl);
        });
        device.WaitForIdle();

        PipelineBindInfo bind = Assert.Single(profiler.PipelineSwitches);
        Assert.True(bind.IsCompute);
        Assert.Equal(ShaderStages.Compute, bind.Stages);

        Assert.NotEmpty(profiler.ResourceSetBinds);
        Assert.All(profiler.ResourceSetBinds, count => Assert.Equal(1u, count));

        DispatchCallInfo dispatch = Assert.Single(profiler.Dispatches);
        Assert.Equal(1u, dispatch.GroupCountX);
        Assert.Equal(1u, dispatch.GroupCountY);
        Assert.Equal(1u, dispatch.GroupCountZ);
        Assert.False(dispatch.IsIndirect);
    }

    [Fact]
    public void CopyBuffer_RecordsBufferTransitionBarrierAndBufferOp()
    {
        RecordingProfiler profiler = new();
        using GraphicsDevice device = CreateProfiledDevice(profiler);

        DeviceBuffer source = device.ResourceFactory.CreateBuffer(new BufferDescription(256, BufferUsage.StructuredBufferReadWrite, sizeof(uint)));
        DeviceBuffer destination = device.ResourceFactory.CreateBuffer(new BufferDescription(256, BufferUsage.StructuredBufferReadWrite, sizeof(uint)));

        device.RunTestGraph(context =>
        {
            CommandBuffer cl = context.GetCommandBuffer();
            cl.CopyBuffer(source, 0, destination, 0, 256);
            context.SubmitCommandBuffer(cl);
        });
        device.WaitForIdle();

        Assert.Contains(profiler.Barriers, b => b.Kind == BarrierBin.BufferTransition);
    }

    [Fact]
    public void DispatchGraph_RecordsPassLifecycleReadsAndSubmits()
    {
        RecordingProfiler profiler = new();
        using GraphicsDevice device = CreateProfiledDevice(profiler);

        const uint size = 64;
        Texture readback = device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
            size, size, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Staging));

        RenderResourceID id = RenderResourceID.Intern("profiler_pass_target");
        ClearingRasterPass clearPass = new(id);
        ReadingCopyPass copyPass = new(id, readback);
        using ProfilerTestPipeline pipeline = new(clearPass, copyPass);

        device.DispatchGraph(pipeline, new ProfilerView[] { new(size, size) });
        device.WaitForIdle();

        Assert.Equal(2, profiler.PassesBegun.Count);
        Assert.Equal(2, profiler.PassesEnded.Count);
        Assert.Equal(new[] { "ProfilerClear", "ProfilerCopy" }, profiler.PassesBegun.ConvertAll(p => p.Name));

        // ClearingRasterPass declares the target as an output; ReadingCopyPass declares it as an input.
        Assert.Contains(profiler.PassReads, r => r.Pass.Name == "ProfilerClear" && r.Resource.Equals(id));
        Assert.Contains(profiler.PassReads, r => r.Pass.Name == "ProfilerCopy" && r.Resource.Equals(id));

        // Both passes submit exactly one graphics command buffer each.
        Assert.Equal(2, profiler.Submits.Count);
        Assert.All(profiler.Submits, s => Assert.Equal(SubmitKind.Graphics, s.Kind));

        Assert.Contains(profiler.Barriers, b => b.Kind == BarrierBin.TextureTransition);
        Assert.Contains(profiler.Barriers, b => b.Kind == BarrierBin.MemoryBarrier);
    }

    [Fact]
    public void SubmitTransfer_RecordsTransferSubmit()
    {
        RecordingProfiler profiler = new();
        using GraphicsDevice device = CreateProfiledDevice(profiler);

        DeviceBuffer source = device.ResourceFactory.CreateBuffer(new BufferDescription(256, BufferUsage.StructuredBufferReadWrite, sizeof(uint)));
        DeviceBuffer destination = device.ResourceFactory.CreateBuffer(new BufferDescription(256, BufferUsage.StructuredBufferReadWrite, sizeof(uint)));

        TransferCommandBuffer transfer = device.ResourceFactory.CreateTransferCommandBuffer();
        transfer.Begin();
        transfer.CopyBuffer(source, 0, destination, 0, 256);
        transfer.End();
        device.SubmitAndWait(transfer);

        Assert.Contains(profiler.Submits, s => s.Kind == SubmitKind.Transfer);
    }

    [Fact]
    public void RequestExecutionTiming_True_RecordsExecutionTime()
    {
        RecordingProfiler profiler = new() { RequestExecutionTiming = true };
        using GraphicsDevice device = CreateProfiledDevice(profiler);

        DeviceBuffer source = device.ResourceFactory.CreateBuffer(new BufferDescription(256, BufferUsage.StructuredBufferReadWrite, sizeof(uint)));
        DeviceBuffer destination = device.ResourceFactory.CreateBuffer(new BufferDescription(256, BufferUsage.StructuredBufferReadWrite, sizeof(uint)));

        device.RunTestGraph(context =>
        {
            CommandBuffer cl = context.GetCommandBuffer();
            cl.CopyBuffer(source, 0, destination, 0, 256);
            context.SubmitCommandBuffer(cl);
        });
        device.WaitForIdle();

        (PassInfo? _, string _, bool isTransfer, double milliseconds) = Assert.Single(profiler.ExecutionTimes);
        Assert.False(isTransfer);
        Assert.True(milliseconds >= 0);
    }

    [Fact]
    public void RequestExecutionTiming_False_NeverRecordsExecutionTime()
    {
        RecordingProfiler profiler = new() { RequestExecutionTiming = false };
        using GraphicsDevice device = CreateProfiledDevice(profiler);

        DeviceBuffer source = device.ResourceFactory.CreateBuffer(new BufferDescription(256, BufferUsage.StructuredBufferReadWrite, sizeof(uint)));
        DeviceBuffer destination = device.ResourceFactory.CreateBuffer(new BufferDescription(256, BufferUsage.StructuredBufferReadWrite, sizeof(uint)));

        device.RunTestGraph(context =>
        {
            CommandBuffer cl = context.GetCommandBuffer();
            cl.CopyBuffer(source, 0, destination, 0, 256);
            context.SubmitCommandBuffer(cl);
        });
        device.WaitForIdle();

        Assert.Empty(profiler.ExecutionTimes);
    }

    [Fact]
    public void SubmitAndWaitTransfer_WithTiming_RecordsExecutionTime()
    {
        RecordingProfiler profiler = new() { RequestExecutionTiming = true };
        using GraphicsDevice device = CreateProfiledDevice(profiler);

        DeviceBuffer source = device.ResourceFactory.CreateBuffer(new BufferDescription(256, BufferUsage.StructuredBufferReadWrite, sizeof(uint)));
        DeviceBuffer destination = device.ResourceFactory.CreateBuffer(new BufferDescription(256, BufferUsage.StructuredBufferReadWrite, sizeof(uint)));

        TransferCommandBuffer transfer = device.ResourceFactory.CreateTransferCommandBuffer();
        transfer.Begin();
        transfer.CopyBuffer(source, 0, destination, 0, 256);
        transfer.End();
        device.SubmitAndWait(transfer);

        (PassInfo? _, string _, bool isTransfer, double milliseconds) = Assert.Single(profiler.ExecutionTimes);
        Assert.True(isTransfer);
        Assert.True(milliseconds >= 0);
    }
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanProfilerEventsTests : ProfilerEventsTests<VulkanDeviceCreator> { }
#endif
