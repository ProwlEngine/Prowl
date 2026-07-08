namespace Prowl.Graphite;

/// <summary>
/// A structure describing several common properties of a GraphicsDevice.
/// </summary>
public struct GraphicsDeviceOptions
{
    /// <summary>
    /// Indicates whether the GraphicsDevice will support debug features, provided they are supported by the host system.
    /// </summary>
    public bool Debug;
    /// <summary>
    /// Indicates whether the Graphicsdevice will include a "main" Swapchain. If this value is true, then the GraphicsDevice
    /// must be created with one of the overloads that provides Swapchain source information.
    /// </summary>
    public bool HasMainSwapchain;
    /// <summary>
    /// An optional <see cref="PixelFormat"/> to be used for the depth buffer of the swapchain. If this value is null, then
    /// no depth buffer will be present on the swapchain.
    /// </summary>
    public PixelFormat? SwapchainDepthFormat;
    /// <summary>
    /// Indicates whether the main Swapchain will be synchronized to the window system's vertical refresh rate.
    /// </summary>
    public bool SyncToVerticalBlank;
    /// <summary>
    /// Indicates whether a 0-to-1 depth range mapping is preferred.
    /// </summary>
    public bool PreferDepthRangeZeroToOne;
    /// <summary>
    /// Indicates whether a bottom-to-top-increasing clip space Y direction is preferred. For Vulkan, this is not the
    /// default, and may not be available on all systems.
    /// </summary>
    public bool PreferStandardClipSpaceYDirection;
    /// <summary>
    /// Indicates whether the main Swapchain should use an sRGB format. This value is only used in cases where the properties
    /// of the main SwapChain are not explicitly specified with a <see cref="SwapchainDescription"/>. If they are, then the
    /// value of <see cref="SwapchainDescription.ColorSrgb"/> will supercede the value specified here.
    /// </summary>
    public bool SwapchainSrgbFormat;

    /// <summary>
    /// The maximum number of frames that may be simultaneously in flight on the GPU.
    /// Must be greater than zero; if 0, a default of 3 is used at device creation.
    /// </summary>
    public uint MaxFramesInFlight;

    /// <summary>
    /// The initial size in bytes of each per-slot transient bump-allocator buffer.
    /// If 0, the device defaults to 4 MB.
    /// </summary>
    public uint TransientBufferInitialSize;

    /// <summary>
    /// A soft cap in bytes for the total transient memory allocated in a single frame.
    /// Exceeding this limit logs a one-shot warning per device. If 0, the device defaults to 64 MB.
    /// </summary>
    public uint TransientBufferSoftCapBytes;

    /// <summary>
    /// A hard cap in bytes for the total transient memory allocated in a single frame.
    /// Exceeding this limit throws a <see cref="RenderException"/>. If 0, the device defaults to 256 MB.
    /// </summary>
    public uint TransientBufferHardCapBytes;

    /// <summary>
    /// Indicates whether the GraphicsDevice runs its usage-validation layer, which performs extra
    /// correctness checks on API calls and throws a <see cref="RenderException"/> on misuse.
    /// If null, the device defaults to enabled.
    /// </summary>
    public bool? EnableValidation;

    /// <summary>
    /// Indicates whether the GraphicsDevice runs its profiling layer, which records per-frame
    /// allocation and data-transfer counters readable through <see cref="GraphicsDevice.GetProfile"/>.
    /// If null, the device defaults to disabled.
    /// </summary>
    public bool? EnableProfiling;

    /// <summary>
    /// Constructs a new GraphicsDeviceOptions for a device with no main Swapchain.
    /// </summary>
    /// <param name="debug">Indicates whether the GraphicsDevice will support debug features, provided they are supported by
    /// the host system.</param>
    public GraphicsDeviceOptions(bool debug)
    {
        Debug = debug;
        HasMainSwapchain = false;
        SwapchainDepthFormat = null;
        SyncToVerticalBlank = false;
        PreferDepthRangeZeroToOne = false;
        PreferStandardClipSpaceYDirection = false;
        SwapchainSrgbFormat = false;
    }

    /// <summary>
    /// Constructs a new GraphicsDeviceOptions for a device with a main Swapchain.
    /// </summary>
    /// <param name="debug">Indicates whether the GraphicsDevice will enable debug features, provided they are supported by
    /// the host system.</param>
    /// <param name="swapchainDepthFormat">An optional <see cref="PixelFormat"/> to be used for the depth buffer of the
    /// swapchain. If this value is null, then no depth buffer will be present on the swapchain.</param>
    /// <param name="syncToVerticalBlank">Indicates whether the main Swapchain will be synchronized to the window system's
    /// vertical refresh rate.</param>
    public GraphicsDeviceOptions(bool debug, PixelFormat? swapchainDepthFormat, bool syncToVerticalBlank)
    {
        Debug = debug;
        HasMainSwapchain = true;
        SwapchainDepthFormat = swapchainDepthFormat;
        SyncToVerticalBlank = syncToVerticalBlank;
        PreferDepthRangeZeroToOne = false;
        PreferStandardClipSpaceYDirection = false;
        SwapchainSrgbFormat = false;
    }

