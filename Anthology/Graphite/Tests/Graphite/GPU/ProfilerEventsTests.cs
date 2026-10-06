#nullable enable

using System;
using System.Collections.Generic;

using Prowl.Graphite.RenderGraph;
using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Tests;

// Coverage for the IProfiler event wiring added this session: pipeline switches, draws/dispatches,
// pass begin/end + resource reads, command buffer submission counts,
// and GPU execution timing. Each test builds its own isolated device with a RecordingProfiler
// attached, since IProfiler is set once at device construction.

file sealed class RecordingProfiler : ICommandProfiler, IGraphProfiler, IGpuStatsProfiler
{
    public readonly List<ShaderSwitchInfo> ShaderSwitches = new();
    public readonly List<PipelineBindInfo> PipelineBinds = new();
    public readonly List<object> ShaderAndPipelineOrder = new();
    public readonly List<DrawCallInfo> Draws = new();
    public readonly List<DispatchCallInfo> Dispatches = new();
    public readonly List<(CommandBufferInfo Info, bool IsTransfer)> Submits = new();
    public readonly List<PassInfo> PassesBegun = new();
    public readonly List<PassInfo> PassesEnded = new();
    public readonly List<(PassInfo Pass, RenderResourceID Resource, RenderTexture? Texture, DeviceBuffer? Buffer)> PassReads = new();
    public readonly List<(PassInfo Pass, RenderResourceID Resource, RenderTexture? Texture, DeviceBuffer? Buffer)> PassWrites = new();
    public readonly List<(CommandBufferInfo info, bool IsTransfer, double Milliseconds)> ExecutionTimes = new();


    public void BeginView(in ViewInfo view) { }
    public void EndView(in ViewInfo view) { }

    public void BeginPass(in PassInfo pass) => PassesBegun.Add(pass);
    public void EndPass(in PassInfo pass) => PassesEnded.Add(pass);
    public void RecordPassRead(in PassInfo pass, RenderResourceID resource, RenderTexture? texture, DeviceBuffer? buffer)
        => PassReads.Add((pass, resource, texture, buffer));
    public void RecordPassWrite(in PassInfo pass, RenderResourceID resource, RenderTexture? texture, DeviceBuffer? buffer)
        => PassWrites.Add((pass, resource, texture, buffer));

    public void RecordDraw(in CommandBufferInfo commandBuffer, in DrawCallInfo info) => Draws.Add(info);
    public void RecordDispatch(in CommandBufferInfo commandBuffer, in DispatchCallInfo info) => Dispatches.Add(info);
    public void RecordShaderSwitch(in CommandBufferInfo commandBuffer, in ShaderSwitchInfo info)
    {
        ShaderSwitches.Add(info);
        ShaderAndPipelineOrder.Add(info);
    }

    public void RecordPipelineBind(in CommandBufferInfo commandBuffer, in PipelineBindInfo info)
    {
        PipelineBinds.Add(info);
        ShaderAndPipelineOrder.Add(info);
    }

    public void RecordSubmit(in CommandBufferInfo commandBuffer, bool isTransfer) => Submits.Add((commandBuffer, isTransfer));

    public void RecordExecutionTime(in CommandBufferInfo commandBuffer, bool isTransfer, double milliseconds)
        => ExecutionTimes.Add((commandBuffer, isTransfer, milliseconds));

    public void RecordGpuVertexStats(in CommandBufferInfo commandBuffer, in GpuVertexStats stats) { }
    public void RecordExecutionResolved(ulong executionId) { }
}

file sealed class StatsOnlyProfiler : IGpuStatsProfiler
{
    public readonly List<CommandBufferInfo> Timed = new();

    public void RecordExecutionTime(in CommandBufferInfo commandBuffer, bool isTransfer, double milliseconds) => Timed.Add(commandBuffer);
    public void RecordGpuVertexStats(in CommandBufferInfo commandBuffer, in GpuVertexStats stats) { }
    public void RecordExecutionResolved(ulong executionId) { }
}

