namespace Prowl.Graphite;

/// <summary>
/// Common GraphicsDevice config.
/// </summary>
public struct GraphicsDeviceOptions
{
    /// <summary>
    /// Enable the Vulkan driver validation layers if installed. Default off.
    /// </summary>
    public bool VulkanValidationLayers;
    /// <summary>
    /// Prefer 0-to-1 depth range.
    /// </summary>
    public bool PreferDepthRangeZeroToOne;

    /// <summary>
    /// Max frames in flight on GPU. Must be > 0; 0 means default 3.
    /// </summary>
    public uint MaxFramesInFlight;

    /// <summary>
    /// Initial size in bytes of each per-slot transient bump-allocator buffer. 0 = default 4 MB.
    /// </summary>
    public uint TransientBufferInitialSize;

    /// <summary>
    /// Run Graphite's own usage checks, which throw on misuse. Default true; process-wide, the last device created decides for all.
    /// </summary>
    public bool GraphiteValidation = true;

    /// <summary>
    /// With <see cref="GraphiteValidation"/>, seconds the GPU may finish no work while the host waits on it before
    /// Graphite throws instead of waiting forever. 0 disables. Default 10.
    /// </summary>
    public float GpuHangSeconds = 10;

    /// <summary>
    /// Driver pipeline cache contents from an earlier <see cref="GraphicsDevice.GetPipelineCacheData"/>, so pipelines
    /// built in an earlier run build fast. The driver ignores data from another device or driver version.
    /// </summary>
    public byte[]? PipelineCacheData;

    /// <summary>
    /// Options for a device with no main Swapchain.
    /// </summary>
    /// <param name="vulkanValidationLayers">Enable the Vulkan driver validation layers if installed.</param>
    public GraphicsDeviceOptions(bool vulkanValidationLayers)
    {
        VulkanValidationLayers = vulkanValidationLayers;
    }

    public GraphicsDeviceOptions()
    {
    }
}
