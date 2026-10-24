using System;
using System.Linq;
using Prowl.Vector;
using Xunit;

namespace Prowl.Graphite.Debugger.Tests;

file static class RecordPasses
{
    public static void AddViews(FrameGraph graph, int count)
    {
        using (graph.Group("World"))
        {
            for (int i = 0; i < count; i++)
            {
                using (graph.Group("View " + i))
                {
                    TextureHandle target = graph.Create("recording_target", GraphTextureDesc.Sized(16, 16, PixelFormat.R8_G8_B8_A8_UNorm), scope: i);
                    graph.AddPass("Clear", pass => pass.Write(target, firstWrite: TargetLoadStoreOps.Clear(Color.FromBytes(0, 0, 0, 255))), _ => { });
                }
            }
        }
    }
}

[Collection("GPU Tests")]
public class RecordingTests
{
    private static GraphicsDevice CreateDevice() => GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true));

    [SkippableFact]
    public void Recording_HoldsExactlyOneExecutionWithItsGroupTree()
    {
        using GraphicsDevice device = CreateDevice();
        using FrameGraph graph = new(device, "Main");
        Recording recording = new(device);
        Assert.Throws<InvalidOperationException>(recording.Wait);

        RecordPasses.AddViews(graph, 2);
        ExecutionTask task = graph.Execute(recording);
        RecordPasses.AddViews(graph, 1);
        Assert.Throws<InvalidOperationException>(() => graph.Execute(recording));
        recording.Wait();

        Assert.Equal(task.Id, recording.ExecutionId);
        Assert.Equal("Main", recording.GraphName);
        Assert.Equal(["World", "View 0", "View 1"], recording.Groups.Select(g => g.Name));
        Assert.Equal([-1, 0, 0], recording.Groups.Select(g => g.ParentIndex));
        Assert.All(recording.Groups.Skip(1), view => Assert.Equal("Clear", Assert.Single(view.Passes).Name));
        Assert.Contains(recording.CommandBuffers, c => c.Milliseconds is not null);
    }
}
