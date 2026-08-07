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

    public void Render(RenderContext<BenchView> context)
    {
        long start = Stopwatch.GetTimestamp();

        CommandBuffer cmd = context.GetCommandBuffer(Name);

        cmd.SetFramebuffer(_scene.Framebuffer);
        if (_index == 0)
            cmd.ClearColorTarget(0, Color.Black);
        cmd.SetFullViewports();

        _record(cmd, _index);

        long recorded = Stopwatch.GetTimestamp();

        context.SubmitCommandBuffer(cmd);

        long submitted = Stopwatch.GetTimestamp();

        _stats.RecordTicks += recorded - start;
        _stats.SubmitTicks += submitted - recorded;
    }
}

public sealed class BenchPresentPass : IPresentPass<BenchView>
{
    public string Name => "BenchPresent";

    public void Setup(PresentContextBuilder builder) { }

    public void Present(RenderContext<BenchView> context) { }
}

public sealed class BenchPipeline : RenderPipeline<BenchView>
{
    private readonly BenchScene _scene;
    private readonly int _passCount;
    private readonly Action<CommandBuffer, int> _record;
    private readonly BenchStats _stats;

    public BenchPipeline(BenchScene scene, int passCount, Action<CommandBuffer, int> record, BenchStats stats)
    {
        _scene = scene;
        _passCount = passCount;
        _record = record;
        _stats = stats;
    }

    protected override void InitializePasses()
    {
        for (int i = 0; i < _passCount; i++)
            AddPass(new BenchPass(_scene, i, _record, _stats));

        SetPresentPass(new BenchPresentPass());
    }
}
