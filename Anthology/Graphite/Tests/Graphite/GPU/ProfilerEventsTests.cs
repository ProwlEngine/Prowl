#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using Prowl.Graphite.RenderGraph;
using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Tests;

file sealed class GraphRecorder : IGraphProfiler
{
    public readonly List<PassInfo> PassesEnded = new();
    public readonly List<PassStats> PassStatsEnded = new();

    public void BeginView(in ViewInfo view) { }
    public void EndView(in ViewInfo view) { }
    public void BeginPass(in PassInfo pass) { }

    public void EndPass(in PassInfo pass, in PassStats stats)
    {
        PassesEnded.Add(pass);
        PassStatsEnded.Add(stats);
    }

    public void RecordPassRead(in PassInfo pass, RenderResourceID resource, RenderTexture? texture, DeviceBuffer? buffer) { }
    public void RecordPassWrite(in PassInfo pass, RenderResourceID resource, RenderTexture? texture, DeviceBuffer? buffer) { }
}

file sealed class LifecycleRecorder : IGraphProfiler
{
    public readonly List<PassInfo> PassesBegun = new();
    public readonly List<PassInfo> PassesEnded = new();
    public readonly List<(PassInfo Pass, RenderResourceID Resource, RenderTexture? Texture, DeviceBuffer? Buffer)> PassReads = new();
    public readonly List<(PassInfo Pass, RenderResourceID Resource, RenderTexture? Texture, DeviceBuffer? Buffer)> PassWrites = new();

    public void BeginView(in ViewInfo view) { }
    public void EndView(in ViewInfo view) { }
    public void BeginPass(in PassInfo pass) => PassesBegun.Add(pass);
    public void EndPass(in PassInfo pass, in PassStats stats) => PassesEnded.Add(pass);
    public void RecordPassRead(in PassInfo pass, RenderResourceID resource, RenderTexture? texture, DeviceBuffer? buffer)
        => PassReads.Add((pass, resource, texture, buffer));
    public void RecordPassWrite(in PassInfo pass, RenderResourceID resource, RenderTexture? texture, DeviceBuffer? buffer)
        => PassWrites.Add((pass, resource, texture, buffer));
}

file sealed class TimingRecorder : IGpuStatsProfiler
{
    public readonly List<(CommandBufferInfo Info, bool IsTransfer, double Milliseconds)> ExecutionTimes = new();

    public void RecordExecutionTime(in CommandBufferInfo commandBuffer, bool isTransfer, double milliseconds)
        => ExecutionTimes.Add((commandBuffer, isTransfer, milliseconds));

    public void RecordGpuVertexStats(in CommandBufferInfo commandBuffer, in GpuVertexStats stats) { }
    public void RecordExecutionResolved(ulong executionId) { }
}

