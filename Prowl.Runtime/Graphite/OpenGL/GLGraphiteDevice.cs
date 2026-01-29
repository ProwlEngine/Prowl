// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Silk.NET.Core.Native;
using Silk.NET.OpenGL;

namespace Prowl.Runtime.Graphite.OpenGL;

/// <summary>
/// OpenGL implementation of the Graphite graphics device.
/// </summary>
public class GLGraphiteDevice : GraphiteDevice
{
    // OpenGL constants not defined in Silk.NET GetPName enum
    private const int GL_MAX_SHADER_STORAGE_BLOCK_SIZE = 0x90DE;
    private const int GL_MAX_COMPUTE_WORK_GROUP_SIZE = 0x91BF;
    private const int GL_MAX_COMPUTE_WORK_GROUP_INVOCATIONS = 0x90EB;

    internal GL GL => GraphicsBackend.OpenGL.GLDevice.GL;

    private DeviceCapabilities _capabilities;
    private bool _initialized;
    private uint _swapchainWidth;
    private uint _swapchainHeight;

    public override string BackendName => $"OpenGL {GL.GetStringS(StringName.Version)}";
    public override GraphicsBackendType BackendType => GraphicsBackendType.OpenGL;
    public override DeviceCapabilities Capabilities => _capabilities;
    public override uint SwapchainWidth => _swapchainWidth;
    public override uint SwapchainHeight => _swapchainHeight;

    public override void Initialize(GraphiteDeviceOptions options)
    {
        ThrowIfDisposed();
        if (_initialized)
            throw new InvalidOperationException("Device is already initialized.");

        // Query capabilities
        _capabilities = QueryCapabilities();
        _initialized = true;

        if (options.EnableDebugLayer)
        {
            unsafe
            {
                if (OperatingSystem.IsWindows())
                {
                    GL.DebugMessageCallback(DebugCallback, null);
                    GL.Enable(EnableCap.DebugOutput);
                    GL.Enable(EnableCap.DebugOutputSynchronous);
                }
            }
        }
    }

    private static void DebugCallback(GLEnum source, GLEnum type, int id, GLEnum severity, int length, nint message, nint userParam)
    {
        string? msg = SilkMarshal.PtrToString(message, NativeStringEncoding.UTF8);
        if (type == GLEnum.DebugTypeError || type == GLEnum.DebugTypeUndefinedBehavior)
            Debug.LogError($"OpenGL Error: {msg}");
        else if (type == GLEnum.DebugTypePerformance || type == GLEnum.DebugTypeMarker || type == GLEnum.DebugTypePortability)
            Debug.LogWarning($"OpenGL Warning: {msg}");
        //else
        //    Debug.Log($"OpenGL Message: {msg}");
    }

    private DeviceCapabilities QueryCapabilities()
    {
        GL.GetInteger(GetPName.MaxTextureSize, out int maxTexSize);
        GL.GetInteger(GetPName.MaxUniformBlockSize, out int maxUboSize);
        GL.GetInteger((GetPName)GL_MAX_SHADER_STORAGE_BLOCK_SIZE, out int maxSsboSize);
        GL.GetInteger(GetPName.MaxVertexAttribs, out int maxVertexAttribs);
        GL.GetInteger(GetPName.MaxColorAttachments, out int maxColorAttachments);

        // Compute shader limits - use safe defaults
        // These are common limits for OpenGL 4.3+ compute shaders
        int maxWorkgroupX = 1024, maxWorkgroupY = 1024, maxWorkgroupZ = 64, maxWorkgroupInvocations = 1024;
        try
        {
            Span<int> workgroupSizes = stackalloc int[3];
            GL.GetInteger((GetPName)GL_MAX_COMPUTE_WORK_GROUP_SIZE, workgroupSizes);
            maxWorkgroupX = workgroupSizes[0];
            maxWorkgroupY = workgroupSizes[1];
            maxWorkgroupZ = workgroupSizes[2];
            GL.GetInteger((GetPName)GL_MAX_COMPUTE_WORK_GROUP_INVOCATIONS, out maxWorkgroupInvocations);
        }
        catch (Exception ex)
        {
            // Compute shaders might not be supported on older OpenGL versions - use defaults
            Debug.LogWarning($"Failed to query compute shader limits, using defaults: {ex.Message}");
        }

        return new DeviceCapabilities
        {
            DeviceName = GL.GetStringS(StringName.Renderer) ?? "Unknown",
            VendorName = GL.GetStringS(StringName.Vendor) ?? "Unknown",
            SupportsCompute = true,
            SupportsGeometryShaders = true,
            SupportsTessellation = true,
            SupportsMultiDrawIndirect = true,
            SupportsBindless = false, // Would need to check for extension
            MaxTextureSize = (uint)maxTexSize,
            MaxUniformBufferSize = (uint)maxUboSize,
            MaxStorageBufferSize = (uint)maxSsboSize,
            MaxBindGroups = 8,
            MaxSamplersPerStage = 16,
            MaxTexturesPerStage = 16,
            MaxVertexAttributes = (uint)maxVertexAttribs,
            MaxVertexBuffers = 16,
            MaxColorAttachments = (uint)maxColorAttachments,
            MaxComputeWorkgroupSizeX = (uint)maxWorkgroupX,
            MaxComputeWorkgroupSizeY = (uint)maxWorkgroupY,
            MaxComputeWorkgroupSizeZ = (uint)maxWorkgroupZ,
            MaxComputeInvocationsPerWorkgroup = (uint)maxWorkgroupInvocations,
        };
    }

