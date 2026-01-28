// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Runtime.Graphite;

/// <summary>
/// Options for creating a graphics device.
/// </summary>
public struct GraphiteDeviceOptions
{
    /// <summary>Enable debug/validation layers.</summary>
    public bool EnableDebugLayer;

    /// <summary>Enable API validation (slower but catches errors).</summary>
    public bool EnableValidation;

    /// <summary>Prefer discrete GPU over integrated.</summary>
    public bool PreferDiscreteGPU;

    /// <summary>Maximum frames in flight for frame buffering (Vulkan).</summary>
    public int MaxFramesInFlight;

    public GraphiteDeviceOptions()
    {
        EnableDebugLayer = false;
        EnableValidation = false;
        PreferDiscreteGPU = true;
        MaxFramesInFlight = 2;
    }

    public static GraphiteDeviceOptions Default => new();

    public static GraphiteDeviceOptions Debug => new()
    {
        EnableDebugLayer = true,
        EnableValidation = true,
    };
}

/// <summary>
/// Describes the capabilities of a graphics device.
/// </summary>
public struct DeviceCapabilities
{
    /// <summary>Device/GPU name.</summary>
    public string DeviceName;

    /// <summary>Vendor name.</summary>
    public string VendorName;

    /// <summary>Whether compute shaders are supported.</summary>
    public bool SupportsCompute;

    /// <summary>Whether geometry shaders are supported.</summary>
    public bool SupportsGeometryShaders;

    /// <summary>Whether tessellation is supported.</summary>
    public bool SupportsTessellation;

    /// <summary>Whether multi-draw indirect is supported.</summary>
    public bool SupportsMultiDrawIndirect;

    /// <summary>Whether bindless resources are supported.</summary>
    public bool SupportsBindless;

    /// <summary>Maximum texture size in any dimension.</summary>
    public uint MaxTextureSize;

    /// <summary>Maximum uniform buffer size.</summary>
    public uint MaxUniformBufferSize;

    /// <summary>Maximum storage buffer size.</summary>
    public uint MaxStorageBufferSize;

    /// <summary>Maximum number of bind groups per pipeline.</summary>
    public uint MaxBindGroups;

    /// <summary>Maximum number of samplers per shader stage.</summary>
    public uint MaxSamplersPerStage;

    /// <summary>Maximum number of textures per shader stage.</summary>
    public uint MaxTexturesPerStage;

    /// <summary>Maximum vertex attributes.</summary>
    public uint MaxVertexAttributes;

    /// <summary>Maximum vertex buffer bindings.</summary>
    public uint MaxVertexBuffers;

    /// <summary>Maximum color attachments for a render pass.</summary>
    public uint MaxColorAttachments;

    /// <summary>Maximum compute workgroup size (X).</summary>
    public uint MaxComputeWorkgroupSizeX;

    /// <summary>Maximum compute workgroup size (Y).</summary>
    public uint MaxComputeWorkgroupSizeY;

    /// <summary>Maximum compute workgroup size (Z).</summary>
    public uint MaxComputeWorkgroupSizeZ;

    /// <summary>Maximum compute invocations per workgroup.</summary>
    public uint MaxComputeInvocationsPerWorkgroup;
}

/// <summary>
/// The main graphics device for creating resources and submitting commands.
/// </summary>
public abstract class GraphiteDevice : IDisposable
{
    private bool _disposed;

    #region Properties

    /// <summary>The backend name (e.g., "OpenGL 4.5", "Vulkan 1.3").</summary>
    public abstract string BackendName { get; }

    /// <summary>The backend type.</summary>
    public abstract GraphicsBackendType BackendType { get; }

    /// <summary>Device capabilities.</summary>
    public abstract DeviceCapabilities Capabilities { get; }

    /// <summary>The current swapchain texture format.</summary>
    public abstract TextureFormat SwapchainFormat { get; }

    /// <summary>Current swapchain width.</summary>
    public abstract uint SwapchainWidth { get; }

    /// <summary>Current swapchain height.</summary>
    public abstract uint SwapchainHeight { get; }

    #endregion

    #region Initialization

    /// <summary>
    /// Initializes the graphics device.
    /// </summary>
    public abstract void Initialize(GraphiteDeviceOptions options);

    #endregion

    #region Resource Creation

    /// <summary>
    /// Creates a GPU buffer.
    /// </summary>
    public abstract Buffer CreateBuffer(in BufferDescriptor descriptor);

