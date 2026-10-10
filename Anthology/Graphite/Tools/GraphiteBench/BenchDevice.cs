using System;

using Prowl.Graphite;

namespace Prowl.Graphite.Bench;

public static class BenchDevice
{
    public static bool Validation { get; set; }

    public static GraphicsDevice Create(BenchProfiler profiler)
    {
        GraphicsDeviceOptions options = new(Validation)
        {
            GraphiteValidation = Validation
        };

        GraphicsDevice gd = GraphicsDevice.CreateVulkan(options);
        gd.GlobalProfilers.Add(() => profiler);
        return gd;
    }
}
