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


public sealed class BenchPass : IPass<BenchView>
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

    public void Render(RenderContext<BenchView> context, CommandBuffer cmd)
    {
        long start = Stopwatch.GetTimestamp();


        cmd.SetFramebuffer(_scene.Framebuffer);
        if (_index == 0)
            cmd.ClearColorTarget(0, Color.Black);
        cmd.SetFullViewport();

        _record(cmd, _index);

        long recorded = Stopwatch.GetTimestamp();


        long submitted = Stopwatch.GetTimestamp();

        _stats.RecordTicks += recorded - start;
        _stats.SubmitTicks += submitted - recorded;
    }
}
