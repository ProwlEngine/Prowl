using static Prowl.Veldrid.OpenGL.OpenGLUtil;
using System;
using Silk.NET.Core.Loader;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.EXT;
using GLPixelFormat = Silk.NET.OpenGL.PixelFormat;
using GLFramebufferAttachment = Silk.NET.OpenGL.FramebufferAttachment;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Runtime.CompilerServices;
using Silk.NET.Core.Contexts;

namespace Prowl.Veldrid.OpenGL;

internal unsafe partial class OpenGLGraphicsDevice : GraphicsDevice
{
    private ResourceFactory _resourceFactory;
    private string _deviceName;
    private string _vendorName;
    private string _version;
    private string _shadingLanguageVersion;
    private GraphicsApiVersion _apiVersion;
    private GraphicsBackend _backendType;
    private GraphicsDeviceFeatures _features;
    private uint _vao;
    private readonly ConcurrentQueue<OpenGLDeferredResource> _resourcesToDispose = new();
    private IGLContext _glContext;
    private bool _glContextDestroyed;
    private Action<bool> _setSyncToVBlank;
    private OpenGLSwapchainFramebuffer _swapchainFramebuffer;
    private OpenGLTextureSamplerManager _textureSamplerManager;
    private OpenGLCommandExecutor _commandExecutor;
    private DebugProc _debugMessageCallback;
    public GL GL { get; private set; }
    private OpenGLExtensions _extensions;
    private bool _isDepthRangeZeroToOne;

    // EXT_debug_marker (GLES extension for GPU profiling tools like Xcode GPU debugger).
    internal ExtDebugMarker _extDebugMarker;

    // Silk.NET maps GL.ClearDepth(float) to glClearDepthf (GL 4.1+ / GLES) and
    // GL.ClearDepth(double) to glClearDepth (desktop GL). Same for DepthRange.
    // Desktop GL below 4.1 doesn't have glClearDepthf, so we must use the double variant there.
    internal void ClearDepthCompat(float depth)
    {
        if (_backendType == GraphicsBackend.OpenGLES)
            GL.ClearDepth(depth);         // float overload -> glClearDepthf
        else
            GL.ClearDepth((double)depth); // double overload -> glClearDepth
    }

    internal void DepthRangeCompat(float near, float far)
    {
        if (_backendType == GraphicsBackend.OpenGLES)
            GL.DepthRange(near, far);         // float overload -> glDepthRangef
        else
            GL.DepthRange((double)near, (double)far); // double overload -> glDepthRange
    }
    private BackendInfoOpenGL _openglInfo;

    private TextureSampleCount _maxColorTextureSamples;
    private uint _maxTextureSize;
    private uint _maxTexDepth;
    private uint _maxTexArrayLayers;
    private uint _minUboOffsetAlignment;
    private uint _minSsboOffsetAlignment;

    private struct SlotState
    {
        public nint SyncObject;
        public OpenGLFence FenceWrapper;
        public OpenGLBuffer TransientPrimary;
        public List<OpenGLBuffer> TransientOverflow;
        public ulong CurrentFrameId;
    }

    private SlotState[] _slots;

    private Frame _executorActiveFrame;

    /// <summary>
    /// The active frame as seen by the GL execution thread. Updated only by the executor when it
    /// processes a <see cref="WorkItemType.SetActiveFrame"/> work item, so it lags behind
    /// <see cref="GraphicsDevice.CurrentFrame"/> by the depth of the work queue. Use this anywhere
    /// inside the executor (e.g. transient UBO allocation) instead of <c>_gd.CurrentFrame</c>.
    /// <para>
    /// When usage validation is enabled (VALIDATE_USAGE), reading this while no frame is active on
    /// the execution thread throws a <see cref="RenderException"/>, catching frame-dependent commands
    /// submitted outside a frame. Without validation it returns <see langword="null"/>.
    /// </para>
    /// </summary>
    internal Frame ExecutorActiveFrame
    {
        get
        {
            ExecutorActiveFrame_CheckActive();
            return _executorActiveFrame;
        }
    }

    private readonly List<OpenGLBuffer> _transientFreePool = [];
    private readonly object _transientFreePoolLock = new();

    private readonly StagingMemoryPool _stagingMemoryPool = new();
    private BlockingCollection<ExecutionThreadWorkItem> _workItems;
    private ExecutionThread _executionThread;
    private readonly object _CommandBufferDisposalLock = new();
    private readonly Dictionary<OpenGLCommandBuffer, int> _submittedCommandBufferCounts
        = [];
    private readonly HashSet<OpenGLCommandBuffer> _CommandBuffersToDispose = [];

    private readonly object _mappedResourceLock = new();
    private readonly Dictionary<MappedResourceCacheKey, MappedResourceInfoWithStaging> _mappedResources
        = [];

    private readonly object _resetEventsLock = new();
    private readonly List<ManualResetEventSlim[]> _resetEvents = [];
    private Swapchain _mainSwapchain;

    private bool _syncToVBlank;

    public override string DeviceName => _deviceName;

    public override string VendorName => _vendorName;

    public override GraphicsApiVersion ApiVersion => _apiVersion;

    public override GraphicsBackend BackendType => _backendType;

    public override bool IsUvOriginTopLeft => false;

    public override bool IsDepthRangeZeroToOne => _isDepthRangeZeroToOne;

    public override bool IsClipSpaceYInverted => false;

    public override ResourceFactory ResourceFactory => _resourceFactory;

    public OpenGLExtensions Extensions => _extensions;

    public override Swapchain MainSwapchain => _mainSwapchain;

    public override bool SyncToVerticalBlank
    {
        get => _syncToVBlank;
        set
        {
            if (_syncToVBlank != value)
            {
                _syncToVBlank = value;
                _executionThread.SetSyncToVerticalBlank(value);
            }
        }
    }

    public string Version => _version;

    public string ShadingLanguageVersion => _shadingLanguageVersion;

    public OpenGLTextureSamplerManager TextureSamplerManager => _textureSamplerManager;

    public override GraphicsDeviceFeatures Features => _features;

    public StagingMemoryPool StagingMemoryPool => _stagingMemoryPool;

    public OpenGLGraphicsDevice(
        GraphicsDeviceOptions options,
        OpenGLPlatformInfo platformInfo,
        uint width,
        uint height)
    {
        Init(options, platformInfo, width, height, true);
    }