    /// <summary>
    /// Creates a GPU buffer with initial data.
    /// </summary>
    public Buffer CreateBuffer<T>(BufferUsage usage, ReadOnlySpan<T> data, MemoryAccess memoryAccess = MemoryAccess.GpuOnly, string? debugName = null) where T : unmanaged
    {
        var descriptor = new BufferDescriptor
        {
            SizeInBytes = (uint)(data.Length * System.Runtime.CompilerServices.Unsafe.SizeOf<T>()),
            Usage = usage | BufferUsage.CopyDestination,
            MemoryAccess = memoryAccess,
            DebugName = debugName,
            InitialData = System.Runtime.InteropServices.MemoryMarshal.AsBytes(data).ToArray(),
        };
        return CreateBuffer(in descriptor);
    }

    /// <summary>
    /// Creates a GPU texture.
    /// </summary>
    public abstract Texture CreateTexture(in TextureDescriptor descriptor);

    /// <summary>
    /// Creates a sampler.
    /// </summary>
    public abstract Sampler CreateSampler(in SamplerDescriptor descriptor);

    /// <summary>
    /// Creates a shader module from source.
    /// </summary>
    public abstract ShaderModule CreateShaderModule(in ShaderModuleDescriptor descriptor);

    /// <summary>
    /// Creates a graphics pipeline state.
    /// </summary>
    public abstract PipelineState CreatePipelineState(in PipelineStateDescriptor descriptor);

    /// <summary>
    /// Creates a compute pipeline state.
    /// </summary>
    public abstract ComputePipelineState CreateComputePipelineState(in ComputePipelineStateDescriptor descriptor);

    /// <summary>
    /// Creates a bind group layout.
    /// </summary>
    public abstract BindGroupLayout CreateBindGroupLayout(in BindGroupLayoutDescriptor descriptor);

    /// <summary>
    /// Creates a bind group.
    /// </summary>
    public abstract BindGroup CreateBindGroup(in BindGroupDescriptor descriptor);

    /// <summary>
    /// Creates a fence for synchronization.
    /// </summary>
    public abstract Fence CreateFence(bool signaled = false);

    #endregion

    #region Command List Management

    /// <summary>
    /// Creates a command list for recording commands.
    /// </summary>
    public abstract CommandList CreateCommandList();

    /// <summary>
    /// Submits a command list for execution.
    /// </summary>
    public abstract void SubmitCommands(CommandList commandList);

    /// <summary>
    /// Submits multiple command lists for execution.
    /// </summary>
    public abstract void SubmitCommands(ReadOnlySpan<CommandList> commandLists);

    /// <summary>
    /// Submits a command list and signals a fence when complete.
    /// </summary>
    public abstract void SubmitCommands(CommandList commandList, Fence fence);

    #endregion

    #region Synchronization

    /// <summary>
    /// Waits for a fence to be signaled.
    /// </summary>
    public abstract void WaitForFence(Fence fence);

    /// <summary>
    /// Waits for the GPU to be completely idle.
    /// </summary>
    public abstract void WaitForIdle();

    #endregion

    #region Frame Management

    /// <summary>
    /// Begins a new frame. Call at the start of each frame.
    /// </summary>
    public abstract void BeginFrame();

    /// <summary>
    /// Ends the current frame. Call after all rendering.
    /// </summary>
    public abstract void EndFrame();

    /// <summary>
    /// Presents the swapchain to the screen.
    /// </summary>
    public abstract void Present();

    /// <summary>
    /// Resizes the swapchain.
    /// </summary>
    public abstract void ResizeSwapchain(uint width, uint height);

    #endregion

    #region Resource Updates

    /// <summary>
    /// Updates buffer data from the CPU. Blocks until complete.
    /// </summary>
    public abstract void UpdateBuffer<T>(Buffer buffer, uint offsetInBytes, ReadOnlySpan<T> data) where T : unmanaged;

    /// <summary>
    /// Updates texture data from the CPU. Blocks until complete.
    /// </summary>
    public abstract void UpdateTexture(Texture texture, in TextureUpdateDescriptor descriptor, ReadOnlySpan<byte> data);

    /// <summary>
    /// Generates mipmaps for a texture.
    /// </summary>
    public abstract void GenerateMipmaps(Texture texture);

    #endregion

    #region Swapchain

    /// <summary>
    /// Gets the current swapchain texture for rendering.
    /// </summary>
    public abstract Texture GetSwapchainTexture();

    #endregion

    #region Disposal

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            WaitForIdle();
            DisposeResources();
            GC.SuppressFinalize(this);
        }
    }

    protected abstract void DisposeResources();

    protected void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    ~GraphiteDevice()
    {
        if (!_disposed)
        {
            Debug.LogWarning("GraphiteDevice was not disposed before finalization.");
        }
    }

    #endregion

    #region Factory

    /// <summary>
    /// Creates a graphics device for the specified backend.
    /// </summary>
    public static GraphiteDevice Create(GraphicsBackendType backend)
    {
        return backend switch
        {
            _ => throw new ArgumentException($"Unknown backend type: {backend}", nameof(backend)),
        };
    }

    #endregion
}
