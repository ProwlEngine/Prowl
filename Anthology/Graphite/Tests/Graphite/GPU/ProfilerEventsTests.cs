#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using Prowl.Graphite.RenderGraph;
using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Tests;

file abstract class ExecutionRecorder : IProfiler
{
    public ulong ExecutionId;
    public string GraphName = "";
    public int Ended;

    public virtual void BeginExecution(ulong executionId, string graphName)
    {
        ExecutionId = executionId;
        GraphName = graphName;
    }

    public virtual void EndExecution() => Ended++;
}

file sealed class GraphRecorder : ExecutionRecorder, IGraphProfiler
{
    public readonly List<PassInfo> PassesEnded = new();
    public readonly List<PassStats> PassStatsEnded = new();

    public void BeginView(in ViewInfo view) { }
    public void EndView(in ViewInfo view) { }
    public void BeginPass(in PassInfo pass) { }
    public void SkipPass(string name, int viewIndex, PassSkipReason reason) { }

    public void EndPass(in PassInfo pass, in PassStats stats)
    {
        PassesEnded.Add(pass);
        PassStatsEnded.Add(stats);
    }
}

file sealed class LifecycleRecorder : ExecutionRecorder, IGraphProfiler
{
    public readonly List<PassInfo> PassesBegun = new();
    public readonly List<PassInfo> PassesEnded = new();

    public void BeginView(in ViewInfo view) { }
    public void EndView(in ViewInfo view) { }
    public void BeginPass(in PassInfo pass) => PassesBegun.Add(pass);
    public void SkipPass(string name, int viewIndex, PassSkipReason reason) { }
    public void EndPass(in PassInfo pass, in PassStats stats) => PassesEnded.Add(pass);
}

file sealed class TimingRecorder : ExecutionRecorder, IGpuStatsProfiler
{
    public readonly List<(CommandBufferInfo Info, double Milliseconds)> ExecutionTimes = new();

    public void RecordExecutionTime(in CommandBufferInfo commandBuffer, double milliseconds)
        => ExecutionTimes.Add((commandBuffer, milliseconds));

    public void RecordGpuVertexStats(in CommandBufferInfo commandBuffer, in GpuVertexStats stats) { }
}

file sealed class ResolveOrderRecorder : IGpuStatsProfiler
{
    public readonly List<string> Events = new();

    public void BeginExecution(ulong executionId, string graphName) => Events.Add("begin");
    public void EndExecution() => Events.Add("end");
    public void RecordExecutionTime(in CommandBufferInfo commandBuffer, double milliseconds) => Events.Add("time");
    public void RecordGpuVertexStats(in CommandBufferInfo commandBuffer, in GpuVertexStats stats) => Events.Add("stats");
}

file sealed class StatsOnlyProfiler : ExecutionRecorder, IGpuStatsProfiler
{
    public readonly List<CommandBufferInfo> Timed = new();

    public void RecordExecutionTime(in CommandBufferInfo commandBuffer, double milliseconds) => Timed.Add(commandBuffer);
    public void RecordGpuVertexStats(in CommandBufferInfo commandBuffer, in GpuVertexStats stats) { }
}

