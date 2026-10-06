using System;
using System.Diagnostics;

using Prowl.Graphite;
using Prowl.Graphite.RenderGraph;

using Prowl.Vector;

namespace Prowl.Graphite.Bench;

public readonly struct BenchView : IRenderView
{
    public uint PixelWidth => BenchScene.TargetWidth;
    public uint PixelHeight => BenchScene.TargetHeight;
    public int ViewId => 0;
}


public sealed class BenchPass : IPass
{
    private readonly BenchScene _scene;
    private readonly int _index;
    private readonly Action<CommandBuffer, int> _record;
    private readonly BenchStats _stats;

    public string Name { get; }

    public BenchPass(BenchScene scene, int index, Action<CommandBuffer, int> record, BenchStats stats)
    {
        _scene = scene;
        _index = index;
        _record = record;
        _stats = stats;
        Name = $"BenchPass{index}";
    }

    public void Setup(RenderContextBuilder builder) { }

    public void Render(RenderContext context, CommandBuffer cmd)
    {
        long start = Stopwatch.GetTimestamp();


        cmd.SetFramebuffer(_scene.Framebuffer, _index == 0 ? new TargetLoadStoreOps(AttachmentOps.Clear(Color.Black), AttachmentOps.Loaded) : null);
        cmd.SetFullViewport();

        _record(cmd, _index);

        long recorded = Stopwatch.GetTimestamp();


        long submitted = Stopwatch.GetTimestamp();

        _stats.RecordTicks += recorded - start;
        _stats.SubmitTicks += submitted - recorded;
    }
}