file sealed class CorrelationProfiler : IGraphProfiler, IGpuStatsProfiler
{
    private readonly object _lock = new();

    public readonly List<ViewInfo> ViewsBegun = new();
    public readonly List<PassInfo> PassesBegun = new();
    public readonly List<(int Order, CommandBufferInfo Info)> Timings = new();
    public readonly List<(int Order, ulong ExecutionId)> Resolved = new();
    private int _order;

    public void BeginView(in ViewInfo view) { lock (_lock) ViewsBegun.Add(view); }
    public void EndView(in ViewInfo view) { }
    public void BeginPass(in PassInfo pass) { lock (_lock) PassesBegun.Add(pass); }
    public void EndPass(in PassInfo pass) { }
    public void RecordPassRead(in PassInfo pass, RenderResourceID resource, RenderTexture? texture, DeviceBuffer? buffer) { }
    public void RecordPassWrite(in PassInfo pass, RenderResourceID resource, RenderTexture? texture, DeviceBuffer? buffer) { }

    public void RecordExecutionTime(in CommandBufferInfo commandBuffer, bool isTransfer, double milliseconds)
    {
        lock (_lock) Timings.Add((_order++, commandBuffer));
    }

    public void RecordGpuVertexStats(in CommandBufferInfo commandBuffer, in GpuVertexStats stats) { }

    public void RecordExecutionResolved(ulong executionId)
    {
        lock (_lock) Resolved.Add((_order++, executionId));
    }
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
    public int ViewId => 0;
}

file sealed class ClearingRasterPass : RasterPass
{
    private readonly RenderResourceID _id;

    public ClearingRasterPass(RenderResourceID id) => _id = id;

    public override string Name => "ProfilerClear";

    public override void Setup(RenderContextBuilder builder)
        => SetTarget(builder, _id, GraphTextureDesc.ViewSized(PixelFormat.R32_G32_B32_A32_Float), ops: TargetLoadStoreOps.Clear(new Color(0, 0, 0, 1)));

    public override void Render(RenderContext context, CommandBuffer cmd)
    {
    }
}

