using System;
using System.Collections.Generic;

using Prowl.Graphite;
using Prowl.Graphite.Bench;

using Prowl.Vector;

internal static class Program
{
    private static int Main(string[] args)
    {
        bool validation = Array.IndexOf(args, "--validation") >= 0;

        BenchProfiler profiler = new();
        GraphicsDevice gd;
        try
        {
            gd = BenchDevice.Create(profiler, validation);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to create a Vulkan device: {ex.Message}");
            return 1;
        }

        try
        {
            Console.WriteLine(BenchDevice.Describe(gd));

            using BenchScene scene = new(gd);
            SmokeRun(gd, scene, profiler, passes: 4, drawsPerPass: 64, frames: 8);
            return 0;
        }
        finally
        {
            gd.WaitForIdle();
            gd.Dispose();
        }
    }

    // Proves the graph, program, mesh and property paths all line up end to end. Replaced by the
    // real scenario set once measurement lands.
    private static void SmokeRun(GraphicsDevice gd, BenchScene scene, BenchProfiler profiler, int passes, int drawsPerPass, int frames)
    {
        PropertySet[] models = new PropertySet[drawsPerPass];
        for (int i = 0; i < drawsPerPass; i++)
            models[i] = scene.CreateModelProperties(new Float4(i / (float)drawsPerPass, 0.5f, 1f, 1f));

        using BenchPipeline pipeline = new(scene, passes, (cmd, passIndex) =>
        {
            cmd.SetShader(scene.Program);
            cmd.SetProperties(scene.ViewProperties);

            for (int i = 0; i < drawsPerPass; i++)
            {
                cmd.SetVertexSource(scene.VertexSource);
                cmd.SetProperties(models[i]);
                cmd.DrawIndexed();
            }
        });

        BenchView[] views = [new BenchView()];

        profiler.Reset();
        for (int frame = 0; frame < frames; frame++)
            gd.DispatchGraph(pipeline, views);

        gd.WaitForIdle();

        Console.WriteLine(
            $"smoke: {frames} frames x {passes} passes x {drawsPerPass} draws -> " +
            $"draws={profiler.Draws} setBinds={profiler.SetBinds} boundSets={profiler.BoundSets} " +
            $"pipelineSwitches={profiler.PipelineSwitches} submits={profiler.Submits}");
    }
}