    /// <summary>
    /// Constructs a new GraphicsDeviceOptions for a device with a main Swapchain.
    /// </summary>
    /// <param name="debug">Indicates whether the GraphicsDevice will enable debug features, provided they are supported by
    /// the host system.</param>
    /// <param name="swapchainDepthFormat">An optional <see cref="PixelFormat"/> to be used for the depth buffer of the
    /// swapchain. If this value is null, then no depth buffer will be present on the swapchain.</param>
    /// <param name="syncToVerticalBlank">Indicates whether the main Swapchain will be synchronized to the window system's
    /// vertical refresh rate.</param>
    /// <param name="preferDepthRangeZeroToOne">Indicates whether a 0-to-1 depth range mapping is preferred.</param>
    public GraphicsDeviceOptions(
        bool debug,
        PixelFormat? swapchainDepthFormat,
        bool syncToVerticalBlank,
        bool preferDepthRangeZeroToOne)
    {
        Debug = debug;
        HasMainSwapchain = true;
        SwapchainDepthFormat = swapchainDepthFormat;
        SyncToVerticalBlank = syncToVerticalBlank;
        PreferDepthRangeZeroToOne = preferDepthRangeZeroToOne;
        PreferStandardClipSpaceYDirection = false;
        SwapchainSrgbFormat = false;
    }

    /// <summary>
    /// Constructs a new GraphicsDeviceOptions for a device with a main Swapchain.
    /// </summary>
    /// <param name="debug">Indicates whether the GraphicsDevice will enable debug features, provided they are supported by
    /// the host system.</param>
    /// <param name="swapchainDepthFormat">An optional <see cref="PixelFormat"/> to be used for the depth buffer of the
    /// swapchain. If this value is null, then no depth buffer will be present on the swapchain.</param>
    /// <param name="syncToVerticalBlank">Indicates whether the main Swapchain will be synchronized to the window system's
    /// vertical refresh rate.</param>
    /// <param name="preferDepthRangeZeroToOne">Indicates whether a 0-to-1 depth range mapping is preferred.</param>
    /// <param name="preferStandardClipSpaceYDirection">Indicates whether a bottom-to-top-increasing clip space Y direction
    /// is preferred. For Vulkan, this is not the default, and is not available on all systems.</param>
    public GraphicsDeviceOptions(
        bool debug,
        PixelFormat? swapchainDepthFormat,
        bool syncToVerticalBlank,
        bool preferDepthRangeZeroToOne,
        bool preferStandardClipSpaceYDirection)
    {
        Debug = debug;
        HasMainSwapchain = true;
        SwapchainDepthFormat = swapchainDepthFormat;
        SyncToVerticalBlank = syncToVerticalBlank;
        PreferDepthRangeZeroToOne = preferDepthRangeZeroToOne;
        PreferStandardClipSpaceYDirection = preferStandardClipSpaceYDirection;
        SwapchainSrgbFormat = false;
    }

    /// <summary>
    /// Constructs a new GraphicsDeviceOptions for a device with a main Swapchain.
    /// </summary>
    /// <param name="debug">Indicates whether the GraphicsDevice will enable debug features, provided they are supported by
    /// the host system.</param>
    /// <param name="swapchainDepthFormat">An optional <see cref="PixelFormat"/> to be used for the depth buffer of the
    /// swapchain. If this value is null, then no depth buffer will be present on the swapchain.</param>
    /// <param name="syncToVerticalBlank">Indicates whether the main Swapchain will be synchronized to the window system's
    /// vertical refresh rate.</param>
    /// <param name="preferDepthRangeZeroToOne">Indicates whether a 0-to-1 depth range mapping is preferred.</param>
    /// <param name="preferStandardClipSpaceYDirection">Indicates whether a bottom-to-top-increasing clip space Y direction
    /// is preferred. For Vulkan, this is not the default, and is not available on all systems.</param>
    /// <param name="swapchainSrgbFormat">Indicates whether the main Swapchain should use an sRGB format. This value is only
    /// used in cases where the properties of the main SwapChain are not explicitly specified with a
    /// <see cref="SwapchainDescription"/>. If they are, then the value of <see cref="SwapchainDescription.ColorSrgb"/> will
    /// supercede the value specified here.</param>
    public GraphicsDeviceOptions(
        bool debug,
        PixelFormat? swapchainDepthFormat,
        bool syncToVerticalBlank,
        bool preferDepthRangeZeroToOne,
        bool preferStandardClipSpaceYDirection,
        bool swapchainSrgbFormat)
    {
        Debug = debug;
        HasMainSwapchain = true;
        SwapchainDepthFormat = swapchainDepthFormat;
        SyncToVerticalBlank = syncToVerticalBlank;
        PreferDepthRangeZeroToOne = preferDepthRangeZeroToOne;
        PreferStandardClipSpaceYDirection = preferStandardClipSpaceYDirection;
        SwapchainSrgbFormat = swapchainSrgbFormat;
    }
}
