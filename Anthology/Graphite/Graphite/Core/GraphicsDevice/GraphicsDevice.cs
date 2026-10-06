using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Prowl.Graphite;

/// <summary>
/// Base graphics device. Makes resources, runs commands.
/// </summary>
public abstract partial class GraphicsDevice : IDisposable
{
    private bool _disposed;

    internal PixelFormat? ResolvedDepthFormat;

    /// <summary>
    /// Device name.
    /// </summary>
    public abstract string DeviceName { get; }

    /// <summary>
    /// Device vendor name.
    /// </summary>
    public abstract string VendorName { get; }

    /// <summary>
    /// Backend API version.
    /// </summary>
    public abstract GraphicsApiVersion ApiVersion { get; }

    /// <summary>
    /// Which graphics API this is.
    /// </summary>
    public abstract GraphicsBackend BackendType { get; }

    /// <summary>
    /// True = texture origin top-left, false = bottom-left. Matters for framebuffer sampling.
    /// </summary>
    public abstract bool IsUvOriginTopLeft { get; }

    /// <summary>
    /// True = depth range 0-1, false = -1 to 1.
    /// </summary>
    public abstract bool IsDepthRangeZeroToOne { get; }

    /// <summary>
    /// This device's resource factory.
    /// </summary>
    public abstract ResourceFactory ResourceFactory { get; }

    /// <summary>
    /// Rents a command buffer for a graph pass to record into.
    /// </summary>
    internal abstract CommandBuffer RentGraphCommandBuffer(ExecutionTask task);

    /// <summary>
    /// Main swapchain for this device, or null if none.
    /// </summary>
    public abstract Swapchain MainSwapchain { get; }

    /// <summary>
    /// Optional features this device supports.
    /// </summary>
    public abstract GraphicsDeviceFeatures Features { get; }

    /// <summary>
    /// Vsync on the main swapchain. Setter needs a main swapchain.
    /// </summary>
    public virtual bool SyncToVerticalBlank
    {
        get => MainSwapchain?.SyncToVerticalBlank ?? false;
        set
        {
            SyncToVerticalBlank_CheckMainSwapchain();
            MainSwapchain.SyncToVerticalBlank = value;
        }
    }

    /// <summary>
    /// Uniform buffer offset alignment, bytes. Offsets must be a multiple of this.
    /// </summary>
    public uint UniformBufferMinOffsetAlignment => GetUniformBufferMinOffsetAlignmentCore();

    /// <summary>
    /// Structured buffer offset alignment, bytes. Offsets must be a multiple of this.
    /// </summary>
    public uint StructuredBufferMinOffsetAlignment => GetStructuredBufferMinOffsetAlignmentCore();

    internal abstract uint GetUniformBufferMinOffsetAlignmentCore();
    internal abstract uint GetStructuredBufferMinOffsetAlignmentCore();

    /// <summary>
    /// Swaps the buffers of the given swapchain.
    /// </summary>
    /// <param name="swapchain">Swapchain to swap and present.</param>
    public void SwapBuffers(Swapchain swapchain)
    {
        SwapBuffersCore(swapchain);
        Counters.RecordSwap(SwapBin.Present);
    }

    private protected abstract void SwapBuffersCore(Swapchain swapchain);

    /// <summary>
    /// Tells the device the main window resized; recreates the swapchain framebuffer. Needs a main swapchain.
    /// </summary>
    /// <param name="width">New window width.</param>
    /// <param name="height">New window height.</param>
    public void ResizeMainWindow(uint width, uint height)
    {
        if (MainSwapchain == null)
        {
            throw new RenderException("This GraphicsDevice was created without a main Swapchain, so the requested operation cannot be performed.");
        }

        MainSwapchain.Resize(width, height);
    }

    /// <summary>
    /// Max sample count this pixel format supports.
    /// </summary>
    /// <param name="format">Format to check.</param>
    /// <param name="depthFormat">Whether it's for a depth texture.</param>
    /// <returns>Max sample count a texture of that format can use.</returns>
    public abstract TextureSampleCount GetSampleCountLimit(PixelFormat format, bool depthFormat);

    /// <summary>
    /// Maps a Dynamic or Staging buffer and returns its bytes. Call Unmap when done.
    /// </summary>
    public unsafe Span<byte> Map(DeviceBuffer buffer)
    {
        Map_CheckResource(buffer);
        buffer.MarkContentChanged();
        IntPtr data = MapCore(buffer);
        Counters.RecordBufferOp(BufferOpBin.Map, buffer.SizeInBytes);
        return new Span<byte>((void*)data, (int)buffer.SizeInBytes);
    }