    private void Init(
        GraphicsDeviceOptions options,
        OpenGLPlatformInfo platformInfo,
        uint width,
        uint height,
        bool loadFunctions)
    {
        _syncToVBlank = options.SyncToVerticalBlank;
        _glContext = platformInfo.GLContext;
        _setSyncToVBlank = platformInfo.SetSyncToVerticalBlank;

        if (loadFunctions)
        {
            GL = GL.GetApi(_glContext);
            OpenGLUtil.GL = GL;
        }

        Debug.Assert(GL != null, "GL instance must be set before Init(). If loadFunctions=false, the caller must set GL beforehand.");

        _version = GL.GetStringS(StringName.Version);
        _shadingLanguageVersion = GL.GetStringS(StringName.ShadingLanguageVersion);
        _vendorName = GL.GetStringS(StringName.Vendor);
        _deviceName = GL.GetStringS(StringName.Renderer);
        _backendType = _version.StartsWith("OpenGL ES") ? GraphicsBackend.OpenGLES : GraphicsBackend.OpenGL;

        // ClearDepthf/DepthRangef are available via GL.ClearDepth(float)/GL.DepthRange(float, float)
        // (core in GL 4.1+ from ARB_ES2_compatibility, and always available in GLES).

        GL.GetInteger(GetPName.MajorVersion, out int majorVersion);
        CheckLastError();
        GL.GetInteger(GetPName.MinorVersion, out int minorVersion);
        CheckLastError();

        GraphicsApiVersion.TryParseGLVersion(_version, out _apiVersion);
        if (_apiVersion.Major != majorVersion ||
            _apiVersion.Minor != minorVersion)
        {
            // This mismatch should never be hit in valid OpenGL implementations.
            _apiVersion = new GraphicsApiVersion(majorVersion, minorVersion, 0, 0);
        }

        GL.GetInteger(GetPName.NumExtensions, out int extensionCount);
        CheckLastError();

        HashSet<string> extensions = [];
        for (uint i = 0; i < extensionCount; i++)
        {
            byte* extensionNamePtr = GL.GetString(StringName.Extensions, i);
            CheckLastError();
            if (extensionNamePtr != null)
            {
                string extensionName = Util.GetString(extensionNamePtr);
                extensions.Add(extensionName);
            }
        }

        _extensions = new OpenGLExtensions(extensions, _backendType, majorVersion, minorVersion);
        HasGlObjectLabel = _extensions.KHR_Debug;

        if (_extensions.EXT_DebugMarker)
        {
            GL.TryGetExtension(out _extDebugMarker);
        }

        bool drawIndirect = _extensions.DrawIndirect || _extensions.MultiDrawIndirect;
        _features = new GraphicsDeviceFeatures(
            computeShader: _extensions.ComputeShaders,
            geometryShader: _extensions.GeometryShader,
            tessellationShaders: _extensions.TessellationShader,
            multipleViewports: _extensions.ARB_ViewportArray,
            samplerLodBias: _backendType == GraphicsBackend.OpenGL,
            drawBaseVertex: _extensions.DrawElementsBaseVertex,
            drawBaseInstance: _extensions.GLVersion(4, 2),
            drawIndirect: drawIndirect,
            drawIndirectBaseInstance: drawIndirect,
            samplerAnisotropy: _extensions.AnisotropicFilter,
            depthClipDisable: _backendType == GraphicsBackend.OpenGL,
            texture1D: _backendType == GraphicsBackend.OpenGL,
            independentBlend: _extensions.IndependentBlend,
            structuredBuffer: _extensions.StorageBuffers,
            subsetTextureView: _extensions.ARB_TextureView,
            CommandBufferDebugMarkers: _extensions.KHR_Debug || _extensions.EXT_DebugMarker,
            bufferRangeBinding: _extensions.ARB_uniform_buffer_object,
            shaderFloat64: _extensions.ARB_GpuShaderFp64);

        GL.GetInteger(GetPName.UniformBufferOffsetAlignment, out int uboAlignment);
        CheckLastError();
        _minUboOffsetAlignment = (uint)uboAlignment;

        if (_features.StructuredBuffer)
        {
            GL.GetInteger(GetPName.ShaderStorageBufferOffsetAlignment, out int ssboAlignment);
            CheckLastError();
            _minSsboOffsetAlignment = (uint)ssboAlignment;
        }

        _resourceFactory = new OpenGLResourceFactory(this);

        _vao = GL.GenVertexArray();
        CheckLastError();

        GL.BindVertexArray(_vao);
        CheckLastError();

        if (options.Debug && (_extensions.KHR_Debug || _extensions.ARB_DebugOutput))
        {
            EnableDebugCallback();
        }

        bool backbufferIsSrgb = ManualSrgbBackbufferQuery();

        PixelFormat swapchainFormat;
        if (options.SwapchainSrgbFormat && (backbufferIsSrgb || RuntimeInformation.IsOSPlatform(OSPlatform.OSX)))
        {
            swapchainFormat = PixelFormat.B8_G8_R8_A8_UNorm_SRgb;
        }
        else
        {
            swapchainFormat = PixelFormat.B8_G8_R8_A8_UNorm;
        }

        _swapchainFramebuffer = new OpenGLSwapchainFramebuffer(
            width,
            height,
            swapchainFormat,
            options.SwapchainDepthFormat,
            swapchainFormat != PixelFormat.B8_G8_R8_A8_UNorm_SRgb);

        // Set miscellaneous initial states.
        if (_backendType == GraphicsBackend.OpenGL)
        {
            GL.Enable(EnableCap.TextureCubeMapSeamless);
            CheckLastError();
        }

        _textureSamplerManager = new OpenGLTextureSamplerManager(this, _extensions);
        _commandExecutor = new OpenGLCommandExecutor(this, platformInfo);

        GL.GetInteger(
            _backendType == GraphicsBackend.OpenGL ? GetPName.MaxColorTextureSamples : (GetPName)GLEnum.MaxSamples,
            out int maxColorTextureSamples);
        CheckLastError();

        _maxColorTextureSamples = maxColorTextureSamples switch
        {
            32 => TextureSampleCount.Count32,
            16 => TextureSampleCount.Count16,
            8 => TextureSampleCount.Count8,
            4 => TextureSampleCount.Count4,
            2 => TextureSampleCount.Count2,
            _ => TextureSampleCount.Count1
        };

        GL.GetInteger(GetPName.MaxTextureSize, out int maxTexSize);
        CheckLastError();

        GL.GetInteger(GetPName.Max3DTextureSize, out int maxTexDepth);
        CheckLastError();

        GL.GetInteger(GetPName.MaxArrayTextureLayers, out int maxTexArrayLayers);
        CheckLastError();

        if (options.PreferDepthRangeZeroToOne && _extensions.ARB_ClipControl)
        {
            GL.ClipControl(ClipControlOrigin.LowerLeft, ClipControlDepth.ZeroToOne);
            CheckLastError();
            _isDepthRangeZeroToOne = true;
        }

        _maxTextureSize = (uint)maxTexSize;
        _maxTexDepth = (uint)maxTexDepth;
        _maxTexArrayLayers = (uint)maxTexArrayLayers;

        _mainSwapchain = new OpenGLSwapchain(
            this,
            _swapchainFramebuffer,
            platformInfo.ResizeSwapchain);

        _workItems = new BlockingCollection<ExecutionThreadWorkItem>(new ConcurrentQueue<ExecutionThreadWorkItem>());
        _glContext.Clear();
        _executionThread = new ExecutionThread(this, _workItems, _glContext);
        _openglInfo = new BackendInfoOpenGL(this);

        InitializeFrameOptions(options);
        InitializeSlots();
        PostDeviceCreated();
    }

