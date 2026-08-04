using System;
using System.Diagnostics.CodeAnalysis;

namespace Prowl.Graphite;

/// <summary>
/// Base graphics device. Makes resources, runs commands.
/// </summary>
public abstract partial class GraphicsDevice : IDisposable
{
    private bool _disposed;

    internal GraphicsDevice() { }

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
    /// True = clip Y goes top(-1) to bottom(1), false = flipped.
    /// </summary>
    public abstract bool IsClipSpaceYInverted { get; }

    /// <summary>
    /// This device's resource factory.
    /// </summary>
    public abstract ResourceFactory ResourceFactory { get; }

    /// <summary>
    /// Rents a command buffer for a graph pass to record into. Pooling backends hand out a recycled reset instance; default just makes a new one.
    /// </summary>
    internal virtual CommandBuffer RentGraphCommandBuffer() => ResourceFactory.CreateCommandBuffer();

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
    /// Blocks until the fence signals, or until timeout.
    /// </summary>
    /// <param name="fence">Fence to wait on.</param>
    /// <param name="nanosecondTimeout">Max wait in nanoseconds. ulong.MaxValue = no timeout.</param>
    /// <returns>True if signaled, false if timed out.</returns>
    public abstract bool WaitForFence(Fence fence, ulong nanosecondTimeout = ulong.MaxValue);

    /// <summary>
    /// Resets the fence to unsignaled.
    /// </summary>
    /// <param name="fence">Fence to reset.</param>
    public abstract void ResetFence(Fence fence);

    /// <summary>
    /// Swaps main swapchain buffers, presents to screen. Needs a main swapchain.
    /// </summary>
    public void SwapBuffers()
    {
        if (MainSwapchain == null)
        {
            throw new RenderException("This GraphicsDevice was created without a main Swapchain, so the requested operation cannot be performed.");
        }

        SwapBuffers(MainSwapchain);
    }

    /// <summary>
    /// Swaps the buffers of the given swapchain.
    /// </summary>
    /// <param name="swapchain">Swapchain to swap and present.</param>
    public void SwapBuffers(Swapchain swapchain)
    {
        SwapBuffersCore(swapchain);
        Profiler?.RecordSwap(SwapBin.Present, 0);
    }

    private protected abstract void SwapBuffersCore(Swapchain swapchain);

    /// <summary>
    /// Main swapchain's framebuffer, or null.
    /// </summary>
    public Framebuffer? SwapchainFramebuffer => MainSwapchain?.Framebuffer;

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
    /// Maps a buffer or texture to CPU memory.
    /// </summary>
    /// <param name="resource">Buffer or texture to map.</param>
    /// <param name="mode">Map mode to use.</param>
    /// <param name="subresource">Subresource index (mip then array layer). 0 for buffers.</param>
    /// <returns>The mapped data region.</returns>
    public MappedResource Map(MappableResource resource, MapMode mode, uint subresource = 0)
    {
        Map_CheckResource(resource, mode, subresource);

        if ((mode == MapMode.Write || mode == MapMode.ReadWrite) && resource is DeviceBuffer mapBuffer)
            mapBuffer.EnsureWritable();

        MappedResource mapped = MapCore(resource, mode, subresource);
        Profiler?.Record(BufferOpBin.Map, mapped.SizeInBytes);
        return mapped;
    }

    /// <summary>
    /// Maps the resource. Backend-specific.
    /// </summary>
    /// <param name="resource">Resource to map.</param>
    /// <param name="mode">Map mode.</param>
    /// <param name="subresource">Subresource index.</param>
    /// <returns>The mapped data region.</returns>
    protected abstract MappedResource MapCore(MappableResource resource, MapMode mode, uint subresource);

    /// <summary>
    /// Maps a buffer or texture as a struct type.
    /// </summary>
    /// <param name="resource">Buffer or texture to map.</param>
    /// <param name="mode">Map mode to use.</param>
    /// <param name="subresource">Subresource index (mip then array layer).</param>
    /// <typeparam name="T">Blittable type to view the data as.</typeparam>
    /// <returns>The mapped data region.</returns>
    public MappedResourceView<T> Map<T>(MappableResource resource, MapMode mode, uint subresource = 0) where T : unmanaged
        => new(Map(resource, mode, subresource));

    /// <summary>
    /// Unmaps a previously mapped buffer or texture.
    /// </summary>
    /// <param name="resource">Resource to unmap.</param>
    /// <param name="subresource">Subresource index (mip then array layer). 0 for buffers.</param>
    public void Unmap(MappableResource resource, uint subresource = 0)
    {
        UnmapCore(resource, subresource);
        Profiler?.Record(BufferOpBin.Unmap, 0);
    }

    /// <summary>
    /// Unmaps the resource. Backend-specific.
    /// </summary>
    /// <param name="resource">Resource to unmap.</param>
    /// <param name="subresource">Subresource index.</param>
    protected abstract void UnmapCore(MappableResource resource, uint subresource);

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
    /// Fires on non-fatal warnings, like implicit buffer reallocation or hitting the transient soft cap. Writes to Console.Error by default; set null to silence, or replace to reroute.
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
        _transientTexturePool?.Dispose();
        _transientBufferPool?.Dispose();
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