    /// <summary>
    /// Maps a Dynamic or Staging buffer as a span of T. Call Unmap when done.
    /// </summary>
    public Span<T> Map<T>(DeviceBuffer buffer) where T : unmanaged
        => MemoryMarshal.Cast<byte, T>(Map(buffer));

    /// <summary>
    /// Unmaps a buffer mapped with Map.
    /// </summary>
    public void Unmap(DeviceBuffer buffer)
    {
        UnmapCore(buffer);
        Counters.RecordBufferOp(BufferOpBin.Unmap, 0);
    }

    /// <summary>
    /// Maps the buffer. Backend-specific.
    /// </summary>
    protected abstract IntPtr MapCore(DeviceBuffer buffer);

    /// <summary>
    /// Unmaps the buffer. Backend-specific.
    /// </summary>
    protected abstract void UnmapCore(DeviceBuffer buffer);

    /// <summary>
    /// Whether this format/type/usage combo is supported, plus its device limits.
    /// </summary>
    /// <param name="format">Pixel format to check.</param>
    /// <param name="type">Texture type to check.</param>
    /// <param name="usage">Texture usage to check.</param>
    /// <param name="properties">If supported, the limits for a texture made with this combo.</param>
    /// <returns>True if supported, with properties filled in.</returns>
    public bool GetPixelFormatSupport(
        PixelFormat format,
        TextureType type,
        TextureUsage usage,
        out PixelFormatProperties properties)
    {
        return GetPixelFormatSupportCore(format, type, usage, out properties);
    }

    /// <summary>
    /// Whether this format/type/usage combo is supported.
    /// </summary>
    /// <param name="format">Pixel format to check.</param>
    /// <param name="type">Texture type to check.</param>
    /// <param name="usage">Texture usage to check.</param>
    /// <returns>True if supported.</returns>
    public bool GetPixelFormatSupport(PixelFormat format, TextureType type, TextureUsage usage)
        => GetPixelFormatSupportCore(format, type, usage, out _);

    private protected abstract bool GetPixelFormatSupportCore(
        PixelFormat format,
        TextureType type,
        TextureUsage usage,
        out PixelFormatProperties properties);

    /// <summary>
    /// Fires at draw/dispatch when a reflected resource slot has no match and gets a default instead. Null (silent) by default.
    /// </summary>
    public MissingPropertyHandler? OnMissingProperty { get; set; }

    /// <summary>
    /// Fires on non-fatal warnings, like implicit buffer reallocation. Writes to Console.Error by default; set null to silence, or replace to reroute.
    /// </summary>
    public GraphicsDeviceWarningHandler? OnWarning { get; set; } = message => Console.Error.WriteLine(message);

    /// <summary>
    /// Backend-specific disposal of this device's resources.
    /// </summary>
    protected abstract void PlatformDispose();

    /// <summary>
    /// True if this device has been disposed.
    /// </summary>
    public bool IsDisposed => _disposed;

    /// <summary>
    /// Frees this device's unmanaged resources. Child resources must already be disposed.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        WaitForIdle();
        _transientTexturePool.Dispose();
        _transientBufferPool.Dispose();
        DisposeDefaultResources();
        PlatformDispose();
    }

#if !EXCLUDE_VULKAN_BACKEND
    /// <summary>
    /// Tries to get Vulkan backend info. Only works on a Vulkan device.
    /// </summary>
    /// <param name="info">Vulkan backend info if successful.</param>
    /// <returns>True if this is a Vulkan device and it worked.</returns>
    public virtual bool GetVulkanInfo([NotNullWhen(true)] out BackendInfoVulkan? info)
    {
        info = null;
        return false;
    }

    /// <summary>
    /// Gets Vulkan backend info. Only works on a Vulkan device, throws otherwise.
    /// </summary>
    /// <returns>Vulkan backend info for this device.</returns>
    public BackendInfoVulkan GetVulkanInfo()
    {
        if (!GetVulkanInfo(out BackendInfoVulkan? info))
            throw new RenderException($"{nameof(GetVulkanInfo)} can only be used on a Vulkan GraphicsDevice.");

        return info;
    }
#endif
}