    private bool ManualSrgbBackbufferQuery()
    {
        if (_backendType == GraphicsBackend.OpenGLES && !_extensions.EXT_sRGBWriteControl)
        {
            return false;
        }

        uint copySrc = GL.GenTexture();
        CheckLastError();

        float* data = stackalloc float[4];
        data[0] = 0.5f;
        data[1] = 0.5f;
        data[2] = 0.5f;
        data[3] = 1f;

        GL.ActiveTexture(TextureUnit.Texture0);
        CheckLastError();
        GL.BindTexture(TextureTarget.Texture2D, copySrc);
        CheckLastError();
        GL.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba32f, 1, 1, 0, GLPixelFormat.Rgba, PixelType.Float, data);
        CheckLastError();
        uint copySrcFb = GL.GenFramebuffer();
        CheckLastError();

        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, copySrcFb);
        CheckLastError();
        GL.FramebufferTexture2D(FramebufferTarget.ReadFramebuffer, GLFramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, copySrc, 0);
        CheckLastError();

        GL.Enable(EnableCap.FramebufferSrgb);
        CheckLastError();
        GL.BlitFramebuffer(
            0, 0, 1, 1,
            0, 0, 1, 1,
            ClearBufferMask.ColorBufferBit,
            BlitFramebufferFilter.Nearest);
        CheckLastError();

        GL.Disable(EnableCap.FramebufferSrgb);
        CheckLastError();

        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);
        CheckLastError();
        GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, copySrcFb);
        CheckLastError();
        GL.BlitFramebuffer(
            0, 0, 1, 1,
            0, 0, 1, 1,
            ClearBufferMask.ColorBufferBit,
            BlitFramebufferFilter.Nearest);
        CheckLastError();
        if (_backendType == GraphicsBackend.OpenGLES)
        {
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, copySrc);
            CheckLastError();
            GL.ReadPixels(
                0, 0, 1, 1,
                GLPixelFormat.Rgba,
                PixelType.Float,
                data);
            CheckLastError();
        }
        else
        {
            GL.GetTexImage(TextureTarget.Texture2D, 0, GLPixelFormat.Rgba, PixelType.Float, data);
            CheckLastError();
        }

        GL.DeleteFramebuffer(copySrcFb);
        GL.DeleteTexture(copySrc);

        return data[0] > 0.6f;
    }

    private static int GetDepthBits(PixelFormat value)
    {
        switch (value)
        {
            case PixelFormat.R16_UNorm:
                return 16;
            case PixelFormat.R32_Float:
                return 32;
            default:
                throw new RenderException($"Unsupported depth format: {value}");
        }
    }

    private void InitializeSlots()
    {
        _slots = new SlotState[_maxFramesInFlight];
        for (int i = 0; i < _slots.Length; i++)
        {
            _slots[i] = new SlotState
            {
                SyncObject = 0,
                FenceWrapper = new OpenGLFence(signaled: false),
                TransientPrimary = Util.AssertSubtype<DeviceBuffer, OpenGLBuffer>(
                    ResourceFactory.CreateBuffer(new BufferDescription(_transientInitialSize, BufferUsage.Dynamic | BufferUsage.UniformBuffer))),
                TransientOverflow = [],
                CurrentFrameId = 0,
            };
        }
    }

    private protected override Frame BeginFrameCore(ulong frameId, uint ringSlot)
    {
        ref SlotState slot = ref _slots[ringSlot];

        if (slot.CurrentFrameId != 0 && slot.SyncObject != 0)
        {
            nint sync = slot.SyncObject;
            slot.SyncObject = 0;
            _executionThread.Run(() =>
            {
                GL.ClientWaitSync((IntPtr)sync, SyncObjectMask.Bit, ulong.MaxValue);
                GL.DeleteSync((IntPtr)sync);
            });

            ulong completed = slot.CurrentFrameId;
            Volatile.Write(ref _lastCompletedFrameId, Math.Max(Volatile.Read(ref _lastCompletedFrameId), completed));
            slot.FenceWrapper.Set();
        }

        slot.FenceWrapper.Reset();

        if (slot.TransientOverflow.Count > 0)
        {
            lock (_transientFreePoolLock)
            {
                _transientFreePool.AddRange(slot.TransientOverflow);
            }
            slot.TransientOverflow.Clear();
        }

        slot.CurrentFrameId = frameId;

        OpenGLFrame frame = new(this, frameId, ringSlot, slot.FenceWrapper,
            slot.TransientPrimary, slot.TransientOverflow);

        _executionThread.SetActiveFrame(frame);

        return frame;
    }

    private protected override void EndFrameCore(Frame frame)
    {
        ref SlotState slot = ref _slots[frame.RingSlot];
        nint sync = 0;
        ExecuteOnGLThread(() =>
        {
            sync = (nint)GL.FenceSync(SyncCondition.SyncGpuCommandsComplete, SyncBehaviorFlags.None);
        });
        slot.SyncObject = sync;

        _executionThread.SetActiveFrame(null);
    }

    private protected override bool IsFrameCompleteCore(ulong frameId)
    {
        uint ringSlot = (uint)((frameId - 1) % _maxFramesInFlight);
        ref SlotState slot = ref _slots[ringSlot];

        if (slot.CurrentFrameId > frameId)
            return true;

        if (slot.CurrentFrameId == frameId && slot.SyncObject != 0)
        {
            bool done = false;
            nint sync = slot.SyncObject;
            ExecuteOnGLThread(() =>
            {
                GLEnum status = GL.ClientWaitSync((IntPtr)sync, (SyncObjectMask)0, 0);
                done = status == GLEnum.AlreadySignaled || status == GLEnum.ConditionSatisfied;
            });
            return done;
        }

        return true;
    }

    private protected override bool WaitForFrameCore(ulong frameId, ulong nanosecondTimeout)
    {
        uint ringSlot = (uint)((frameId - 1) % _maxFramesInFlight);
        ref SlotState slot = ref _slots[ringSlot];

        if (slot.CurrentFrameId > frameId)
            return true;

        if (slot.CurrentFrameId == frameId && slot.SyncObject != 0)
        {
            bool succeeded = false;
            nint sync = slot.SyncObject;
            ExecuteOnGLThread(() =>
            {
                GLEnum status = GL.ClientWaitSync((IntPtr)sync, SyncObjectMask.Bit, nanosecondTimeout);
                succeeded = status == GLEnum.AlreadySignaled || status == GLEnum.ConditionSatisfied;
            });
            return succeeded;
        }

        return true;
    }

    internal void SubmitCommandBufferInternal(CommandBuffer cl)
    {
        lock (_CommandBufferDisposalLock)
        {
            OpenGLCommandBuffer glCommandBuffer = Util.AssertSubtype<CommandBuffer, OpenGLCommandBuffer>(cl);
            OpenGLCommandEntryList entryList = glCommandBuffer.CurrentCommands;
            IncrementCount(glCommandBuffer);
            _executionThread.ExecuteCommands(entryList);
        }
    }

    internal OpenGLBuffer CreateTransientBuffer(uint sizeInBytes)
    {
        lock (_transientFreePoolLock)
        {
            for (int i = 0; i < _transientFreePool.Count; i++)
            {
                if (_transientFreePool[i].SizeInBytes >= sizeInBytes)
                {
                    OpenGLBuffer buf = _transientFreePool[i];
                    _transientFreePool.RemoveAt(i);
                    return buf;
                }
            }
        }

        return new OpenGLBuffer(this, sizeInBytes, BufferUsage.Dynamic | BufferUsage.UniformBuffer);
    }

    private int IncrementCount(OpenGLCommandBuffer glCommandBuffer)
    {
        if (_submittedCommandBufferCounts.TryGetValue(glCommandBuffer, out int count))
        {
            count += 1;
        }
        else
        {
            count = 1;
        }

        _submittedCommandBufferCounts[glCommandBuffer] = count;
        return count;
    }

    private int DecrementCount(OpenGLCommandBuffer glCommandBuffer)
    {
        if (_submittedCommandBufferCounts.TryGetValue(glCommandBuffer, out int count))
        {
            count -= 1;
        }
        else
        {
            count = -1;
        }

        if (count == 0)
        {
            _submittedCommandBufferCounts.Remove(glCommandBuffer);
        }
        else
        {
            _submittedCommandBufferCounts[glCommandBuffer] = count;
        }
        return count;
    }

    private int GetCount(OpenGLCommandBuffer glCommandBuffer)
    {
        return _submittedCommandBufferCounts.TryGetValue(glCommandBuffer, out int count) ? count : 0;
    }

    private protected override void SwapBuffersCore(Swapchain swapchain)
    {
        _executionThread.SwapBuffers();
    }

    private protected override void WaitForIdleCore()
    {
        try
        {
            if (_slots != null)
            {
                List<nint> pending = null;
                for (int i = 0; i < _slots.Length; i++)
                {
                    ref SlotState slot = ref _slots[i];
                    if (slot.CurrentFrameId != 0 && slot.SyncObject != 0)
                    {
                        (pending ??= []).Add(slot.SyncObject);
                        slot.SyncObject = 0;
                        slot.FenceWrapper.Set();
                    }
                }

                if (pending != null)
                {
                    nint[] syncs = pending.ToArray();
                    _executionThread.Run(() =>
                    {
                        for (int i = 0; i < syncs.Length; i++)
                        {
                            GL.ClientWaitSync((IntPtr)syncs[i], SyncObjectMask.Bit, ulong.MaxValue);
                            GL.DeleteSync((IntPtr)syncs[i]);
                        }
                    });
                }
            }

            _executionThread.WaitForIdle();
        }
        catch (RenderException)
        {
            // The GL context may already be destroyed by SDL_DestroyWindow.
            // Silk.NET throws SymbolLoadingException for unresolved GL functions
            // on a dead context. Safe to ignore - the OS reclaims all GL objects.
        }
    }

    public override TextureSampleCount GetSampleCountLimit(PixelFormat format, bool depthFormat)
    {
        return _maxColorTextureSamples;
    }

    private protected override bool GetPixelFormatSupportCore(
        PixelFormat format,
        TextureType type,
        TextureUsage usage,
        out PixelFormatProperties properties)
    {
        if (type == TextureType.Texture1D && !_features.Texture1D
            || !OpenGLFormats.IsFormatSupported(_extensions, format, _backendType))
        {
            properties = default(PixelFormatProperties);
            return false;
        }

        uint sampleCounts = 0;
        int max = (int)_maxColorTextureSamples + 1;
        for (int i = 0; i < max; i++)
        {
            sampleCounts |= (uint)(1 << i);
        }

        properties = new PixelFormatProperties(
            _maxTextureSize,
            type == TextureType.Texture1D ? 1 : _maxTextureSize,
            type != TextureType.Texture3D ? 1 : _maxTexDepth,
            uint.MaxValue,
            type == TextureType.Texture3D ? 1 : _maxTexArrayLayers,
            sampleCounts);
        return true;
    }

    protected override MappedResource MapCore(MappableResource resource, MapMode mode, uint subresource)
    {
        MappedResourceCacheKey key = new(resource, subresource);
        lock (_mappedResourceLock)
        {
            if (_mappedResources.TryGetValue(key, out MappedResourceInfoWithStaging info))
            {
                if (info.Mode != mode)
                {
                    throw new RenderException("The given resource was already mapped with a different MapMode.");
                }

                info.RefCount += 1;
                _mappedResources[key] = info;
                return info.MappedResource;
            }
        }

        return _executionThread.Map(resource, mode, subresource);
    }

    protected override void UnmapCore(MappableResource resource, uint subresource)
    {
        _executionThread.Unmap(resource, subresource);
    }

    private protected override void UpdateBufferCore(DeviceBuffer buffer, uint bufferOffsetInBytes, IntPtr source, uint sizeInBytes)
    {
        lock (_mappedResourceLock)
        {
            if (_mappedResources.ContainsKey(new MappedResourceCacheKey(buffer, 0)))
            {
                throw new RenderException("Cannot call UpdateBuffer on a currently-mapped Buffer.");
            }
        }
        StagingBlock sb = _stagingMemoryPool.Stage(source, sizeInBytes);
        _executionThread.UpdateBuffer(buffer, bufferOffsetInBytes, sb);
    }

    private protected override void UpdateTextureCore(
        Texture texture,
        IntPtr source,
        uint sizeInBytes,
        uint x,
        uint y,
        uint z,
        uint width,
        uint height,
        uint depth,
        uint mipLevel,
        uint arrayLayer)
    {
        StagingBlock textureData = _stagingMemoryPool.Stage(source, sizeInBytes);
        StagingBlock argBlock = _stagingMemoryPool.GetStagingBlock(UpdateTextureArgsSize);
        ref UpdateTextureArgs args = ref Unsafe.AsRef<UpdateTextureArgs>(argBlock.Data);
        args.Data = (IntPtr)textureData.Data;
        args.X = x;
        args.Y = y;
        args.Z = z;
        args.Width = width;
        args.Height = height;
        args.Depth = depth;
        args.MipLevel = mipLevel;
        args.ArrayLayer = arrayLayer;

        _executionThread.UpdateTexture(texture, argBlock.Id, textureData.Id);
    }

    private static readonly uint UpdateTextureArgsSize = (uint)Unsafe.SizeOf<UpdateTextureArgs>();

    private struct UpdateTextureArgs
    {
        public IntPtr Data;
        public uint X;
        public uint Y;
        public uint Z;
        public uint Width;
        public uint Height;
        public uint Depth;
        public uint MipLevel;
        public uint ArrayLayer;
    }

    public override bool WaitForFence(Fence fence, ulong nanosecondTimeout)
    {
        return Util.AssertSubtype<Fence, OpenGLFence>(fence).Wait(nanosecondTimeout);
    }

    private ManualResetEventSlim[] GetResetEventArray(int length)
    {
        lock (_resetEventsLock)
        {
            for (int i = _resetEvents.Count - 1; i > 0; i--)
            {
                ManualResetEventSlim[] array = _resetEvents[i];
                if (array.Length == length)
                {
                    _resetEvents.RemoveAt(i);
                    return array;
                }
            }
        }

        ManualResetEventSlim[] newArray = new ManualResetEventSlim[length];
        return newArray;
    }

    private void ReturnResetEventArray(ManualResetEventSlim[] array)
    {
        lock (_resetEventsLock)
        {
            _resetEvents.Add(array);
        }
    }

    public override void ResetFence(Fence fence)
    {
        Util.AssertSubtype<Fence, OpenGLFence>(fence).Reset();
    }

    internal void EnqueueDisposal(OpenGLDeferredResource resource)
    {
        _resourcesToDispose.Enqueue(resource);
    }

    internal void EnqueueDisposal(OpenGLCommandBuffer CommandBuffer)
    {
        lock (_CommandBufferDisposalLock)
        {
            if (GetCount(CommandBuffer) > 0)
            {
                _CommandBuffersToDispose.Add(CommandBuffer);
            }
            else
            {
                CommandBuffer.DestroyResources();
            }
        }
    }

    internal bool CheckCommandBufferDisposal(OpenGLCommandBuffer CommandBuffer)
    {

        lock (_CommandBufferDisposalLock)
        {
            int count = DecrementCount(CommandBuffer);
            if (count == 0)
            {
                if (_CommandBuffersToDispose.Remove(CommandBuffer))
                {
                    CommandBuffer.DestroyResources();
                    return true;
                }
            }

            return false;
        }
    }

    private void FlushDisposables()
    {
        if (_glContextDestroyed) return;

        try
        {
            while (_resourcesToDispose.TryDequeue(out OpenGLDeferredResource resource))
            {
                resource.DestroyGLResources();
            }
        }
        catch (SymbolLoadingException)
        {
            // GL context already gone (SDL_DestroyWindow ran or the OS reclaimed it).
            // Silk.NET's lazy symbol loader throws on any unresolved GL call. Stop
            // attempting GL cleanup; remaining resources stay in the queue and the
            // OS reclaims their GL objects on process exit.
            _glContextDestroyed = true;
        }
    }

    public void EnableDebugCallback() => EnableDebugCallback(DebugSeverity.DebugSeverityNotification);
    public void EnableDebugCallback(DebugSeverity minimumSeverity) => EnableDebugCallback(DefaultDebugCallback(minimumSeverity));
    public void EnableDebugCallback(DebugProc callback)
    {
        GL.Enable(EnableCap.DebugOutput);
        CheckLastError();
        // The debug callback delegate must be persisted, otherwise errors will occur
        // when the OpenGL drivers attempt to call it after it has been collected.
        _debugMessageCallback = callback;
        GL.DebugMessageCallback(_debugMessageCallback, null);
        CheckLastError();
    }

    private DebugProc DefaultDebugCallback(DebugSeverity minimumSeverity)
    {
        return (source, type, id, severity, length, message, userParam) =>
        {
            if ((DebugSeverity)severity >= minimumSeverity
                && (DebugType)type != DebugType.DebugTypeMarker
                && (DebugType)type != DebugType.DebugTypePushGroup
                && (DebugType)type != DebugType.DebugTypePopGroup)
            {
                string messageString = Marshal.PtrToStringAnsi(message, length);
                Debug.WriteLine($"GL DEBUG MESSAGE: {source}, {type}, {id}. {severity}: {messageString}");
            }
        };
    }

    protected override void PlatformDispose()
    {
        if (_slots != null)
        {
            foreach (ref SlotState slot in _slots.AsSpan())
            {
                if (slot.SyncObject != 0)
                {
                    nint sync = slot.SyncObject;
                    try { ExecuteOnGLThread(() => GL.DeleteSync((IntPtr)sync)); }
                    catch { }
                }
                slot.TransientPrimary?.Dispose();
                foreach (OpenGLBuffer buf in slot.TransientOverflow)
                    buf.Dispose();
                slot.FenceWrapper?.Dispose();
            }
        }

        lock (_transientFreePoolLock)
        {
            foreach (OpenGLBuffer buf in _transientFreePool)
                buf.Dispose();
            _transientFreePool.Clear();
        }

        try
        {
            FlushAndFinish();
        }
        catch (RenderException)
        {
            // The GL context may already be destroyed by SDL_DestroyWindow.
            // Silk.NET throws SymbolLoadingException for unresolved GL functions
            // on a dead context. Safe to ignore during shutdown.
        }
        _executionThread.Terminate();
    }

    public override bool GetOpenGLInfo(out BackendInfoOpenGL info)
    {
        info = _openglInfo;
        return true;
    }

    internal void ExecuteOnGLThread(Action action)
    {
        _executionThread.Run(action);
        _executionThread.WaitForIdle();
    }

    internal void FlushAndFinish()
    {
        _executionThread.FlushAndFinish();
    }

    internal void EnsureResourceInitialized(OpenGLDeferredResource deferredResource)
    {
        _executionThread.InitializeResource(deferredResource);
    }

    internal override uint GetUniformBufferMinOffsetAlignmentCore() => _minUboOffsetAlignment;

    internal override uint GetStructuredBufferMinOffsetAlignmentCore() => _minSsboOffsetAlignment;

    private class ExecutionThread
    {
        private readonly OpenGLGraphicsDevice _gd;
        private readonly BlockingCollection<ExecutionThreadWorkItem> _workItems;
        private readonly IGLContext _context;
        private bool _terminated;
        private readonly ManualResetEventSlim _terminatedEvent = new();
        private readonly List<Exception> _exceptions = [];
        private readonly object _exceptionsLock = new();

        public ExecutionThread(
            OpenGLGraphicsDevice gd,
            BlockingCollection<ExecutionThreadWorkItem> workItems,
            IGLContext context)
        {
            _gd = gd;
            _workItems = workItems;
            _context = context;
            Thread thread = new(Run);
            thread.IsBackground = true;
            thread.Start();
        }

        private void Run()
        {
            _context.MakeCurrent();
            while (!_terminated)
            {
                ExecutionThreadWorkItem workItem = _workItems.Take();
                ExecuteWorkItem(workItem);
            }
        }

        private void ExecuteWorkItem(ExecutionThreadWorkItem workItem)
        {
            try
            {
                switch (workItem.Type)
                {
                    case WorkItemType.ExecuteList:
                        {
                            OpenGLCommandEntryList list = (OpenGLCommandEntryList)workItem.Object0;
                            try
                            {
                                list.ExecuteAll(_gd._commandExecutor);
                            }
                            finally
                            {
                                if (!_gd.CheckCommandBufferDisposal(list.Parent))
                                {
                                    list.Parent.OnCompleted(list);
                                }
                            }
                        }
                        break;
                    case WorkItemType.Map:
                        {
                            MappableResource resourceToMap = (MappableResource)workItem.Object0;
                            ManualResetEventSlim mre = (ManualResetEventSlim)workItem.Object1;

                            MapParams* resultPtr = (MapParams*)Util.UnpackIntPtr(workItem.UInt0, workItem.UInt1);

                            if (resultPtr->Map)
                            {
                                ExecuteMapResource(
                                    resourceToMap,
                                    mre,
                                    resultPtr);
                            }
                            else
                            {
                                ExecuteUnmapResource(resourceToMap, resultPtr->Subresource, mre);
                            }
                        }
                        break;
                    case WorkItemType.UpdateBuffer:
                        {
                            DeviceBuffer updateBuffer = (DeviceBuffer)workItem.Object0;
                            uint offsetInBytes = workItem.UInt0;
                            StagingBlock stagingBlock = _gd.StagingMemoryPool.RetrieveById(workItem.UInt1);

                            _gd._commandExecutor.UpdateBuffer(
                                updateBuffer,
                                offsetInBytes,
                                (IntPtr)stagingBlock.Data,
                                stagingBlock.SizeInBytes);

                            _gd.StagingMemoryPool.Free(stagingBlock);
                        }
                        break;
                    case WorkItemType.UpdateTexture:
                        Texture texture = (Texture)workItem.Object0;
                        StagingMemoryPool pool = _gd.StagingMemoryPool;
                        StagingBlock argBlock = pool.RetrieveById(workItem.UInt0);
                        StagingBlock textureData = pool.RetrieveById(workItem.UInt1);
                        ref UpdateTextureArgs args = ref Unsafe.AsRef<UpdateTextureArgs>(argBlock.Data);

                        _gd._commandExecutor.UpdateTexture(
                            texture, args.Data, args.X, args.Y, args.Z,
                            args.Width, args.Height, args.Depth, args.MipLevel, args.ArrayLayer);

                        pool.Free(argBlock);
                        pool.Free(textureData);
                        break;
                    case WorkItemType.GenericAction:
                        {
                            ((Action)workItem.Object0)();
                        }
                        break;
                    case WorkItemType.TerminateAction:
                        {
                            try
                            {
                                try
                                {
                                    _gd._glContext.MakeCurrent();
                                    _gd.FlushDisposables();
                                    _gd._glContext.Dispose();
                                }
                                catch (SymbolLoadingException)
                                {
                                    // Context already destroyed by the OS. Nothing to clean up via GL.
                                }
                                _gd.StagingMemoryPool.Dispose();
                            }
                            finally
                            {
                                _terminated = true;
                                _terminatedEvent.Set();
                            }
                        }
                        break;
                    case WorkItemType.SetSyncToVerticalBlank:
                        {
                            bool value = workItem.UInt0 == 1 ? true : false;
                            _gd._setSyncToVBlank(value);
                        }
                        break;
                    case WorkItemType.SwapBuffers:
                        {
                            _gd._glContext.SwapBuffers();
                            _gd.FlushDisposables();
                        }
                        break;
                    case WorkItemType.WaitForIdle:
                        {
                            // Set() must be in finally so the main thread never deadlocks
                            // if FlushDisposables or glFinish throws on a destroyed context.
                            try
                            {
                                _gd.FlushDisposables();
                                bool isFullFlush = workItem.UInt0 != 0;
                                if (isFullFlush)
                                {
                                    _gd.GL.Flush();
                                    _gd.GL.Finish();
                                }
                            }
                            catch (SymbolLoadingException)
                            {
                                // Context destroyed before the work item ran. Flush is moot; finally releases the caller.
                            }
                            finally
                            {
                                ((ManualResetEventSlim)workItem.Object0).Set();
                            }
                        }
                        break;
                    case WorkItemType.InitializeResource:
                        {
                            InitializeResourceInfo info = (InitializeResourceInfo)workItem.Object0;
                            try
                            {
                                info.DeferredResource.EnsureResourcesCreated();
                            }
                            catch (Exception e)
                            {
                                info.Exception = e;
                            }
                            finally
                            {
                                info.ResetEvent.Set();
                            }
                        }
                        break;
                    case WorkItemType.SetActiveFrame:
                        {
                            _gd._executorActiveFrame = (Frame)workItem.Object0;
                        }
                        break;
                    default:
                        throw new InvalidOperationException("Invalid command type: " + workItem.Type);
                }
            }
            catch (Exception e) when (!Debugger.IsAttached)
            {
                lock (_exceptionsLock)
                {
                    _exceptions.Add(e);
                }
            }
        }

        private void ExecuteMapResource(
            MappableResource resource,
            ManualResetEventSlim mre,
            MapParams* result)
        {
            uint subresource = result->Subresource;
            MapMode mode = result->MapMode;

            MappedResourceCacheKey key = new(resource, subresource);
            try
            {
                lock (_gd._mappedResourceLock)
                {
                    Debug.Assert(!_gd._mappedResources.ContainsKey(key));
                    if (resource is OpenGLBuffer buffer)
                    {
                        buffer.EnsureResourcesCreated();
                        void* mappedPtr;
                        MapBufferAccessMask accessMask = OpenGLFormats.VdToGLMapMode(mode);
                        if (_gd.Extensions.ARB_DirectStateAccess)
                        {
                            mappedPtr = _gd.GL.MapNamedBufferRange(buffer.Buffer, IntPtr.Zero, buffer.SizeInBytes, accessMask);
                            CheckLastError();
                        }
                        else
                        {
                            _gd.GL.BindBuffer(BufferTargetARB.CopyWriteBuffer, buffer.Buffer);
                            CheckLastError();

                            mappedPtr = _gd.GL.MapBufferRange(BufferTargetARB.CopyWriteBuffer, IntPtr.Zero, (UIntPtr)buffer.SizeInBytes, accessMask);
                            CheckLastError();
                        }

                        MappedResourceInfoWithStaging info = new();
                        info.MappedResource = new MappedResource(
                            resource,
                            mode,
                            (IntPtr)mappedPtr,
                            buffer.SizeInBytes);
                        info.RefCount = 1;
                        info.Mode = mode;
                        _gd._mappedResources.Add(key, info);
                        result->Data = (IntPtr)mappedPtr;
                        result->DataSize = buffer.SizeInBytes;
                        result->RowPitch = 0;
                        result->DepthPitch = 0;
                        result->Succeeded = true;
                    }
                    else
                    {
                        OpenGLTexture texture = Util.AssertSubtype<MappableResource, OpenGLTexture>(resource);
                        texture.EnsureResourcesCreated();

                        Util.GetMipLevelAndArrayLayer(texture, subresource, out uint mipLevel, out uint arrayLayer);
                        Util.GetMipDimensions(texture, mipLevel, out uint mipWidth, out uint mipHeight, out uint mipDepth);

                        uint depthSliceSize = FormatHelpers.GetDepthPitch(
                            FormatHelpers.GetRowPitch(mipWidth, texture.Format),
                            mipHeight,
                            texture.Format);
                        uint subresourceSize = depthSliceSize * mipDepth;
                        int compressedSize = 0;

                        bool isCompressed = FormatHelpers.IsCompressedFormat(texture.Format);
                        if (isCompressed)
                        {
                            _gd.GL.GetTexLevelParameter(
                                texture.TextureTarget,
                                (int)mipLevel,
                                (GetTextureParameter)GLEnum.TextureCompressedImageSize,
                                out compressedSize);
                            CheckLastError();
                        }

                        StagingBlock block = _gd._stagingMemoryPool.GetStagingBlock(subresourceSize);

                        uint packAlignment = 4;
                        if (!isCompressed)
                        {
                            packAlignment = FormatSizeHelpers.GetSizeInBytes(texture.Format);
                        }

                        if (packAlignment < 4)
                        {
                            _gd.GL.PixelStore(PixelStoreParameter.PackAlignment, (int)packAlignment);
                            CheckLastError();
                        }

                        if (mode == MapMode.Read || mode == MapMode.ReadWrite)
                        {
                            if (!isCompressed)
                            {
                                // Read data into buffer.
                                if (_gd.Extensions.ARB_DirectStateAccess && texture.ArrayLayers == 1)
                                {
                                    int zoffset = texture.ArrayLayers > 1 ? (int)arrayLayer : 0;
                                    _gd.GL.GetTextureSubImage(
                                        texture.Texture,
                                        (int)mipLevel,
                                        0, 0, zoffset,
                                        mipWidth, mipHeight, mipDepth,
                                        texture.GLPixelFormat,
                                        texture.GLPixelType,
                                        subresourceSize,
                                        block.Data);
                                    CheckLastError();
                                }
                                else
                                {
                                    for (uint layer = 0; layer < mipDepth; layer++)
                                    {
                                        uint curLayer = arrayLayer + layer;
                                        uint curOffset = depthSliceSize * layer;
                                        uint readFB = _gd.GL.GenFramebuffer();
                                        CheckLastError();
                                        _gd.GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, readFB);
                                        CheckLastError();

                                        if (texture.ArrayLayers > 1 || texture.Type == TextureType.Texture3D)
                                        {
                                            _gd.GL.FramebufferTextureLayer(
                                                FramebufferTarget.ReadFramebuffer,
                                                GLFramebufferAttachment.ColorAttachment0,
                                                texture.Texture,
                                                (int)mipLevel,
                                                (int)curLayer);
                                            CheckLastError();
                                        }
                                        else if (texture.Type == TextureType.Texture1D)
                                        {
                                            _gd.GL.FramebufferTexture1D(
                                                FramebufferTarget.ReadFramebuffer,
                                                GLFramebufferAttachment.ColorAttachment0,
                                                TextureTarget.Texture1D,
                                                texture.Texture,
                                                (int)mipLevel);
                                            CheckLastError();
                                        }
                                        else
                                        {
                                            _gd.GL.FramebufferTexture2D(
                                                FramebufferTarget.ReadFramebuffer,
                                                GLFramebufferAttachment.ColorAttachment0,
                                                TextureTarget.Texture2D,
                                                texture.Texture,
                                                (int)mipLevel);
                                            CheckLastError();
                                        }

                                        _gd.GL.ReadPixels(
                                            0, 0,
                                            mipWidth, mipHeight,
                                            texture.GLPixelFormat,
                                            texture.GLPixelType,
                                            (byte*)block.Data + curOffset);
                                        CheckLastError();
                                        _gd.GL.DeleteFramebuffer(readFB);
                                        CheckLastError();
                                    }
                                }
                            }
                            else // isCompressed
                            {
                                if (texture.TextureTarget == TextureTarget.Texture2DArray
                                    || texture.TextureTarget == TextureTarget.Texture2DMultisampleArray
                                    || texture.TextureTarget == TextureTarget.TextureCubeMapArray)
                                {
                                    // We only want a single subresource (array slice), so we need to copy
                                    // a subsection of the downloaded data into our staging block.

                                    uint fullDataSize = (uint)compressedSize;
                                    StagingBlock fullBlock = _gd._stagingMemoryPool.GetStagingBlock(fullDataSize);

                                    if (_gd.Extensions.ARB_DirectStateAccess)
                                    {
                                        _gd.GL.GetCompressedTextureImage(
                                            texture.Texture,
                                            (int)mipLevel,
                                            fullBlock.SizeInBytes,
                                            fullBlock.Data);
                                        CheckLastError();
                                    }
                                    else
                                    {
                                        _gd.TextureSamplerManager.SetTextureTransient(texture.TextureTarget, texture.Texture);
                                        CheckLastError();

                                        _gd.GL.GetCompressedTexImage(texture.TextureTarget, (int)mipLevel, fullBlock.Data);
                                        CheckLastError();
                                    }
                                    byte* sliceStart = (byte*)fullBlock.Data + (arrayLayer * subresourceSize);
                                    System.Buffer.MemoryCopy(sliceStart, block.Data, subresourceSize, subresourceSize);
                                    _gd._stagingMemoryPool.Free(fullBlock);
                                }
                                else
                                {
                                    if (_gd.Extensions.ARB_DirectStateAccess)
                                    {
                                        _gd.GL.GetCompressedTextureImage(
                                            texture.Texture,
                                            (int)mipLevel,
                                            block.SizeInBytes,
                                            block.Data);
                                        CheckLastError();
                                    }
                                    else
                                    {
                                        _gd.TextureSamplerManager.SetTextureTransient(texture.TextureTarget, texture.Texture);
                                        CheckLastError();

                                        _gd.GL.GetCompressedTexImage(texture.TextureTarget, (int)mipLevel, block.Data);
                                        CheckLastError();
                                    }
                                }
                            }
                        }

                        if (packAlignment < 4)
                        {
                            _gd.GL.PixelStore(PixelStoreParameter.PackAlignment, 4);
                            CheckLastError();
                        }

                        uint rowPitch = FormatHelpers.GetRowPitch(mipWidth, texture.Format);
                        uint depthPitch = FormatHelpers.GetDepthPitch(rowPitch, mipHeight, texture.Format);
                        MappedResourceInfoWithStaging info = new();
                        info.MappedResource = new MappedResource(
                            resource,
                            mode,
                            (IntPtr)block.Data,
                            subresourceSize,
                            subresource,
                            rowPitch,
                            depthPitch);
                        info.RefCount = 1;
                        info.Mode = mode;
                        info.StagingBlock = block;
                        _gd._mappedResources.Add(key, info);
                        result->Data = (IntPtr)block.Data;
                        result->DataSize = subresourceSize;
                        result->RowPitch = rowPitch;
                        result->DepthPitch = depthPitch;
                        result->Succeeded = true;
                    }
                }
            }
            catch
            {
                result->Succeeded = false;
                throw;
            }
            finally
            {
                mre.Set();
            }
        }

        private void ExecuteUnmapResource(MappableResource resource, uint subresource, ManualResetEventSlim mre)
        {
            MappedResourceCacheKey key = new(resource, subresource);
            lock (_gd._mappedResourceLock)
            {
                MappedResourceInfoWithStaging info = _gd._mappedResources[key];
                if (info.RefCount == 1)
                {
                    if (resource is OpenGLBuffer buffer)
                    {
                        if (_gd.Extensions.ARB_DirectStateAccess)
                        {
                            _gd.GL.UnmapNamedBuffer(buffer.Buffer);
                            CheckLastError();
                        }
                        else
                        {
                            _gd.GL.BindBuffer(BufferTargetARB.CopyWriteBuffer, buffer.Buffer);
                            CheckLastError();

                            _gd.GL.UnmapBuffer(BufferTargetARB.CopyWriteBuffer);
                            CheckLastError();
                        }
                    }
                    else
                    {
                        OpenGLTexture texture = Util.AssertSubtype<MappableResource, OpenGLTexture>(resource);

                        if (info.Mode == MapMode.Write || info.Mode == MapMode.ReadWrite)
                        {
                            Util.GetMipLevelAndArrayLayer(texture, subresource, out uint mipLevel, out uint arrayLayer);
                            Util.GetMipDimensions(texture, mipLevel, out uint width, out uint height, out uint depth);

                            IntPtr data = (IntPtr)info.StagingBlock.Data;

                            _gd._commandExecutor.UpdateTexture(
                                texture,
                                data,
                                0, 0, 0,
                                width, height, depth,
                                mipLevel,
                                arrayLayer);
                        }

                        _gd.StagingMemoryPool.Free(info.StagingBlock);
                    }

                    _gd._mappedResources.Remove(key);
                }
            }

            mre.Set();
        }

        private void CheckExceptions()
        {
            lock (_exceptionsLock)
            {
                if (_exceptions.Count > 0)
                {
                    Exception innerException = _exceptions.Count == 1
                        ? _exceptions[0]
                        : new AggregateException(_exceptions.ToArray());
                    _exceptions.Clear();
                    throw new RenderException(
                        "Error(s) were encountered during the execution of OpenGL commands. See InnerException for more information.",
                        innerException);

                }
            }
        }

        public MappedResource Map(MappableResource resource, MapMode mode, uint subresource)
        {
            CheckExceptions();

            MapParams mrp = new();
            mrp.Map = true;
            mrp.Subresource = subresource;
            mrp.MapMode = mode;

            ManualResetEventSlim mre = new(false);
            _workItems.Add(new ExecutionThreadWorkItem(resource, &mrp, mre));
            mre.Wait();
            if (!mrp.Succeeded)
            {
                throw new RenderException("Failed to map OpenGL resource.");
            }

            mre.Dispose();

            return new MappedResource(resource, mode, mrp.Data, mrp.DataSize, mrp.Subresource, mrp.RowPitch, mrp.DepthPitch);
        }

        internal void Unmap(MappableResource resource, uint subresource)
        {
            CheckExceptions();

            MapParams mrp = new();
            mrp.Map = false;
            mrp.Subresource = subresource;

            ManualResetEventSlim mre = new(false);
            _workItems.Add(new ExecutionThreadWorkItem(resource, &mrp, mre));
            mre.Wait();
            mre.Dispose();
        }

        public void ExecuteCommands(OpenGLCommandEntryList entryList)
        {
            CheckExceptions();
            entryList.Parent.OnSubmitted(entryList);
            _workItems.Add(new ExecutionThreadWorkItem(entryList));
        }

        internal void SetActiveFrame(Frame frame)
        {
            CheckExceptions();
            _workItems.Add(new ExecutionThreadWorkItem(WorkItemType.SetActiveFrame, frame));
        }

        internal void UpdateBuffer(DeviceBuffer buffer, uint offsetInBytes, StagingBlock stagingBlock)
        {
            CheckExceptions();

            _workItems.Add(new ExecutionThreadWorkItem(buffer, offsetInBytes, stagingBlock));
        }

        internal void UpdateTexture(Texture texture, uint argBlockId, uint dataBlockId)
        {
            CheckExceptions();

            _workItems.Add(new ExecutionThreadWorkItem(texture, argBlockId, dataBlockId));
        }

        internal void Run(Action a)
        {
            CheckExceptions();

            _workItems.Add(new ExecutionThreadWorkItem(a));
        }

        internal void Terminate()
        {
            CheckExceptions();

            _workItems.Add(new ExecutionThreadWorkItem(WorkItemType.TerminateAction));
            _terminatedEvent.Wait();
            _terminatedEvent.Dispose();
            CheckExceptions();
        }

        internal void WaitForIdle()
        {
            ManualResetEventSlim mre = new();
            _workItems.Add(new ExecutionThreadWorkItem(mre, isFullFlush: false));
            mre.Wait();
            mre.Dispose();

            CheckExceptions();
        }

        internal void SetSyncToVerticalBlank(bool value)
        {
            _workItems.Add(new ExecutionThreadWorkItem(value));
        }

        internal void SwapBuffers()
        {
            _workItems.Add(new ExecutionThreadWorkItem(WorkItemType.SwapBuffers));
        }

        internal void FlushAndFinish()
        {
            ManualResetEventSlim mre = new();
            _workItems.Add(new ExecutionThreadWorkItem(mre, isFullFlush: true));
            mre.Wait();
            mre.Dispose();

            CheckExceptions();
        }

        internal void InitializeResource(OpenGLDeferredResource deferredResource)
        {
            InitializeResourceInfo info = new(deferredResource, new ManualResetEventSlim());
            _workItems.Add(new ExecutionThreadWorkItem(info));
            info.ResetEvent.Wait();
            info.ResetEvent.Dispose();

            if (info.Exception != null)
            {
                throw info.Exception;
            }
        }
    }

    public enum WorkItemType : byte
    {
        Map,
        Unmap,
        ExecuteList,
        UpdateBuffer,
        UpdateTexture,
        GenericAction,
        TerminateAction,
        SetSyncToVerticalBlank,
        SwapBuffers,
        WaitForIdle,
        InitializeResource,
        SetActiveFrame,
    }

    private unsafe struct ExecutionThreadWorkItem
    {
        public readonly WorkItemType Type;
        public readonly object? Object0;
        public readonly object? Object1;
        public readonly uint UInt0;
        public readonly uint UInt1;
        public readonly uint UInt2;

        public ExecutionThreadWorkItem(
            MappableResource resource,
            MapParams* mapResult,
            ManualResetEventSlim resetEvent)
        {
            Type = WorkItemType.Map;
            Object0 = resource;
            Object1 = resetEvent;

            Util.PackIntPtr((IntPtr)mapResult, out UInt0, out UInt1);
            UInt2 = 0;
        }

        public ExecutionThreadWorkItem(OpenGLCommandEntryList CommandBuffer)
        {
            Type = WorkItemType.ExecuteList;
            Object0 = CommandBuffer;
            Object1 = null;

            UInt0 = 0;
            UInt1 = 0;
            UInt2 = 0;
        }

        public ExecutionThreadWorkItem(DeviceBuffer updateBuffer, uint offsetInBytes, StagingBlock stagedSource)
        {
            Type = WorkItemType.UpdateBuffer;
            Object0 = updateBuffer;
            Object1 = null;

            UInt0 = offsetInBytes;
            UInt1 = stagedSource.Id;
            UInt2 = 0;
        }

        public ExecutionThreadWorkItem(Action a, bool isTermination = false)
        {
            Type = isTermination ? WorkItemType.TerminateAction : WorkItemType.GenericAction;
            Object0 = a;
            Object1 = null;

            UInt0 = 0;
            UInt1 = 0;
            UInt2 = 0;
        }

        public ExecutionThreadWorkItem(Texture texture, uint argBlockId, uint dataBlockId)
        {
            Type = WorkItemType.UpdateTexture;
            Object0 = texture;
            Object1 = null;

            UInt0 = argBlockId;
            UInt1 = dataBlockId;
            UInt2 = 0;
        }

        public ExecutionThreadWorkItem(ManualResetEventSlim mre, bool isFullFlush)
        {
            Type = WorkItemType.WaitForIdle;
            Object0 = mre;
            Object1 = null;

            UInt0 = isFullFlush ? 1u : 0u;
            UInt1 = 0;
            UInt2 = 0;
        }

        public ExecutionThreadWorkItem(bool value)
        {
            Type = WorkItemType.SetSyncToVerticalBlank;
            Object0 = null;
            Object1 = null;

            UInt0 = value ? 1u : 0u;
            UInt1 = 0;
            UInt2 = 0;
        }

        public ExecutionThreadWorkItem(WorkItemType type)
        {
            Type = type;
            Object0 = null;
            Object1 = null;

            UInt0 = 0;
            UInt1 = 0;
            UInt2 = 0;
        }

        public ExecutionThreadWorkItem(InitializeResourceInfo info)
        {
            Type = WorkItemType.InitializeResource;
            Object0 = info;
            Object1 = null;

            UInt0 = 0;
            UInt1 = 0;
            UInt2 = 0;
        }

        public ExecutionThreadWorkItem(WorkItemType type, Frame frame)
        {
            Type = type;
            Object0 = frame;
            Object1 = null;

            UInt0 = 0;
            UInt1 = 0;
            UInt2 = 0;
        }
    }

    private struct MapParams
    {
        public MapMode MapMode;
        public uint Subresource;
        public bool Map;
        public bool Succeeded;
        public IntPtr Data;
        public uint DataSize;
        public uint RowPitch;
        public uint DepthPitch;
    }

    internal struct MappedResourceInfoWithStaging
    {
        public int RefCount;
        public MapMode Mode;
        public MappedResource MappedResource;
        public StagingBlock StagingBlock;
    }

    private class InitializeResourceInfo
    {
        public OpenGLDeferredResource DeferredResource;
        public ManualResetEventSlim ResetEvent;
        public Exception Exception;

        public InitializeResourceInfo(OpenGLDeferredResource deferredResource, ManualResetEventSlim mre)
        {
            DeferredResource = deferredResource;
            ResetEvent = mre;
        }
    }
}