file sealed class ResolveOrderRecorder : IGpuStatsProfiler
{
    public readonly List<string> Events = new();

    public void RecordExecutionTime(in CommandBufferInfo commandBuffer, bool isTransfer, double milliseconds) => Events.Add("time");
    public void RecordGpuVertexStats(in CommandBufferInfo commandBuffer, in GpuVertexStats stats) => Events.Add("stats");
    public void RecordExecutionResolved(ulong executionId) => Events.Add("resolved");
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
    public void EndPass(in PassInfo pass, in PassStats stats) { }
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

file sealed class DrawingRasterPass : RasterPass
{
    private readonly RenderResourceID _id;
    private readonly GraphicsProgram _program;
    private readonly DeviceBuffer _vertices;
    private readonly DeviceBuffer _indirect;

    public DrawingRasterPass(RenderResourceID id, GraphicsProgram program, DeviceBuffer vertices, DeviceBuffer indirect)
    {
        _id = id;
        _program = program;
        _vertices = vertices;
        _indirect = indirect;
    }

    public override string Name => "ProfilerDraw";

    public override void Setup(RenderContextBuilder builder)
        => SetTarget(builder, _id, GraphTextureDesc.ViewSized(PixelFormat.R32_G32_B32_A32_Float), ops: TargetLoadStoreOps.Clear(new Color(0, 0, 0, 1)));

    public override void Render(RenderContext context, CommandBuffer cmd)
    {
        cmd.SetFullViewport();
        cmd.SetShader(_program);
        cmd.SetVertexSource(new VertexSource().SetBuffer("POSITION", _vertices));
        cmd.ClearProperties();
        cmd.Draw(3);
        cmd.Draw(3);
        cmd.Draw(3);
        cmd.DrawIndirect(_indirect, 0, 1, (uint)System.Runtime.CompilerServices.Unsafe.SizeOf<IndirectDrawArguments>());
    }
}

file sealed class DispatchingPass : IPass
{
    private readonly ComputeProgram _program;
    private readonly PropertySet _properties;

    public DispatchingPass(ComputeProgram program, PropertySet properties)
    {
        _program = program;
        _properties = properties;
    }

    public string Name => "ProfilerDispatch";

    public void Setup(RenderContextBuilder builder) { }

    public void Render(RenderContext context, CommandBuffer cmd)
    {
        cmd.SetComputeShader(_program);
        cmd.SetProperties(_properties);
        cmd.Dispatch(1, 1, 1);
        cmd.Dispatch(1, 1, 1);
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

    private static ComputeProgram CreateBasicComputeProgram(GraphicsDevice device)
    {
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
        return device.ResourceFactory.CreateComputeProgram(new ComputeDescription(stage, layouts, 16, 16, 1));
    }

    private static GraphicsProgram CreateSinkProgram(GraphicsDevice device)
    {
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
        return device.ResourceFactory.CreateGraphicsProgram(description);
    }

    [Fact]
    public void EndPass_ReportsExactPassStats()
    {
        GraphRecorder profiler = new();
        using GraphicsDevice device = CreateProfiledDevice(profiler);

        const uint size = 16;
        const uint stride = 52;
        const uint count = size * size;

        GraphicsProgram graphics = CreateSinkProgram(device);
        ComputeProgram compute = CreateBasicComputeProgram(device);

        DeviceBuffer vertices = device.ResourceFactory.CreateBuffer(new BufferDescription(stride * 3, BufferUsage.VertexBuffer));
        device.UpdateBuffer(vertices, 0, new byte[stride * 3]);
        DeviceBuffer indirect = device.ResourceFactory.CreateBuffer(new BufferDescription(
            (uint)System.Runtime.CompilerServices.Unsafe.SizeOf<IndirectDrawArguments>(), BufferUsage.IndirectBuffer));
        device.UpdateBuffer(indirect, 0, new IndirectDrawArguments { VertexCount = 3, InstanceCount = 1 });

        DeviceBuffer source = device.ResourceFactory.CreateBuffer(new BufferDescription(count * sizeof(float), BufferUsage.StructuredBufferReadWrite));
        DeviceBuffer destination = device.ResourceFactory.CreateBuffer(new BufferDescription(count * sizeof(float), BufferUsage.StructuredBufferReadWrite));
        PropertySet props = new();
        props.SetInt("Width", (int)size);
        props.SetInt("Height", (int)size);
        props.SetBuffer("Source", source);
        props.SetBuffer("Destination", destination);

        RenderResourceID id = RenderResourceID.Intern("profiler_pass_stats_target");
        using RenderPipeline pipeline = new([new DrawingRasterPass(id, graphics, vertices, indirect), new DispatchingPass(compute, props)]);

        device.DispatchGraph(pipeline, new ProfilerView[] { new(size, size) });
        device.WaitForIdle();

        Assert.Equal(new[] { "ProfilerDraw", "ProfilerDispatch" }, profiler.PassesEnded.ConvertAll(p => p.Name));

        PassStats draw = profiler.PassStatsEnded[0];
        Assert.Equal(3u, draw.Draws);
        Assert.Equal(1u, draw.IndirectDraws);
        Assert.Equal(0u, draw.Dispatches);
        Assert.Equal(1u, draw.ShaderSwitches);
        Assert.Equal(1u, draw.PipelineBinds);
        Assert.True(draw.Barriers > 0);

        PassStats dispatch = profiler.PassStatsEnded[1];
        Assert.Equal(0u, dispatch.Draws);
        Assert.Equal(0u, dispatch.IndirectDraws);
        Assert.Equal(2u, dispatch.Dispatches);
        Assert.Equal(1u, dispatch.ShaderSwitches);
        Assert.Equal(1u, dispatch.PipelineBinds);
        Assert.True(dispatch.ResourceSetBinds > 0);
    }

    [Fact]
    public void DispatchGraph_RecordsPassLifecycleAndReads()
    {
        LifecycleRecorder profiler = new();
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
    public void ConcurrentDispatch_DeliversCompleteEventsUnderEachExecutionId()
    {
        const int threadCount = 4;
        const int iterations = 100;
        const uint size = 32;

        CorrelationProfiler profiler = new();
        using GraphicsDevice device = CreateProfiledDevice(profiler);

        List<ExecutionTask> tasks = new();
        List<Exception> failures = new();
        using System.Threading.Barrier start = new(threadCount);

        Thread[] threads = new Thread[threadCount];
        for (int t = 0; t < threadCount; t++)
        {
            int threadIndex = t;
            threads[t] = new Thread(() =>
            {
                try
                {
                    DeviceBuffer readback = device.ResourceFactory.CreateBuffer(new BufferDescription(size * size * 16, BufferUsage.Staging));
                    RenderResourceID id = RenderResourceID.Intern($"profiler_concurrent_target_{threadIndex}");
                    using RenderPipeline pipeline = new([new ClearingRasterPass(id), new ReadingCopyPass(id, readback)]);
                    ProfilerView[] views = [new(size, size), new(size, size)];

                    start.SignalAndWait();
                    for (int i = 0; i < iterations; i++)
                    {
                        ExecutionTask task = device.DispatchGraph(pipeline, views);
                        lock (tasks)
                            tasks.Add(task);
                    }
                }
                catch (Exception e)
                {
                    lock (failures)
                        failures.Add(e);
                }
            });
            threads[t].Start();
        }

        foreach (Thread thread in threads)
            thread.Join();

        device.WaitForIdle();

        Assert.Empty(failures);
        Assert.Equal(threadCount * iterations, tasks.Count);
        Assert.Equal(tasks.Count, tasks.ConvertAll(t => t.Id).Distinct().Count());

        foreach (ExecutionTask task in tasks)
        {
            ulong execution = task.Id;

            Assert.Equal(new[] { 0, 1 }, profiler.ViewsBegun.FindAll(v => v.ExecutionId == execution).ConvertAll(v => v.Index).Order().ToArray());
            Assert.Equal(4, profiler.PassesBegun.FindAll(p => p.ExecutionId == execution).Count);

            var passTimings = profiler.Timings.FindAll(t => t.Info.ExecutionId == execution && t.Info.Pass != null);
            foreach (int viewIndex in new[] { 0, 1 })
            {
                foreach (string name in new[] { "ProfilerClear", "ProfilerCopy" })
                {
                    (int _, CommandBufferInfo info) = Assert.Single(
                        passTimings, t => t.Info.Pass!.Value.ViewIndex == viewIndex && t.Info.Pass!.Value.Name == name);
                    Assert.Equal(execution, info.Pass!.Value.ExecutionId);
                }
            }

            (int resolvedOrder, ulong _) = Assert.Single(profiler.Resolved, r => r.ExecutionId == execution);
            foreach ((int order, CommandBufferInfo _) in profiler.Timings.FindAll(t => t.Info.ExecutionId == execution))
                Assert.True(order < resolvedOrder);
        }

        Assert.Equal(tasks.Count, profiler.Resolved.Count);
        Assert.Equal(tasks.Count * 4, profiler.Timings.Count);
    }

    [Fact]
    public void ExecutionTiming_RecordsExecutionTime()
    {
        TimingRecorder profiler = new();
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
    public void CompositeProfiler_ForwardsEachCategoryToEverySinkThatImplementsIt()
    {
        TimingRecorder first = new();
        TimingRecorder second = new();
        GraphRecorder graph = new();
        using GraphicsDevice device = CreateProfiledDevice(new CompositeProfiler(first, graph, new CompositeProfiler(second)));

        DeviceBuffer source = device.ResourceFactory.CreateBuffer(new BufferDescription(256, BufferUsage.StructuredBufferReadWrite));
        DeviceBuffer destination = device.ResourceFactory.CreateBuffer(new BufferDescription(256, BufferUsage.StructuredBufferReadWrite));

        device.RunTestGraph((context, cl) =>
        {
            cl.CopyBuffer(source, 0, destination, 0, 256);
        });
        device.WaitForIdle();

        Assert.Single(first.ExecutionTimes);
        Assert.Single(second.ExecutionTimes);
    }

    [Fact]
    public void ExecutionResolved_FiresOnceAfterTheFenceWithOrWithoutQueries()
    {
        ResolveOrderRecorder profiler = new();
        using GraphicsDevice device = CreateProfiledDevice(profiler);

        ExecutionTask empty = device.BeginExecution();
        device.CompleteExecution(empty);
        Assert.Empty(profiler.Events);

        device.WaitForExecution(empty);
        Assert.Equal(new[] { "resolved" }, profiler.Events);

        profiler.Events.Clear();
        DeviceBuffer source = device.ResourceFactory.CreateBuffer(new BufferDescription(256, BufferUsage.StructuredBufferReadWrite));
        DeviceBuffer destination = device.ResourceFactory.CreateBuffer(new BufferDescription(256, BufferUsage.StructuredBufferReadWrite));
        ExecutionTask task = device.RunTestGraph((context, cl) => cl.CopyBuffer(source, 0, destination, 0, 256));
        Assert.Empty(profiler.Events);

        device.WaitForExecution(task);
        Assert.Equal("resolved", profiler.Events[^1]);
        Assert.Contains("time", profiler.Events);
        Assert.Single(profiler.Events, e => e == "resolved");
    }

    [Fact]
    public void AttachDetach_TakeEffectFromTheNextExecution()
    {
        using GraphicsDevice device = GD.BackendType switch
        {
            GraphicsBackend.Vulkan => GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true)),
            _ => throw new NotSupportedException(),
        };

        DeviceBuffer source = device.ResourceFactory.CreateBuffer(new BufferDescription(256, BufferUsage.StructuredBufferReadWrite));
        DeviceBuffer destination = device.ResourceFactory.CreateBuffer(new BufferDescription(256, BufferUsage.StructuredBufferReadWrite));
        TimingRecorder profiler = new();

        void RunCopyGraph(bool attachDuringDispatch)
        {
            device.RunTestGraph((context, cl) =>
            {
                if (attachDuringDispatch)
                    device.Debug.Attach(profiler);
                cl.CopyBuffer(source, 0, destination, 0, 256);
            });
            device.WaitForIdle();
        }

        RunCopyGraph(attachDuringDispatch: true);
        Assert.Empty(profiler.ExecutionTimes);

        RunCopyGraph(attachDuringDispatch: false);
        Assert.NotEmpty(profiler.ExecutionTimes);

        device.Debug.Detach(profiler);
        profiler.ExecutionTimes.Clear();
        RunCopyGraph(attachDuringDispatch: false);
        Assert.Empty(profiler.ExecutionTimes);
    }

    [Fact]
    public void Record_WithTiming_RecordsExecutionTime()
    {
        TimingRecorder profiler = new();
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