    #region Resource Creation

    public override Buffer CreateBuffer(in BufferDescriptor descriptor)
    {
        ThrowIfDisposed();
        return new GLBuffer(this, in descriptor);
    }

    public override Texture CreateTexture(in TextureDescriptor descriptor)
    {
        ThrowIfDisposed();
        return new GLTexture(this, in descriptor);
    }

    public override Sampler CreateSampler(in SamplerDescriptor descriptor)
    {
        ThrowIfDisposed();
        return new GLSampler(this, in descriptor);
    }

    public override ShaderModule CreateShaderModule(in ShaderModuleDescriptor descriptor)
    {
        ThrowIfDisposed();
        return new GLShaderModule(this, in descriptor);
    }

    public override PipelineState CreatePipelineState(in PipelineStateDescriptor descriptor)
    {
        ThrowIfDisposed();
        return new GLPipelineState(this, in descriptor);
    }

    public override ComputePipelineState CreateComputePipelineState(in ComputePipelineStateDescriptor descriptor)
    {
        ThrowIfDisposed();
        return new GLComputePipelineState(this, in descriptor);
    }

    public override BindGroupLayout CreateBindGroupLayout(in BindGroupLayoutDescriptor descriptor)
    {
        ThrowIfDisposed();
        return new GLBindGroupLayout(this, in descriptor);
    }

    public override BindGroup CreateBindGroup(in BindGroupDescriptor descriptor)
    {
        ThrowIfDisposed();
        return new GLBindGroup(this, in descriptor);
    }

    public override Fence CreateFence(bool signaled = false)
    {
        ThrowIfDisposed();
        return new GLFence(this, signaled);
    }

    #endregion

    #region Command List Management

    public override CommandList CreateCommandList()
    {
        ThrowIfDisposed();
        return new GLCommandList(this);
    }

    public override void SubmitCommands(CommandList commandList)
    {
        ThrowIfDisposed();
        if (commandList is GLCommandList glCmd)
        {
            glCmd.Execute();
        }
        else
        {
            throw new ArgumentException("Command list is not a GL command list.", nameof(commandList));
        }
    }

    public override void SubmitCommands(ReadOnlySpan<CommandList> commandLists)
    {
        ThrowIfDisposed();
        foreach (var cmd in commandLists)
        {
            SubmitCommands(cmd);
        }
    }

    public override void SubmitCommands(CommandList commandList, Fence fence)
    {
        SubmitCommands(commandList);
        if (fence is GLFence glFence)
        {
            glFence.InsertFence();
        }
    }

    #endregion

    #region Synchronization

    public override void WaitForFence(Fence fence)
    {
        ThrowIfDisposed();
        fence.Wait();
    }

    public override void WaitForIdle()
    {
        ThrowIfDisposed();
        GL.Finish();
    }

    #endregion

    #region Frame Management

    public override void ResizeSwapchain(uint width, uint height)
    {
        ThrowIfDisposed();
        _swapchainWidth = width;
        _swapchainHeight = height;
        // OpenGL swapchain resize is handled by the windowing system
    }

    #endregion

    #region Resource Updates

    public override unsafe void UpdateBuffer<T>(Buffer buffer, uint offsetInBytes, ReadOnlySpan<T> data)
    {
        ThrowIfDisposed();
        if (buffer is GLBuffer glBuffer)
        {
            glBuffer.Update(offsetInBytes, data);
        }
    }

    public override void UpdateTexture(Texture texture, in TextureUpdateDescriptor descriptor, ReadOnlySpan<byte> data)
    {
        ThrowIfDisposed();
        if (texture is GLTexture glTexture)
        {
            glTexture.Update(in descriptor, data);
        }
    }

    public override void GenerateMipmaps(Texture texture)
    {
        ThrowIfDisposed();
        if (texture is GLTexture glTexture)
        {
            glTexture.GenerateMipmaps();
        }
    }

    #endregion

    #region Swapchain

    public override Texture GetSwapchainTexture()
    {
        ThrowIfDisposed();
        // OpenGL's default framebuffer doesn't give us a texture handle
        // We return a special "null texture" that represents the default framebuffer
        return GLSwapchainTexture.Instance;
    }

    #endregion

    protected override void DisposeResources()
    {
        GL.Dispose();
    }
}

/// <summary>
/// Special texture type representing the OpenGL default framebuffer.
/// </summary>
internal class GLSwapchainTexture : Texture
{
    public static readonly GLSwapchainTexture Instance = new();

    private GLSwapchainTexture()
    {
        Dimension = TextureDimension.Texture2D;
        Width = 0; // Will be set dynamically
        Height = 0;
        Depth = 1;
        MipLevels = 1;
        ArrayLayers = 1;
        Format = TextureFormat.RGBA8Unorm;
        Usage = TextureUsage.RenderTarget;
        SampleCount = SampleCount.Count1;
    }

    protected override void DisposeResources()
    {
        // Singleton, never disposed
    }
}