file sealed class CorrelationProfiler : ExecutionRecorder, IGraphProfiler, IGpuStatsProfiler
{
    public readonly List<ViewInfo> ViewsBegun = new();
    public readonly List<PassInfo> PassesBegun = new();
    public readonly List<CommandBufferInfo> Timings = new();
    public int TimingsAtEnd = -1;

    public void BeginView(in ViewInfo view) => ViewsBegun.Add(view);
    public void EndView(in ViewInfo view) { }
    public void BeginPass(in PassInfo pass) => PassesBegun.Add(pass);
    public void SkipPass(string name, int viewIndex, PassSkipReason reason) { }
    public void EndPass(in PassInfo pass, in PassStats stats) { }
    public void RecordExecutionTime(in CommandBufferInfo commandBuffer, double milliseconds) => Timings.Add(commandBuffer);
    public void RecordGpuVertexStats(in CommandBufferInfo commandBuffer, in GpuVertexStats stats) { }

    public override void EndExecution()
    {
        TimingsAtEnd = Timings.Count;
        base.EndExecution();
    }

    public void AssertCompleteTwoViewCopyExecution(ulong executionId, string graphName)
    {
        Assert.Equal(executionId, ExecutionId);
        Assert.Equal(graphName, GraphName);
        Assert.Equal(1, Ended);
        Assert.Equal(new[] { 0, 1 }, ViewsBegun.ConvertAll(v => v.Index));
        Assert.Equal(4, PassesBegun.Count);
        Assert.Equal(4, TimingsAtEnd);
        foreach (int viewIndex in new[] { 0, 1 })
        {
            foreach (string name in new[] { "ProfilerClear", "ProfilerCopy" })
            {
                CommandBufferInfo info = Assert.Single(Timings, t => t.Pass!.Value.ViewIndex == viewIndex && t.Pass!.Value.Name == name);
                Assert.Equal(name, info.Name);
            }
        }
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
    private GraphicsDevice CreateDevice() => GD.BackendType switch
    {
        GraphicsBackend.Vulkan => GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true)),
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
        return device.Tracked().CreateComputeProgram(new ComputeDescription(stage, layouts, 16, 16, 1));
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
        return device.Tracked().CreateGraphicsProgram(description);
    }

    [Fact]
    public void EndPass_ReportsExactPassStats()
    {
        GraphRecorder profiler = new();
        using GraphicsDevice device = CreateDevice();
        using IDisposable trackedCleanup = device.TrackedCleanup();

        const uint size = 16;
        const uint stride = 52;
        const uint count = size * size;

        GraphicsProgram graphics = CreateSinkProgram(device);
        ComputeProgram compute = CreateBasicComputeProgram(device);

        DeviceBuffer vertices = device.Tracked().CreateBuffer(new BufferDescription(stride * 3, BufferUsage.VertexBuffer));
        device.UpdateBuffer(vertices, 0, new byte[stride * 3]);
        DeviceBuffer indirect = device.Tracked().CreateBuffer(new BufferDescription(
            (uint)System.Runtime.CompilerServices.Unsafe.SizeOf<IndirectDrawArguments>(), BufferUsage.IndirectBuffer));
        device.UpdateBuffer(indirect, 0, new IndirectDrawArguments { VertexCount = 3, InstanceCount = 1 });

        DeviceBuffer source = device.Tracked().CreateBuffer(new BufferDescription(count * sizeof(float), BufferUsage.StructuredBufferReadWrite));
        DeviceBuffer destination = device.Tracked().CreateBuffer(new BufferDescription(count * sizeof(float), BufferUsage.StructuredBufferReadWrite));
        PropertySet props = new();
        props.SetInt("Width", (int)size);
        props.SetInt("Height", (int)size);
        props.SetBuffer("Source", source);
        props.SetBuffer("Destination", destination);

        RenderResourceID id = RenderResourceID.Intern("profiler_pass_stats_target");
        using RenderPipeline pipeline = new([new DrawingRasterPass(id, graphics, vertices, indirect), new DispatchingPass(compute, props)]);

        device.DispatchGraph(pipeline, new ProfilerView[] { new(size, size) }, profiler);
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
    public void DispatchGraph_RecordsPassLifecycle()
    {
        LifecycleRecorder profiler = new();
        using GraphicsDevice device = CreateDevice();
        using IDisposable trackedCleanup = device.TrackedCleanup();

        const uint size = 64;
        DeviceBuffer readback = device.Tracked().CreateBuffer(new BufferDescription(size * size * 16, BufferUsage.Staging));

        RenderResourceID id = RenderResourceID.Intern("profiler_pass_target");
        ClearingRasterPass clearPass = new(id);
        ReadingCopyPass copyPass = new(id, readback);
        using RenderPipeline pipeline = new([clearPass, copyPass]);

        device.DispatchGraph(pipeline, new ProfilerView[] { new(size, size) }, profiler);
        device.WaitForIdle();

        Assert.Equal(2, profiler.PassesBegun.Count);
        Assert.Equal(2, profiler.PassesEnded.Count);
        Assert.Equal(new[] { "ProfilerClear", "ProfilerCopy" }, profiler.PassesBegun.ConvertAll(p => p.Name));

        Assert.True(device.Counters.Snapshot().Barriers(BarrierBin.TextureTransition) > 0);
    }

    [Fact]
    public void GpuStatsProfilerAlone_ReceivesPassInputsAndOutputs()
    {
        StatsOnlyProfiler profiler = new();
        using GraphicsDevice device = CreateDevice();
        using IDisposable trackedCleanup = device.TrackedCleanup();

        const uint size = 64;
        DeviceBuffer readback = device.Tracked().CreateBuffer(new BufferDescription(size * size * 16, BufferUsage.Staging));

        RenderResourceID id = RenderResourceID.Intern("profiler_stats_only_target");
        using RenderPipeline pipeline = new([new ClearingRasterPass(id), new ReadingCopyPass(id, readback)]);

        device.DispatchGraph(pipeline, new ProfilerView[] { new(size, size) }, profiler);
        device.WaitForIdle();

        PassInfo clear = Assert.Single(profiler.Timed, t => t.Name == "ProfilerClear").Pass!.Value;
        PassInfo copy = Assert.Single(profiler.Timed, t => t.Name == "ProfilerCopy").Pass!.Value;
        Assert.Contains(id, clear.GetOutputs().Select(a => a.Id));
        Assert.DoesNotContain(id, clear.GetInputs().Select(a => a.Id));
        Assert.Contains(id, copy.GetInputs().Select(a => a.Id));
    }

    [Fact]
    public void EachExecution_DeliversOnlyItsOwnEventsToItsProfiler()
    {
        CorrelationProfiler firstProfiler = new();
        CorrelationProfiler secondProfiler = new();
        using GraphicsDevice device = CreateDevice();
        using IDisposable trackedCleanup = device.TrackedCleanup();

        const uint size = 64;
        DeviceBuffer readback = device.Tracked().CreateBuffer(new BufferDescription(size * size * 16, BufferUsage.Staging));

        RenderResourceID id = RenderResourceID.Intern("profiler_correlation_target");
        using RenderPipeline pipeline = new([new ClearingRasterPass(id), new ReadingCopyPass(id, readback)]) { Name = "Correlation" };
        ProfilerView[] views = [new(size, size), new(size, size)];

        ExecutionTask first = device.DispatchGraph(pipeline, views, firstProfiler);
        ExecutionTask second = device.DispatchGraph(pipeline, views, secondProfiler);
        device.WaitForIdle();

        Assert.True(first.Id < second.Id);
        firstProfiler.AssertCompleteTwoViewCopyExecution(first.Id, "Correlation");
        secondProfiler.AssertCompleteTwoViewCopyExecution(second.Id, "Correlation");
    }

    [Fact]
    public void ConcurrentDispatch_GlobalFactoryGivesEachExecutionItsOwnProfiler()
    {
        const int threadCount = 4;
        const int iterations = 100;
        const uint size = 32;

        using GraphicsDevice device = CreateDevice();
        using IDisposable trackedCleanup = device.TrackedCleanup();

        List<CorrelationProfiler> profilers = new();
        device.GlobalProfilers.Add(() =>
        {
            CorrelationProfiler profiler = new();
            lock (profilers)
                profilers.Add(profiler);
            return profiler;
        });

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
                    DeviceBuffer readback = device.Tracked().CreateBuffer(new BufferDescription(size * size * 16, BufferUsage.Staging));
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
        Assert.Equal(tasks.Count, profilers.Count);

        Dictionary<ulong, CorrelationProfiler> byExecution = profilers.ToDictionary(p => p.ExecutionId);
        foreach (ExecutionTask task in tasks)
            byExecution[task.Id].AssertCompleteTwoViewCopyExecution(task.Id, nameof(RenderPipeline));
    }

    [Fact]
    public void GlobalProfilers_OnlyFeedUnprofiledExecutionsTheyChooseToSample()
    {
        using GraphicsDevice device = CreateDevice();
        using IDisposable trackedCleanup = device.TrackedCleanup();

        DeviceBuffer source = device.Tracked().CreateBuffer(new BufferDescription(256, BufferUsage.StructuredBufferReadWrite));
        DeviceBuffer destination = device.Tracked().CreateBuffer(new BufferDescription(256, BufferUsage.StructuredBufferReadWrite));
        List<TimingRecorder> created = new();
        bool sample = true;
        Func<IProfiler?> factory = () =>
        {
            if (!sample)
                return null;

            TimingRecorder recorder = new();
            created.Add(recorder);
            return recorder;
        };
        device.GlobalProfilers.Add(factory);

        TimingRecorder explicitProfiler = new();
        device.RunTestGraph((context, cl) => cl.CopyBuffer(source, 0, destination, 0, 256), explicitProfiler);
        device.WaitForIdle();
        Assert.Empty(created);
        Assert.Single(explicitProfiler.ExecutionTimes);

        device.RunTestGraph((context, cl) => cl.CopyBuffer(source, 0, destination, 0, 256));
        device.WaitForIdle();
        Assert.Single(Assert.Single(created).ExecutionTimes);

        sample = false;
        device.RunTestGraph((context, cl) => cl.CopyBuffer(source, 0, destination, 0, 256));
        device.WaitForIdle();
        Assert.Single(created);

        device.GlobalProfilers.Remove(factory);
        device.RunTestGraph((context, cl) => cl.CopyBuffer(source, 0, destination, 0, 256));
        device.WaitForIdle();
        Assert.Single(created);
    }

    [Fact]
    public void SharedCapability_IsMergedAndEveryProfilerGetsTheLifecycleOnce()
    {
        TimingRecorder first = new();
        TimingRecorder second = new();
        GraphRecorder graph = new();
        using GraphicsDevice device = CreateDevice();
        using IDisposable trackedCleanup = device.TrackedCleanup();

        DeviceBuffer source = device.Tracked().CreateBuffer(new BufferDescription(256, BufferUsage.StructuredBufferReadWrite));
        DeviceBuffer destination = device.Tracked().CreateBuffer(new BufferDescription(256, BufferUsage.StructuredBufferReadWrite));

        ExecutionTask task = device.RunTestGraph((context, cl) => cl.CopyBuffer(source, 0, destination, 0, 256), first, graph, second, first);
        device.WaitForIdle();

        Assert.Single(first.ExecutionTimes);
        Assert.Single(second.ExecutionTimes);
        Assert.All(new ExecutionRecorder[] { first, second, graph }, p =>
        {
            Assert.Equal(task.Id, p.ExecutionId);
            Assert.Equal(1, p.Ended);
        });
    }

    [Fact]
    public void EndExecution_FiresOnceAfterTheFenceWithOrWithoutQueries()
    {
        ResolveOrderRecorder profiler = new();
        using GraphicsDevice device = CreateDevice();
        using IDisposable trackedCleanup = device.TrackedCleanup();

        ExecutionTask empty = device.BeginExecution(profiler);
        device.CompleteExecution(empty);
        Assert.Equal(new[] { "begin" }, profiler.Events);

        device.WaitForExecution(empty);
        Assert.Equal(new[] { "begin", "end" }, profiler.Events);

        profiler.Events.Clear();
        DeviceBuffer source = device.Tracked().CreateBuffer(new BufferDescription(256, BufferUsage.StructuredBufferReadWrite));
        DeviceBuffer destination = device.Tracked().CreateBuffer(new BufferDescription(256, BufferUsage.StructuredBufferReadWrite));
        ExecutionTask task = device.RunTestGraph((context, cl) => cl.CopyBuffer(source, 0, destination, 0, 256), profiler);
        Assert.Equal(new[] { "begin" }, profiler.Events);

        device.WaitForExecution(task);
        Assert.Equal("end", profiler.Events[^1]);
        Assert.Contains("time", profiler.Events);
        Assert.Single(profiler.Events, e => e == "end");
    }

    [Fact]
    public void ExecutionIdOverloads_PollAndWaitWithoutATask()
    {
        ResolveOrderRecorder profiler = new();
        using GraphicsDevice device = CreateDevice();
        using IDisposable trackedCleanup = device.TrackedCleanup();

        DeviceBuffer source = device.Tracked().CreateBuffer(new BufferDescription(256, BufferUsage.StructuredBufferReadWrite));
        DeviceBuffer destination = device.Tracked().CreateBuffer(new BufferDescription(256, BufferUsage.StructuredBufferReadWrite));
        ulong id = device.RunTestGraph((context, cl) => cl.CopyBuffer(source, 0, destination, 0, 256), profiler).Id;

        Assert.True(device.WaitForExecution(id));
        Assert.True(device.IsExecutionComplete(id));
        Assert.Single(profiler.Events, e => e == "end");
        Assert.Throws<ArgumentOutOfRangeException>(() => device.IsExecutionComplete(id + 1000));
    }
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanProfilerEventsTests : ProfilerEventsTests<VulkanDeviceCreator> { }
#endif
