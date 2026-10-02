namespace Prowl.Graphite;

/// <summary>
/// Common GraphicsDevice config.
/// </summary>
public struct GraphicsDeviceOptions
{
    /// <summary>
    /// Enable debug features if the host supports them.
    /// </summary>
    public bool Debug;
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
    /// Run the usage-validation layer (extra correctness checks, throws on misuse). Null = enabled by default.
    /// </summary>
    public bool? EnableValidation;

    /// <summary>
    /// Profiler to report events to, or null for none. No default impl shipped - bring your own.
    /// </summary>
    public IProfiler? Profiler;

    /// <summary>
    /// Options for a device with no main Swapchain.
    /// </summary>
    /// <param name="debug">Enable debug features if host supports them.</param>
    public GraphicsDeviceOptions(bool debug)
    {
        Debug = debug;
    }
}
