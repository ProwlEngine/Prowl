using System;

using Prowl.Graphite;

namespace Prowl.Graphite.Bench;

// Headless device for the harness. Mirrors Tests/Graphite/TestUtils.CreateVulkanDevice, but with
// validation off - the validation layers sit directly in the recording path being measured.
public static class BenchDevice
{
    public static GraphicsDevice Create(BenchProfiler profiler, bool validation)
    {
        GraphicsDeviceOptions options = new(validation)
        {
            GraphiteValidation = validation
        };

        GraphicsDevice gd = GraphicsDevice.CreateVulkan(options);
        gd.GlobalProfilers.Add(() => profiler);
        return gd;
    }

    public static string Describe(GraphicsDevice gd)
        => $"{gd.VendorName} {gd.DeviceName} (Vulkan {gd.ApiVersion}), {gd.MaxExecutingTasks} frames in flight";
}
