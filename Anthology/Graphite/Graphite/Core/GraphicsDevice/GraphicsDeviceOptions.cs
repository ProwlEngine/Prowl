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
    /// Prefer bottom-to-top clip space Y. Not default on Vulkan, not always available.
    /// </summary>
    public bool PreferStandardClipSpaceYDirection;

    /// <summary>
    /// Max frames in flight on GPU. Must be > 0; 0 means default 3.
    /// </summary>
    public uint MaxFramesInFlight;

    /// <summary>
    /// Initial size in bytes of each per-slot transient bump-allocator buffer. 0 = default 4 MB.
    /// </summary>
    public uint TransientBufferInitialSize;

    /// <summary>
    /// Soft cap in bytes for total transient memory per frame. Over this logs a one-shot warning. 0 = default 64 MB.
    /// </summary>
    public uint TransientBufferSoftCapBytes;

    /// <summary>
    /// Hard cap in bytes for total transient memory per frame. Over this throws. 0 = default 256 MB.
    /// </summary>
    public uint TransientBufferHardCapBytes;

    /// <summary>
    /// Run Graphite's own usage checks, which throw on misuse. Default true; process-wide, the last device created decides for all.
    /// </summary>
    public bool GraphiteValidation = true;

    /// <summary>
    /// Profiler to report events to, or null for none. No default impl shipped - bring your own.
    /// </summary>
    public IProfiler? Profiler;

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
