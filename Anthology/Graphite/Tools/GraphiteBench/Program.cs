using System;

using Prowl.Graphite;
using Prowl.Graphite.Bench;

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
            return 0;
        }
        finally
        {
            gd.WaitForIdle();
            gd.Dispose();
        }
    }
}
