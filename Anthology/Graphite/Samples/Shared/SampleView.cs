using System;
using Prowl.Vector;

namespace Prowl.Graphite.Samples;


public static class SampleView
{
    private static readonly Color ClearColor = new(0.10f, 0.12f, 0.16f, 1.0f);

    public static ExecutionTask Draw(FrameGraph graph, Swapchain swapchain, string name, Action<CommandBuffer> draw)
    {
        TextureHandle target = graph.ImportTarget(swapchain.Framebuffer, PixelFormat.D24_UNorm_S8_UInt);
        graph.AddPass(name, pass => pass.Write(target, firstWrite: TargetLoadStoreOps.Clear(ClearColor)), context => draw(context.Command));
        return graph.Execute();
    }
}