file sealed class ReadingCopyPass : IPass
{
    private readonly RenderResourceID _id;
    private readonly DeviceBuffer _readback;
    private TextureHandle _handle;

    public ReadingCopyPass(RenderResourceID id, DeviceBuffer readback)
    {
        _id = id;
        _readback = readback;
    }

    public string Name => "ProfilerCopy";

    public void Setup(RenderContextBuilder builder) => _handle = builder.DeclareInputTexture(_id, TextureState.TransferSrc);

    public void Render(RenderContext context, CommandBuffer cmd)
    {
        RenderTexture target = context.GetRenderTexture(_handle);
        cmd.CopyTextureToBuffer(target.ColorTextures[0], _readback, 0, TextureRegion.Whole(target.ColorTextures[0]));
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
    public void Dispatch_RecordsShaderSwitchPipelineBindResourceSetBindAndDispatch()
    {
        RecordingProfiler profiler = new();
        using GraphicsDevice device = CreateProfiledDevice(profiler);

        const uint width = 16;
        const uint height = 16;
        const uint count = width * height;

        DeviceBuffer source = device.ResourceFactory.CreateBuffer(new BufferDescription(
            count * sizeof(float), BufferUsage.StructuredBufferReadWrite));
        DeviceBuffer destination = device.ResourceFactory.CreateBuffer(new BufferDescription(
            count * sizeof(float), BufferUsage.StructuredBufferReadWrite));

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
        props.SetBuffer("Source", source);
        props.SetBuffer("Destination", destination);

        device.RunTestGraph((context, cl) =>
        {
            cl.SetComputeShader(program);
            cl.SetProperties(props);
            cl.Dispatch(1, 1, 1);
        });
        device.WaitForIdle();

        ShaderSwitchInfo shaderSwitch = Assert.Single(profiler.ShaderSwitches);
        Assert.True(shaderSwitch.IsCompute);
        Assert.Equal(ShaderStages.Compute, shaderSwitch.Stages);
        Assert.Same(program, shaderSwitch.Program);

        PipelineBindInfo bind = Assert.Single(profiler.PipelineBinds);
        Assert.True(bind.IsCompute);
        Assert.Same(program, bind.Program);
        Assert.Null(bind.Outputs);
        Assert.Null(bind.Topology);
        Assert.IsType<ShaderSwitchInfo>(profiler.ShaderAndPipelineOrder[0]);

        GraphicsCountersSnapshot counters = device.Counters.Snapshot();
        Assert.True(counters.ResourceSetBinds > 0);
        Assert.Equal(counters.ResourceSetBinds, counters.ResourceSetsBound);

        DispatchCallInfo dispatch = Assert.Single(profiler.Dispatches);
        Assert.Equal(1u, dispatch.GroupCountX);
        Assert.Equal(1u, dispatch.GroupCountY);
        Assert.Equal(1u, dispatch.GroupCountZ);
        Assert.False(dispatch.IsIndirect);
    }

    [Fact]
    public void Draw_OneShaderIntoTwoFramebufferLayouts_RecordsOneShaderSwitchThenTwoPipelineBinds()
    {
        RecordingProfiler profiler = new();
        using GraphicsDevice device = CreateProfiledDevice(profiler);

        const uint size = 16;
        const uint stride = 52;
        ShaderStageDescription[] stages = TestShaderLoader.LoadGraphics(device.BackendType, "VertexLayoutTestShader.slang");
        ShaderDescription description = new(stages)
        {
            BlendState = BlendStateDescription.SingleOverrideBlend,
            DepthStencilState = DepthStencilStateDescription.Disabled,
            RasterizerState = RasterizerStateDescription.CullNone,
            VertexLayouts =
            [
                new VertexLayoutDescription(0, stride,
                    new VertexElementDescription("POSITION", VertexElementFormat.Float3),
                    new VertexElementDescription("COLOR0", VertexElementFormat.Float4),
                    new VertexElementDescription("TEXCOORD0", VertexElementFormat.Float2),
                    new VertexElementDescription("COLOR1", VertexElementFormat.Float4))
            ],
        };
        GraphicsProgram program = device.ResourceFactory.CreateGraphicsProgram(description);

        DeviceBuffer vertices = device.ResourceFactory.CreateBuffer(new BufferDescription(stride * 3, BufferUsage.VertexBuffer));
        device.UpdateBuffer(vertices, 0, new byte[stride * 3]);

        Texture floatTarget = device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
            size, size, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.RenderTarget));
        Texture byteTarget = device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
            size, size, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.RenderTarget));
        Framebuffer floatFramebuffer = device.ResourceFactory.CreateFramebuffer(new FramebufferDescription(null, floatTarget));
        Framebuffer byteFramebuffer = device.ResourceFactory.CreateFramebuffer(new FramebufferDescription(null, byteTarget));

        device.RunTestGraph((context, cl) =>
        {
            cl.SetShader(program);
            cl.SetVertexSource(new VertexSource().SetBuffer("POSITION", vertices));
            cl.ClearProperties();

            cl.SetFramebuffer(floatFramebuffer, new TargetLoadStoreOps(AttachmentOps.Clear(Color.Black), AttachmentOps.Loaded));
            cl.SetFullViewport();
            cl.Draw(3);

            cl.SetFramebuffer(byteFramebuffer, new TargetLoadStoreOps(AttachmentOps.Clear(Color.Black), AttachmentOps.Loaded));
            cl.SetFullViewport();
            cl.Draw(3);
        });
        device.WaitForIdle();

        Assert.Equal(3, profiler.ShaderAndPipelineOrder.Count);
        ShaderSwitchInfo shaderSwitch = Assert.IsType<ShaderSwitchInfo>(profiler.ShaderAndPipelineOrder[0]);
        PipelineBindInfo first = Assert.IsType<PipelineBindInfo>(profiler.ShaderAndPipelineOrder[1]);
        PipelineBindInfo second = Assert.IsType<PipelineBindInfo>(profiler.ShaderAndPipelineOrder[2]);

        Assert.False(shaderSwitch.IsCompute);
        Assert.Same(program, shaderSwitch.Program);
        Assert.Same(program, first.Program);
        Assert.Same(program, second.Program);
        Assert.NotEqual(first.PipelineId, second.PipelineId);
        Assert.Equal(PixelFormat.R32_G32_B32_A32_Float, first.Outputs!.Value.ColorFormats[0]);
        Assert.Equal(PixelFormat.R8_G8_B8_A8_UNorm, second.Outputs!.Value.ColorFormats[0]);
        Assert.Equal(PrimitiveTopology.TriangleList, first.Topology);
    }

    [Fact]
    public void DispatchGraph_RecordsPassLifecycleReadsAndSubmits()
    {
        RecordingProfiler profiler = new();
        using GraphicsDevice device = CreateProfiledDevice(profiler);

        const uint size = 64;
        DeviceBuffer readback = device.ResourceFactory.CreateBuffer(new BufferDescription(size * size * 16, BufferUsage.Staging));

        RenderResourceID id = RenderResourceID.Intern("profiler_pass_target");
        ClearingRasterPass clearPass = new(id);
        ReadingCopyPass copyPass = new(id, readback);
        using RenderPipeline pipeline = new([clearPass, copyPass]);

        device.DispatchGraph(pipeline, new ProfilerView[] { new(size, size) });
        device.WaitForIdle();

        Assert.Equal(2, profiler.PassesBegun.Count);
        Assert.Equal(2, profiler.PassesEnded.Count);
        Assert.Equal(new[] { "ProfilerClear", "ProfilerCopy" }, profiler.PassesBegun.ConvertAll(p => p.Name));

        // ClearingRasterPass declares the target as an output; ReadingCopyPass declares it as an input.
        Assert.Contains(profiler.PassWrites, w => w.Pass.Name == "ProfilerClear" && w.Resource.Equals(id));
        Assert.DoesNotContain(profiler.PassReads, r => r.Pass.Name == "ProfilerClear");
        Assert.Contains(profiler.PassReads, r => r.Pass.Name == "ProfilerCopy" && r.Resource.Equals(id));

        Assert.Equal(
            new[] { "ProfilerClear", "ProfilerCopy" },
            profiler.Submits.ConvertAll(s => s.Info.Name));
        Assert.All(profiler.Submits, s => Assert.False(s.IsTransfer));

        Assert.True(device.Counters.Snapshot().Barriers(BarrierBin.TextureTransition) > 0);
    }

    [Fact]
    public void GpuStatsProfilerAlone_ReceivesPassInputsAndOutputs()
    {
        StatsOnlyProfiler profiler = new();
        using GraphicsDevice device = CreateProfiledDevice(profiler);

        const uint size = 64;
        DeviceBuffer readback = device.ResourceFactory.CreateBuffer(new BufferDescription(size * size * 16, BufferUsage.Staging));

        RenderResourceID id = RenderResourceID.Intern("profiler_stats_only_target");
        using RenderPipeline pipeline = new([new ClearingRasterPass(id), new ReadingCopyPass(id, readback)]);

        device.DispatchGraph(pipeline, new ProfilerView[] { new(size, size) });
        device.WaitForIdle();

        PassInfo clear = Assert.Single(profiler.Timed, t => t.Name == "ProfilerClear").Pass!.Value;
        PassInfo copy = Assert.Single(profiler.Timed, t => t.Name == "ProfilerCopy").Pass!.Value;
        Assert.Contains(id, clear.Outputs.ToArray());
        Assert.Contains(id, copy.Inputs.ToArray());
    }

    [Fact]
    public void CorrelationIds_MatchTimingsToExecutionViewAndPass_AcrossTwoExecutions()
    {
        CorrelationProfiler profiler = new();
        using GraphicsDevice device = CreateProfiledDevice(profiler);

        const uint size = 64;
        DeviceBuffer readback = device.ResourceFactory.CreateBuffer(new BufferDescription(size * size * 16, BufferUsage.Staging));

        RenderResourceID id = RenderResourceID.Intern("profiler_correlation_target");
        using RenderPipeline pipeline = new([new ClearingRasterPass(id), new ReadingCopyPass(id, readback)]);
        ProfilerView[] views = [new(size, size), new(size, size)];

        ExecutionTask first = device.DispatchGraph(pipeline, views);
        ExecutionTask second = device.DispatchGraph(pipeline, views);
        device.WaitForIdle();

        ulong[] executions = [first.Id, second.Id];
        Assert.NotEqual(first.Id, second.Id);

        foreach (ulong execution in executions)
        {
            Assert.Equal(new[] { 0, 1 }, profiler.ViewsBegun.FindAll(v => v.ExecutionId == execution).ConvertAll(v => v.Index));
            Assert.Equal(4, profiler.PassesBegun.FindAll(p => p.ExecutionId == execution).Count);

            var passTimings = profiler.Timings.FindAll(t => t.Info.ExecutionId == execution && t.Info.Pass != null);
            foreach (int viewIndex in new[] { 0, 1 })
            {
                foreach (string name in new[] { "ProfilerClear", "ProfilerCopy" })
                {
                    (int _, CommandBufferInfo info) = Assert.Single(
                        passTimings, t => t.Info.Pass!.Value.ViewIndex == viewIndex && t.Info.Pass!.Value.Name == name);
                    Assert.Equal(name, info.Name);
                    Assert.Equal(execution, info.Pass!.Value.ExecutionId);
                }
            }

            (int resolvedOrder, ulong _) = Assert.Single(profiler.Resolved, r => r.ExecutionId == execution);
            foreach ((int order, CommandBufferInfo info) in profiler.Timings.FindAll(t => t.Info.ExecutionId == execution))
                Assert.True(order < resolvedOrder);
        }

        Assert.All(profiler.Timings, t => Assert.Contains(t.Info.ExecutionId, executions));
        Assert.Equal(2, profiler.Resolved.Count);
    }

    [Fact]
    public void ExecutionTiming_RecordsExecutionTime()
    {
        RecordingProfiler profiler = new();
        using GraphicsDevice device = CreateProfiledDevice(profiler);

        DeviceBuffer source = device.ResourceFactory.CreateBuffer(new BufferDescription(256, BufferUsage.StructuredBufferReadWrite));
        DeviceBuffer destination = device.ResourceFactory.CreateBuffer(new BufferDescription(256, BufferUsage.StructuredBufferReadWrite));

        device.RunTestGraph((context, cl) =>
        {
            cl.CopyBuffer(source, 0, destination, 0, 256);
        });
        device.WaitForIdle();

        (CommandBufferInfo _, bool isTransfer, double milliseconds) = Assert.Single(profiler.ExecutionTimes);
        Assert.False(isTransfer);
        Assert.True(milliseconds >= 0);
    }

    [Fact]
    public void SetProfiler_SwapsActiveProfilerAtRuntime()
    {
        using GraphicsDevice device = GD.BackendType switch
        {
            GraphicsBackend.Vulkan => GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true)),
            _ => throw new NotSupportedException(),
        };

        DeviceBuffer source = device.ResourceFactory.CreateBuffer(new BufferDescription(256, BufferUsage.StructuredBufferReadWrite));
        DeviceBuffer destination = device.ResourceFactory.CreateBuffer(new BufferDescription(256, BufferUsage.StructuredBufferReadWrite));

        void RunCopyGraph()
        {
            device.RunTestGraph((context, cl) =>
            {
                cl.CopyBuffer(source, 0, destination, 0, 256);
            });
            device.WaitForIdle();
        }

        Assert.Null(device.Profiler);

        RecordingProfiler profiler = new();
        device.SetProfiler(profiler);
        Assert.Same(profiler, device.Profiler);

        RunCopyGraph();
        Assert.NotEmpty(profiler.Submits);

        device.SetProfiler(null);
        Assert.Null(device.Profiler);

        profiler.Submits.Clear();
        RunCopyGraph();
        Assert.Empty(profiler.Submits);
    }

    [Fact]
    public void Record_WithTiming_RecordsExecutionTime()
    {
        RecordingProfiler profiler = new();
        using GraphicsDevice device = CreateProfiledDevice(profiler);

        DeviceBuffer source = device.ResourceFactory.CreateBuffer(new BufferDescription(256, BufferUsage.StructuredBufferReadWrite));
        DeviceBuffer destination = device.ResourceFactory.CreateBuffer(new BufferDescription(256, BufferUsage.StructuredBufferReadWrite));

        device.Record(transfer => transfer.CopyBuffer(source, 0, destination, 0, 256)).Wait();

        (CommandBufferInfo _, bool isTransfer, double milliseconds) = Assert.Single(profiler.ExecutionTimes);
        Assert.True(isTransfer);
        Assert.True(milliseconds >= 0);
    }
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanProfilerEventsTests : ProfilerEventsTests<VulkanDeviceCreator> { }
#endif
