using System;

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

    public string Name { get; }

    public BenchPass(BenchScene scene, int index, Action<CommandBuffer, int> record)
    {
        _scene = scene;
        _index = index;
        _record = record;
        Name = $"BenchPass{index}";
    }

    public void Setup(RenderContextBuilder builder) { }

    public void Render(RenderContext<BenchView> context)
    {
        CommandBuffer cmd = context.GetCommandBuffer(Name);

        cmd.SetFramebuffer(_scene.Framebuffer);
        if (_index == 0)
            cmd.ClearColorTarget(0, Color.Black);
        cmd.SetFullViewports();

        _record(cmd, _index);

        context.SubmitCommandBuffer(cmd);
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

    public BenchPipeline(BenchScene scene, int passCount, Action<CommandBuffer, int> record)
    {
        _scene = scene;
        _passCount = passCount;
        _record = record;
    }

    protected override void InitializePasses()
    {
        for (int i = 0; i < _passCount; i++)
            AddPass(new BenchPass(_scene, i, _record));

        SetPresentPass(new BenchPresentPass());
    }
}
