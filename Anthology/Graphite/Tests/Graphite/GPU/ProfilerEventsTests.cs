#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

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

file sealed class CorrelationProfiler : ExecutionRecorder, IGraphProfiler, IGpuStatsProfiler
{
    public readonly List<GroupInfo> GroupsBegun = new();
    public readonly List<PassInfo> PassesBegun = new();
    public readonly List<CommandBufferInfo> Timings = new();
    public int TimingsAtEnd = -1;

    public void BeginGroup(in GroupInfo group) => GroupsBegun.Add(group);
    public void EndGroup(in GroupInfo group) { }
    public void BeginPass(in PassInfo pass) => PassesBegun.Add(pass);
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
        Assert.Equal(new[] { 0, 1 }, GroupsBegun.ConvertAll(v => v.Index));
        Assert.Equal(4, PassesBegun.Count);
        Assert.Equal(4, TimingsAtEnd);
        foreach (int viewIndex in new[] { 0, 1 })
        {
            foreach (string name in new[] { "ProfilerClear", "ProfilerCopy" })
            {
                CommandBufferInfo info = Assert.Single(Timings, t => t.Pass!.Value.GroupIndex == viewIndex && t.Pass!.Value.Name == name);
                Assert.Equal(name, info.Name);
            }
        }
    }
}

file static class ProfilerPasses
{
    private static readonly TargetLoadStoreOps ClearBlack = TargetLoadStoreOps.Clear(new Color(0f, 0f, 0f, 1f));

    public static TextureHandle AddClear(FrameGraph graph, RenderResourceID id, uint size, int scope = 0)
    {
        TextureHandle target = graph.Create(id, GraphTextureDesc.Sized((int)size, (int)size, PixelFormat.R32_G32_B32_A32_Float), scope: scope);
        TextureHandle written = target;
        graph.AddPass("ProfilerClear", pass => written = pass.Write(target, firstWrite: ClearBlack), _ => { });
        return written;
    }

    public static void AddCopy(FrameGraph graph, TextureHandle target, DeviceBuffer readback)
        => graph.AddPass("ProfilerCopy", pass => pass.Read(target, TextureState.TransferSrc), context =>
        {
            RenderTexture texture = context.Get(target);
            context.Command.CopyTextureToBuffer(texture.ColorTextures[0], readback, 0, TextureRegion.Whole(texture.ColorTextures[0]));
        });

    public static void AddViews(FrameGraph graph, RenderResourceID id, uint size, DeviceBuffer readback)
    {
        for (int view = 0; view < 2; view++)
        {
            using (graph.Group("View " + view))
                AddCopy(graph, AddClear(graph, id, size, view), readback);
        }
    }
}

public abstract class ProfilerEventsTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    private GraphicsDevice CreateDevice() => GD.BackendType switch
    {
        GraphicsBackend.Vulkan => GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true)),
        _ => throw new NotSupportedException(),
    };

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
                    using FrameGraph graph = new(device, "Concurrent");

                    start.SignalAndWait();
                    for (int i = 0; i < iterations; i++)
                    {
                        ProfilerPasses.AddViews(graph, id, size, readback);
                        ExecutionTask task = graph.Execute();
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
            byExecution[task.Id].AssertCompleteTwoViewCopyExecution(task.Id, "Concurrent");

        CorrelationProfiler firstProfiler = new();
        CorrelationProfiler secondProfiler = new();
        using GraphicsDevice pairDevice = CreateDevice();
        using IDisposable pairCleanup = pairDevice.TrackedCleanup();

        const uint pairSize = 64;
        DeviceBuffer pairReadback = pairDevice.Tracked().CreateBuffer(new BufferDescription(pairSize * pairSize * 16, BufferUsage.Staging));

        RenderResourceID pairId = RenderResourceID.Intern("profiler_correlation_target");
        using FrameGraph pairGraph = new(pairDevice, "Correlation");
        ProfilerPasses.AddViews(pairGraph, pairId, pairSize, pairReadback);
        ExecutionTask first = pairGraph.Execute(firstProfiler);
        ProfilerPasses.AddViews(pairGraph, pairId, pairSize, pairReadback);
        ExecutionTask second = pairGraph.Execute(secondProfiler);
        pairDevice.WaitForIdle();

        Assert.True(first.Id < second.Id);
        firstProfiler.AssertCompleteTwoViewCopyExecution(first.Id, "Correlation");
        secondProfiler.AssertCompleteTwoViewCopyExecution(second.Id, "Correlation");
    }
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanProfilerEventsTests : ProfilerEventsTests<VulkanDeviceCreator> { }
#endif
