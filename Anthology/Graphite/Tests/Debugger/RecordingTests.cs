using System;
using System.Linq;
using Prowl.Vector;
using Xunit;

namespace Prowl.Graphite.Debugger.Tests;

file static class RecordKinds
{
    public static readonly GraphKind World = GraphKind.Intern("Test.World");
    public static readonly GraphKind View = GraphKind.Intern("Test.RecordView");
    public static readonly GraphScope[] Scopes = [new GraphScope(World, GraphKind.Root), new GraphScope(View, World)];
}

file sealed class ClearPass(string target) : InstanceRasterPass
{
    private readonly RenderResourceID _target = RenderResourceID.Intern(target);

    public override string Name => "Clear";

    public override GraphKind Kind => RecordKinds.View;

    public override void Setup(InstanceContextBuilder builder)
        => SetTarget(builder, _target, GraphTextureDesc.InstanceSized(PixelFormat.R8_G8_B8_A8_UNorm), ops: TargetLoadStoreOps.Clear(new Color(0, 0, 0, 255)));

    public override void Render(InstanceContext context, CommandBuffer cmd) { }
}

file sealed class InstanceTargetPass : IInstancePass
{
    public string Name => "Present";

    public GraphKind Kind => RecordKinds.View;

    public void Setup(InstanceContextBuilder builder) => builder.DeclareInstanceTarget();

    public void Render(InstanceContext context, CommandBuffer cmd) { }
}

[Collection("GPU Tests")]
public class RecordingTests
{
    private static GraphicsDevice CreateDevice() => GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true));

    private static GraphInstances Views(RenderGraph graph, int count)
    {
        GraphInstances instances = graph.CreateInstances();
        GraphInstance world = instances.Add(RecordKinds.World, 7, name: "World");
        for (int i = 0; i < count; i++)
            instances.Add(RecordKinds.View, 10 + i, world, new GraphExtent(16, 16));
        return instances;
    }

    [SkippableFact]
    public void Recording_HoldsExactlyOneExecutionWithItsInstanceTree()
    {
        using GraphicsDevice device = CreateDevice();
        using RenderGraph graph = RenderGraph.Build([new ClearPass("recording_target")], RecordKinds.Scopes, "Main");
        Recording recording = new(device);
        Assert.Throws<InvalidOperationException>(recording.Wait);

        ExecutionTask task = device.DispatchGraph(graph, Views(graph, 2), recording);
        Assert.Throws<InvalidOperationException>(() => device.DispatchGraph(graph, Views(graph, 1), recording));
        recording.Wait();

        Assert.Equal(task.Id, recording.ExecutionId);
        Assert.Equal("Main", recording.GraphName);
        Assert.Equal([-1, 0, 1, 1], recording.Instances.Select(i => i.ParentIndex));
        Assert.Equal(new GraphInstanceKey(RecordKinds.World, 7), recording.Instances[1].Key);
        Assert.Equal([10, 11], recording.Instances.Where(i => i.Key.Kind == RecordKinds.View).Select(i => i.Key.Id));
        Assert.All(recording.Instances.Skip(2), view => Assert.Equal("Clear", Assert.Single(view.Passes).Name));
        Assert.Contains(recording.CommandBuffers, c => c.Milliseconds is not null);
    }

    [SkippableFact]
    public void Recording_KeepsPassesSkippedForMissingInstanceTarget()
    {
        using GraphicsDevice device = CreateDevice();
        using RenderGraph graph = RenderGraph.Build([new ClearPass("skipped_target"), new InstanceTargetPass()], RecordKinds.Scopes);
        Recording recording = new(device);

        device.DispatchGraph(graph, Views(graph, 1), recording);
        recording.Wait();

        RecordedInstance view = recording.Instances.Single(i => i.Key.Kind == RecordKinds.View);
        Assert.Equal("Clear", Assert.Single(view.Passes).Name);
        Assert.Equal(new RecordedSkippedPass("Present", PassSkipReason.NoTarget), Assert.Single(view.Skipped));
    }
}
