using System;
using System.Linq;
using System.Threading;
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

file sealed class ClearPass : RasterPass
{
    private readonly RenderResourceID _target;
    private readonly ManualResetEventSlim? _entered;
    private readonly ManualResetEventSlim? _release;

    public ClearPass(string target, ManualResetEventSlim? entered = null, ManualResetEventSlim? release = null)
    {
        _target = RenderResourceID.Intern(target);
        _entered = entered;
        _release = release;
    }

    public override string Name => "Clear";

    public override void Setup(RenderContextBuilder builder)
        => SetTarget(builder, _target, GraphTextureDesc.ViewSized(PixelFormat.R8_G8_B8_A8_UNorm), ops: TargetLoadStoreOps.Clear(new Color(0, 0, 0, 255)));

    public override void Render(RenderContext context, CommandBuffer cmd)
    {
        _entered?.Set();
        _release?.Wait();
    }
}

public class RecorderTests
{
    private static GraphicsDevice CreateDevice() => GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true));

    [SkippableFact]
    public void RecordingSpanningSeveralExecutions_HoldsAllOfThem()
    {
        using GraphicsDevice device = CreateDevice();
        Recorder recorder = new(device);
        using RenderPipeline pipeline = new([new ClearPass("recorder_target")]);

        recorder.BeginRecord();
        Assert.Throws<InvalidOperationException>(recorder.BeginRecord);
        ExecutionTask first = device.DispatchGraph(pipeline, new RecordView[] { new() });
        ExecutionTask second = device.DispatchGraph(pipeline, new RecordView[] { new(), new() });
        Recording recording = recorder.EndRecord();
        recording.Wait();

        Assert.True(recording.IsDone);
        Assert.Equal([first.Id, second.Id], recording.Executions.Select(e => e.ExecutionId));
        Assert.Equal([1, 2], recording.Executions.Select(e => e.Views.Length));
        Assert.All(recording.Executions.SelectMany(e => e.Views), view => Assert.Equal("Clear", Assert.Single(view.Passes).Name));
        Assert.All(recording.Executions, e => Assert.Contains(e.CommandBuffers, c => c.Milliseconds is not null));
        Assert.Throws<InvalidOperationException>(recorder.EndRecord);
    }

    [SkippableFact]
    public void Handle_IsNotDoneBeforeTheOneStartedBeforeIt()
    {
        using GraphicsDevice device = CreateDevice();
        Recorder recorder = new(device);
        using ManualResetEventSlim entered = new();
        using ManualResetEventSlim release = new();
        using RenderPipeline blocking = new([new ClearPass("recorder_blocking", entered, release)]);
        using RenderPipeline plain = new([new ClearPass("recorder_plain")]);

        recorder.BeginRecord();
        Thread dispatcher = new(() => device.DispatchGraph(blocking, new RecordView[] { new() }));
        dispatcher.Start();
        entered.Wait();
        Recording first = recorder.EndRecord();

        recorder.BeginRecord();
        ExecutionTask task = device.DispatchGraph(plain, new RecordView[] { new() });
        Recording second = recorder.EndRecord();
        device.WaitForExecution(task);

        Assert.False(first.IsDone);
        Assert.False(second.IsDone);
        Assert.Throws<InvalidOperationException>(() => second.Executions);

        release.Set();
        dispatcher.Join();
        second.Wait();

        Assert.True(first.IsDone);
        Assert.Single(first.Executions);
        Assert.Single(second.Executions);
    }
}
