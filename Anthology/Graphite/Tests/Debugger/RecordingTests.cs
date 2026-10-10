using System;
using System.Linq;
using Prowl.Graphite.RenderGraph;
using Prowl.Vector;
using Xunit;

namespace Prowl.Graphite.Debugger.Tests;

file readonly struct RecordView : IRenderView
{
    public uint PixelWidth => 16;
    public uint PixelHeight => 16;
    public int ViewId => 0;
    public string Name => "RecordView";
}

file sealed class ClearPass(string target) : RasterPass
{
    private readonly RenderResourceID _target = RenderResourceID.Intern(target);

    public override string Name => "Clear";

    public override void Setup(RenderContextBuilder builder)
        => SetTarget(builder, _target, GraphTextureDesc.ViewSized(PixelFormat.R8_G8_B8_A8_UNorm), ops: TargetLoadStoreOps.Clear(new Color(0, 0, 0, 255)));

    public override void Render(RenderContext context, CommandBuffer cmd) { }
}

file sealed class ViewTargetPass : IPass
{
    public string Name => "Present";

    public void Setup(RenderContextBuilder builder) => builder.DeclareViewTarget();

    public void Render(RenderContext context, CommandBuffer cmd) { }
}

public class RecordingTests
{
    private static GraphicsDevice CreateDevice() => GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true));

    [SkippableFact]
    public void Recording_HoldsExactlyOneExecution()
    {
        using GraphicsDevice device = CreateDevice();
        using RenderPipeline pipeline = new([new ClearPass("recording_target")]) { Name = "Main" };
        Recording recording = new(device);
        Assert.Throws<InvalidOperationException>(recording.Wait);

        ExecutionTask task = device.DispatchGraph(pipeline, new RecordView[] { new(), new() }, recording);
        Assert.Throws<InvalidOperationException>(() => device.DispatchGraph(pipeline, new RecordView[] { new() }, recording));
        recording.Wait();

        Assert.Equal(task.Id, recording.ExecutionId);
        Assert.Equal("Main", recording.GraphName);
        Assert.Equal([0, 1], recording.Views.Select(v => v.Index));
        Assert.All(recording.Views, view => Assert.Equal("Clear", Assert.Single(view.Passes).Name));
        Assert.Contains(recording.CommandBuffers, c => c.Milliseconds is not null);
    }

    [SkippableFact]
    public void Recording_KeepsPassesSkippedForMissingViewTarget()
    {
        using GraphicsDevice device = CreateDevice();
        using RenderPipeline pipeline = new([new ClearPass("skipped_target"), new ViewTargetPass()]);
        Recording recording = new(device);

        device.DispatchGraph(pipeline, new RecordView[] { new() }, recording);
        recording.Wait();

        RecordedView view = Assert.Single(recording.Views);
        Assert.Equal("Clear", Assert.Single(view.Passes).Name);
        Assert.Equal(new RecordedSkippedPass("Present", PassSkipReason.NoViewTarget), Assert.Single(view.Skipped));
    }
}
