using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

using Silk.NET.Core;
using Silk.NET.Core.Contexts;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Vulkan.Extensions.KHR;

using VkApi = Silk.NET.Vulkan.Vk;
using VkFenceHandle = Silk.NET.Vulkan.Fence;
using VkSemaphore = Silk.NET.Vulkan.Semaphore;

namespace Prowl.Graphite.Vk;

internal unsafe partial class VkGraphicsDevice : GraphicsDevice
{
    private static readonly FixedUtf8String s_name = "Prowl.Graphite-VkGraphicsDevice";
    private static readonly Lazy<bool> s_isSupported = new(CheckIsSupported, isThreadSafe: true);

    private readonly VkApi _vk = VkApi.GetApi();

    private Instance _instance;
    private PhysicalDevice _physicalDevice;
    private string _deviceName;
    private string _vendorName;
    private GraphicsApiVersion _apiVersion;
    private string _driverName;
    private string _driverInfo;
    private VkDeviceMemoryManager _memoryManager;
    private PhysicalDeviceProperties _physicalDeviceProperties;
    private PhysicalDeviceFeatures _physicalDeviceFeatures;
    private PhysicalDeviceMemoryProperties _physicalDeviceMemProperties;
    private Device _device;
    private uint _graphicsQueueIndex;
    private uint _presentQueueIndex;
    private CommandPool _graphicsCommandPool;
    private Queue _graphicsQueue;
    private readonly object _graphicsQueueLock = new();
    private DebugReportCallbackEXT _debugCallbackHandle;
    private PfnDebugReportCallbackEXT _debugCallbackFunc;
    private bool _debugMarkerEnabled;
    private vkDebugMarkerSetObjectNameEXT_t _setObjectNameDelegate;
    private vkCmdDebugMarkerBeginEXT_t _markerBegin;
    private vkCmdDebugMarkerEndEXT_t _markerEnd;
    private vkCmdDebugMarkerInsertEXT_t _markerInsert;
    private readonly ConcurrentDictionary<Format, Filter> _filters = new();
    private readonly BackendInfoVulkan _vulkanInfo;

    private ExtDebugReport _extDebugReport;
    private KhrSurface _khrSurface;
    private KhrSwapchain _khrSwapchain;

    private VkDescriptorPoolManager _descriptorPoolManager;

    // Pool of recyclable graph command buffers. Rented by render-graph passes and returned once their
    // GPU work retires, so a pass no longer allocates a fresh command pool + buffers every frame.
    private readonly object _graphCommandBufferPoolLock = new();
    private readonly Stack<VkCommandBuffer> _freeGraphCommandBuffers = new();
    private readonly List<VkCommandBuffer> _allGraphCommandBuffers = [];
    private bool _graphCommandBuffersDisposed;

    // Live per-shader descriptor-set caches, swept each frame to enforce the retention window even for
    // shaders that have stopped rendering. Registration may come from asset-load threads, so guarded.
    private readonly List<VkDescriptorSetCache> _descriptorSetCaches = [];
    private readonly object _descriptorSetCachesLock = new();
    private readonly Dictionary<Texture, VkTextureView> _defaultTextureViews = [];
    private readonly object _defaultTextureViewsLock = new();
    private bool _standardValidationSupported;
    private bool _khronosValidationSupported;
    private bool _standardClipYDirection;
    private vkGetBufferMemoryRequirements2_t? _getBufferMemoryRequirements2;
    private vkGetImageMemoryRequirements2_t? _getImageMemoryRequirements2;
    private vkGetPhysicalDeviceProperties2_t? _getPhysicalDeviceProperties2;

    private readonly VkBuffer _emptyUniformBuffer;
    private readonly VkBuffer _emptyStructuredBuffer;

    public override string DeviceName => _deviceName;

    public override string VendorName => _vendorName;

    public override GraphicsApiVersion ApiVersion => _apiVersion;

    public override GraphicsBackend BackendType => GraphicsBackend.Vulkan;

    public override bool IsUvOriginTopLeft => true;

    public override bool IsDepthRangeZeroToOne => true;

    public override bool IsClipSpaceYInverted => !_standardClipYDirection;

    public override Swapchain MainSwapchain => _mainSwapchain;

    public override GraphicsDeviceFeatures Features { get; }

    public override bool GetVulkanInfo(out BackendInfoVulkan info)
    {
        info = _vulkanInfo;
        return true;
    }

    public Instance Instance => _instance;
    public Device Device => _device;
    public VkApi Vk => _vk;
    public PhysicalDevice PhysicalDevice => _physicalDevice;
    public PhysicalDeviceMemoryProperties PhysicalDeviceMemProperties => _physicalDeviceMemProperties;
    public Queue GraphicsQueue => _graphicsQueue;
    public uint GraphicsQueueIndex => _graphicsQueueIndex;
    public uint PresentQueueIndex => _presentQueueIndex;
    public string DriverName => _driverName;
    public string DriverInfo => _driverInfo;
    public VkDeviceMemoryManager MemoryManager => _memoryManager;
    public VkDescriptorPoolManager DescriptorPoolManager => _descriptorPoolManager;

    internal void RegisterDescriptorSetCache(VkDescriptorSetCache cache)
    {
        lock (_descriptorSetCachesLock)
            _descriptorSetCaches.Add(cache);
    }

    internal void UnregisterDescriptorSetCache(VkDescriptorSetCache cache)
    {
        lock (_descriptorSetCachesLock)
            _descriptorSetCaches.Remove(cache);
    }

    /// <summary>
    /// Gets or creates full-range view for texture. Device-owned, lives til dispose.
    /// </summary>
    internal VkTextureView GetOrCreateDefaultView(VkTexture texture)
    {
        lock (_defaultTextureViewsLock)
        {
            if (!_defaultTextureViews.TryGetValue(texture, out VkTextureView? view))
            {
                view = (VkTextureView)ResourceFactory.CreateTextureView(texture);
                _defaultTextureViews[texture] = view;
            }
            return view;
        }
    }

    /// <summary>
    /// VkPipelineCache handle passed to every pipeline create call, speeds up compiles.
    /// </summary>
    internal PipelineCache DriverPipelineCache => _driverPipelineCache;
    public vkCmdDebugMarkerBeginEXT_t MarkerBegin => _markerBegin;
    public vkCmdDebugMarkerEndEXT_t MarkerEnd => _markerEnd;
    public vkCmdDebugMarkerInsertEXT_t MarkerInsert => _markerInsert;
    public vkGetBufferMemoryRequirements2_t? GetBufferMemoryRequirements2 => _getBufferMemoryRequirements2;
    public vkGetImageMemoryRequirements2_t? GetImageMemoryRequirements2 => _getImageMemoryRequirements2;
    public KhrSurface KhrSurface => _khrSurface;
    public KhrSwapchain KhrSwapchain => _khrSwapchain;

    private readonly VkSwapchain _mainSwapchain;

    private PipelineCache _driverPipelineCache;


    public VkGraphicsDevice(GraphicsDeviceOptions options, SwapchainDescription? scDesc)
        : this(options, scDesc, new VulkanDeviceOptions()) { }

    public VkGraphicsDevice(GraphicsDeviceOptions options, SwapchainDescription? scDesc, VulkanDeviceOptions vkOptions)
    {
        VkSurfaceSwapchainSource? surfaceSource = scDesc != null ?
            Util.AssertSubtype<SwapchainSource, VkSurfaceSwapchainSource>(scDesc.Value.Source) : null;

        CreateInstance(options.Debug, vkOptions, surfaceSource);

        SurfaceKHR surface = default;
        if (surfaceSource != null)
            surface = surfaceSource.GetSurface(Instance);

        CreatePhysicalDevice();
        CreateLogicalDevice(surface, options.PreferStandardClipSpaceYDirection, vkOptions);

        _memoryManager = new VkDeviceMemoryManager(
            _vk,
            _device,
            _physicalDevice,
            _physicalDeviceProperties.Limits.BufferImageGranularity,
            _getBufferMemoryRequirements2,
            _getImageMemoryRequirements2);

        Features = new GraphicsDeviceFeatures(
            computeShader: true,
            geometryShader: _physicalDeviceFeatures.GeometryShader,
            tessellationShaders: _physicalDeviceFeatures.TessellationShader,
            multipleViewports: _physicalDeviceFeatures.MultiViewport,
            samplerLodBias: true,
            drawBaseVertex: true,
            drawBaseInstance: true,
            drawIndirect: true,
            drawIndirectBaseInstance: _physicalDeviceFeatures.DrawIndirectFirstInstance,
            samplerAnisotropy: _physicalDeviceFeatures.SamplerAnisotropy,
            depthClipDisable: _physicalDeviceFeatures.DepthClamp,
            texture1D: true,
            independentBlend: _physicalDeviceFeatures.IndependentBlend,
            structuredBuffer: true,
            subsetTextureView: true,
            commandBufferDebugMarkers: _debugMarkerEnabled,
            bufferRangeBinding: true,
            shaderFloat64: _physicalDeviceFeatures.ShaderFloat64);

        ResourceFactory = new VkResourceFactory(this);

        InitializeFrameOptions(options);

        if (scDesc != null)
        {
            SwapchainDescription desc = scDesc.Value;
            _mainSwapchain = new VkSwapchain(this, ref desc, surface);
        }

        CreateDescriptorPool();
        CreateGraphicsCommandPool();

        PipelineCacheCreateInfo pcCI = new()
        {
            SType = StructureType.PipelineCacheCreateInfo,
            InitialDataSize = 0,
            PInitialData = null,
        };
        _vk.CreatePipelineCache(_device, in pcCI, null, out _driverPipelineCache).CheckResult();

        for (int i = 0; i < SharedCommandPoolCount; i++)
        {
            _sharedGraphicsCommandPools.Push(new SharedCommandPool(this, true));
        }

        _vulkanInfo = new BackendInfoVulkan(this);

        InitializeSlots();
        PostDeviceCreated();
    }

    public override ResourceFactory ResourceFactory { get; }

    // --------------- Execution lifecycle slot state ---------------

    private struct SlotState
    {
        public VkFenceHandle Fence;
        public VkFence FenceWrapper;
        public VkBuffer TransientPrimary;
        public byte* TransientMapped;
        public List<VkBuffer> TransientOverflow;
        public List<VkCommandBuffer> RentedCommandBuffers;
        public ulong CurrentExecutionId;
    }

    private SlotState[] _slots;
    private readonly List<VkBuffer> _transientFreePool = [];
    private readonly object _transientFreePoolLock = new();

    private void InitializeSlots()
    {
        _slots = new SlotState[_maxExecutingTasks];
        for (uint i = 0; i < _maxExecutingTasks; i++)
        {
            VkFence slotWrapper = new(this, false);

            VkBuffer primary = new(this, _transientInitialSize,
                BufferUsage.Dynamic | BufferUsage.UniformBuffer);
            primary.SetTransientWrites(true);
            primary.Name = $"TransientPrimary[{i}]";
            byte* mapped = (byte*)primary.Memory.BlockMappedPointer;

            _slots[i] = new SlotState
            {
                Fence = slotWrapper.DeviceFence,
                FenceWrapper = slotWrapper,
                TransientPrimary = primary,
                TransientMapped = mapped,
                TransientOverflow = [],
                RentedCommandBuffers = [],
                CurrentExecutionId = 0,
            };
        }
    }

    // --------------- Graph execution lifecycle implementations ---------------

    private protected override ExecutionTask BeginExecutionCore(ulong executionId, uint ringSlot)
    {
        ref SlotState slot = ref _slots[ringSlot];

        // The base class only hands out a slot whose previous execution has completed and been reclaimed,
        // so no fence wait is needed here; just recycle the slot's fence and transient memory.
        VkFenceHandle slotFence = slot.Fence;
        _vk.ResetFences(_device, 1, in slotFence).CheckResult();
        slot.FenceWrapper.Reset();

        // Return overflow buffers to the free pool and reset transient head
        if (slot.TransientOverflow.Count > 0)
        {
            lock (_transientFreePoolLock)
            {
                _transientFreePool.AddRange(slot.TransientOverflow);
            }
            slot.TransientOverflow.Clear();
        }

        // The slot's previous execution is complete, so its rented command buffers can be reclaimed.
        if (slot.RentedCommandBuffers.Count > 0)
        {
            foreach (VkCommandBuffer rented in slot.RentedCommandBuffers)
                ReturnGraphCommandBuffer(rented);
            slot.RentedCommandBuffers.Clear();
        }

        // Age out descriptor sets that have gone unused past the retention window. Safe: anything freed
        // is older than MaxExecutingTasks executions and therefore already GPU-retired.
        lock (_descriptorSetCachesLock)
        {
            foreach (VkDescriptorSetCache cache in _descriptorSetCaches)
                cache.Sweep(executionId, _maxExecutingTasks);
        }

        slot.CurrentExecutionId = executionId;

        return new VkExecutionTask(this, executionId, ringSlot, slot.FenceWrapper,
            slot.TransientPrimary, slot.TransientOverflow, slot.RentedCommandBuffers);
    }

    private protected override void CompleteExecutionCore(ExecutionTask task)
    {
        uint ringSlot = task.RingSlot;
        VkFenceHandle slotFence = _slots[ringSlot].Fence;

        SubmitInfo si = new(sType: StructureType.SubmitInfo);
        si.CommandBufferCount = 0;

        lock (_graphicsQueueLock)
        {
            _vk.QueueSubmit(_graphicsQueue, 1, in si, slotFence).CheckResult();
            FlushValidationErrors();
        }
    }

    private protected override bool IsExecutionCompleteCore(ExecutionTask task)
    {
        ref SlotState slot = ref _slots[task.RingSlot];

        // The slot was reused by a newer execution, so this one has definitely finished.
        if (slot.CurrentExecutionId != task.Id)
            return true;

        Result status = _vk.GetFenceStatus(_device, slot.Fence);
        return status == Result.Success;
    }

    private protected override bool WaitForExecutionCore(ExecutionTask task, ulong nanosecondTimeout)
    {
        ref SlotState slot = ref _slots[task.RingSlot];

        if (slot.CurrentExecutionId != task.Id)
            return true;

        VkFenceHandle fence = slot.Fence;
        Result result = _vk.WaitForFences(_device, 1, in fence, true, nanosecondTimeout);
        return result == Result.Success;
    }

    internal VkBuffer CreateTransientBuffer(uint sizeInBytes)
    {
        lock (_transientFreePoolLock)
        {
            for (int i = 0; i < _transientFreePool.Count; i++)
            {
                if (_transientFreePool[i].SizeInBytes >= sizeInBytes)
                {
                    VkBuffer buf = _transientFreePool[i];
                    _transientFreePool.RemoveAt(i);
                    return buf;
                }
            }
        }

        VkBuffer overflow = new(this, sizeInBytes, BufferUsage.Dynamic | BufferUsage.UniformBuffer);
        overflow.SetTransientWrites(true);
        overflow.Name = "TransientOverflow";
        return overflow;
    }

    internal override CommandBuffer RentGraphCommandBuffer()
    {
        lock (_graphCommandBufferPoolLock)
        {
            if (_freeGraphCommandBuffers.Count > 0)
                return _freeGraphCommandBuffers.Pop();
        }

        CommandBufferDescription desc = new();
        VkCommandBuffer cb = new(this, ref desc);
        lock (_graphCommandBufferPoolLock)
        {
            _allGraphCommandBuffers.Add(cb);
        }
        return cb;
    }

    // Returns a rented graph command buffer once its owning execution has retired (GPU-complete). Clean
    // buffers go back to the free list for reuse; a buffer left mid-recording can't be reset, so dispose it.
    internal void ReturnGraphCommandBuffer(VkCommandBuffer cb)
    {
        lock (_graphCommandBufferPoolLock)
        {
            if (_graphCommandBuffersDisposed)
                return;

            if (cb.CanRecycle)
            {
                _freeGraphCommandBuffers.Push(cb);
            }
            else
            {
                _allGraphCommandBuffers.Remove(cb);
                cb.Dispose();
            }
        }
    }

    /// <summary>Test hook: total distinct graph command buffers ever allocated.</summary>
    internal int PooledGraphCommandBufferCount
    {
        get { lock (_graphCommandBufferPoolLock) { return _allGraphCommandBuffers.Count; } }
    }

    private protected override void SwapBuffersCore(Swapchain swapchain)
    {
        VkSwapchain vkSC = Util.AssertSubtype<Swapchain, VkSwapchain>(swapchain);
        SwapchainKHR deviceSwapchain = vkSC.DeviceSwapchain;
        PresentInfoKHR presentInfo = new(sType: StructureType.PresentInfoKhr);
        presentInfo.SwapchainCount = 1;
        presentInfo.PSwapchains = &deviceSwapchain;
        uint imageIndex = vkSC.ImageIndex;
        presentInfo.PImageIndices = &imageIndex;

        object presentLock = vkSC.PresentQueueIndex == _graphicsQueueIndex ? _graphicsQueueLock : vkSC;
        lock (presentLock)
        {
            _khrSwapchain.QueuePresent(vkSC.PresentQueue, &presentInfo);
            if (vkSC.AcquireNextImage(_device, default, vkSC.ImageAvailableFence))
            {
                VkFenceHandle fence = vkSC.ImageAvailableFence;
                _vk.WaitForFences(_device, 1, &fence, true, ulong.MaxValue);
                _vk.ResetFences(_device, 1, &fence);
            }
        }
    }

    internal void SetResourceName(DeviceResource resource, string name)
    {
        if (_debugMarkerEnabled)
        {
            switch (resource)
            {
                case VkBuffer buffer:
                    SetDebugMarkerName(DebugReportObjectTypeEXT.BufferExt, buffer.DeviceBuffer.Handle, name);
                    break;
                case VkCommandBuffer CommandBuffer:
                    SetDebugMarkerName(
                        DebugReportObjectTypeEXT.CommandBufferExt,
                        (ulong)CommandBuffer.CommandBuffer.Handle,
                        string.Format("{0}_CommandBuffer", name));
                    SetDebugMarkerName(
                        DebugReportObjectTypeEXT.CommandPoolExt,
                        CommandBuffer.CommandPool.Handle,
                        string.Format("{0}_CommandPool", name));
                    break;
                case VkFramebuffer framebuffer:
                    SetDebugMarkerName(
                        DebugReportObjectTypeEXT.FramebufferExt,
                        framebuffer.CurrentFramebuffer.Handle,
                        name);
                    break;
                case VkSampler sampler:
                    SetDebugMarkerName(DebugReportObjectTypeEXT.SamplerExt, sampler.DeviceSampler.Handle, name);
                    break;
                case VkGraphicsProgram shaderProgram:
                    foreach (ShaderModule module in shaderProgram.Modules.Values)
                    {
                        SetDebugMarkerName(DebugReportObjectTypeEXT.ShaderModuleExt, module.Handle, name);
                    }
                    break;
                case VkComputeProgram computeProgram:
                    SetDebugMarkerName(DebugReportObjectTypeEXT.PipelineExt, computeProgram.DevicePipeline.Handle, name);
                    break;
                case VkTexture tex:
                    SetDebugMarkerName(DebugReportObjectTypeEXT.ImageExt, tex.OptimalDeviceImage.Handle, name);
                    break;
                case VkTextureView texView:
                    SetDebugMarkerName(DebugReportObjectTypeEXT.ImageViewExt, texView.ImageView.Handle, name);
                    break;
                case VkFence fence:
                    SetDebugMarkerName(DebugReportObjectTypeEXT.FenceExt, fence.DeviceFence.Handle, name);
                    break;
                case VkSwapchain sc:
                    SetDebugMarkerName(DebugReportObjectTypeEXT.SwapchainKhrExt, sc.DeviceSwapchain.Handle, name);
                    break;
                default:
                    break;
            }
        }
    }

    private void SetDebugMarkerName(DebugReportObjectTypeEXT type, ulong target, string name)
    {
        Debug.Assert(_setObjectNameDelegate != null);

        DebugMarkerObjectNameInfoEXT nameInfo = new(sType: StructureType.DebugMarkerObjectNameInfoExt);
        nameInfo.ObjectType = type;
        nameInfo.Object = target;

        byte* utf8Ptr = stackalloc byte[Utf8Stack.ByteCount(name)];
        Utf8Stack.Write(name, utf8Ptr);

        nameInfo.PObjectName = utf8Ptr;
        _setObjectNameDelegate(_device, &nameInfo).CheckResult();
    }

    /// <summary>
    /// Throws if Vulkan reported a validation error. Call after ops that could trigger one.
    /// </summary>
    internal static void FlushValidationErrors()
    {
        if (_lastValidationError == null)
            return;

        string error = _lastValidationError;
        _lastValidationError = null;
        throw new RenderException("A Vulkan validation error was encountered: " + error);
    }

    protected override MappedResource MapCore(MappableResource resource, MapMode mode, uint subresource)
    {
        VkMemoryBlock memoryBlock = default;
        IntPtr mappedPtr = IntPtr.Zero;
        uint sizeInBytes;
        uint offset = 0;
        uint rowPitch = 0;
        uint depthPitch = 0;
        if (resource is VkBuffer buffer)
        {
            memoryBlock = buffer.Memory;
            sizeInBytes = buffer.SizeInBytes;
        }
        else
        {
            VkTexture texture = Util.AssertSubtype<MappableResource, VkTexture>(resource);
            SubresourceLayout layout = texture.GetSubresourceLayout(subresource);
            memoryBlock = texture.Memory;
            sizeInBytes = (uint)layout.Size;
            offset = (uint)layout.Offset;
            rowPitch = (uint)layout.RowPitch;
            depthPitch = (uint)layout.DepthPitch;
        }

        if (memoryBlock.DeviceMemory.Handle != 0)
        {
            if (memoryBlock.IsPersistentMapped)
            {
                mappedPtr = (IntPtr)memoryBlock.BlockMappedPointer;
            }
            else
            {
                mappedPtr = _memoryManager.Map(memoryBlock);
            }
        }

        byte* dataPtr = (byte*)mappedPtr.ToPointer() + offset;
        return new MappedResource(
            resource,
            mode,
            (IntPtr)dataPtr,
            sizeInBytes,
            subresource,
            rowPitch,
            depthPitch);
    }

    protected override void UnmapCore(MappableResource resource, uint subresource)
    {
        VkMemoryBlock memoryBlock = default;
        if (resource is VkBuffer buffer)
        {
            memoryBlock = buffer.Memory;
        }
        else
        {
            VkTexture tex = Util.AssertSubtype<MappableResource, VkTexture>(resource);
            memoryBlock = tex.Memory;
        }

        if (memoryBlock.DeviceMemory.Handle != 0 && !memoryBlock.IsPersistentMapped)
        {
            _vk.UnmapMemory(_device, memoryBlock.DeviceMemory);
        }
    }

    protected override void PlatformDispose()
    {
        if (_slots != null)
        {
            foreach (ref SlotState slot in _slots.AsSpan())
            {
                slot.TransientPrimary?.Dispose();
                foreach (VkBuffer overflow in slot.TransientOverflow)
                    overflow.Dispose();
                slot.FenceWrapper?.Dispose();
            }
        }

        lock (_transientFreePoolLock)
        {
            foreach (VkBuffer buf in _transientFreePool)
                buf.Dispose();
            _transientFreePool.Clear();
        }

        Debug.Assert(_submittedFences.Count == 0);
        foreach (VkFenceHandle fence in _availableSubmissionFences)
        {
            _vk.DestroyFence(_device, fence, null);
        }

        _mainSwapchain?.Dispose();
        if (_debugCallbackFunc.Handle != default)
        {
            _extDebugReport?.DestroyDebugReportCallback(_instance, _debugCallbackHandle, null);
        }

        lock (_graphCommandBufferPoolLock)
        {
            _graphCommandBuffersDisposed = true;
            foreach (VkCommandBuffer cb in _allGraphCommandBuffers)
                cb.Dispose();
            _allGraphCommandBuffers.Clear();
            _freeGraphCommandBuffers.Clear();
        }

        _descriptorPoolManager.DestroyAll();
        foreach (VkTextureView view in _defaultTextureViews.Values)
            view.Dispose();
        _vk.DestroyCommandPool(_device, _graphicsCommandPool, null);

        Debug.Assert(_submittedStagingTextures.Count == 0);
        foreach (VkTexture tex in _availableStagingTextures)
        {
            tex.Dispose();
        }

        Debug.Assert(_submittedStagingBuffers.Count == 0);
        foreach (VkBuffer buffer in _availableStagingBuffers)
        {
            buffer.Dispose();
        }

        lock (_graphicsCommandPoolLock)
        {
            while (_sharedGraphicsCommandPools.Count > 0)
            {
                SharedCommandPool sharedPool = _sharedGraphicsCommandPools.Pop();
                sharedPool.Destroy();
            }
        }

        _vk.DestroyPipelineCache(_device, _driverPipelineCache, null);

        _memoryManager.Dispose();

        _vk.DeviceWaitIdle(_device).CheckResult();

        _vk.DestroyDevice(_device, null);
        _vk.DestroyInstance(_instance, null);
    }

    private protected override void WaitForIdleCore()
    {
        lock (_graphicsQueueLock)
        {
            _vk.QueueWaitIdle(_graphicsQueue);
        }

        CheckSubmittedFences();
        FlushValidationErrors();
    }

    public override TextureSampleCount GetSampleCountLimit(PixelFormat format, bool depthFormat)
    {
        ImageUsageFlags usageFlags = ImageUsageFlags.SampledBit;
        usageFlags |= depthFormat ? ImageUsageFlags.DepthStencilAttachmentBit : ImageUsageFlags.ColorAttachmentBit;

        _vk.GetPhysicalDeviceImageFormatProperties(
            _physicalDevice,
            VkFormats.VdToVkPixelFormat(format),
            ImageType.Type2D,
            ImageTiling.Optimal,
            usageFlags,
            ImageCreateFlags.None,
            out ImageFormatProperties formatProperties);

        SampleCountFlags vkSampleCounts = formatProperties.SampleCounts;
        if ((vkSampleCounts & SampleCountFlags.Count32Bit) == SampleCountFlags.Count32Bit)
        {
            return TextureSampleCount.Count32;
        }
        else if ((vkSampleCounts & SampleCountFlags.Count16Bit) == SampleCountFlags.Count16Bit)
        {
            return TextureSampleCount.Count16;
        }
        else if ((vkSampleCounts & SampleCountFlags.Count8Bit) == SampleCountFlags.Count8Bit)
        {
            return TextureSampleCount.Count8;
        }
        else if ((vkSampleCounts & SampleCountFlags.Count4Bit) == SampleCountFlags.Count4Bit)
        {
            return TextureSampleCount.Count4;
        }
        else if ((vkSampleCounts & SampleCountFlags.Count2Bit) == SampleCountFlags.Count2Bit)
        {
            return TextureSampleCount.Count2;
        }

        return TextureSampleCount.Count1;
    }

    private protected override bool GetPixelFormatSupportCore(
        PixelFormat format,
        TextureType type,
        TextureUsage usage,
        out PixelFormatProperties properties)
    {
        Format vkFormat = VkFormats.VdToVkPixelFormat(format, (usage & TextureUsage.DepthStencil) != 0);
        ImageType vkType = VkFormats.VdToVkTextureType(type);
        ImageTiling tiling = usage == TextureUsage.Staging ? ImageTiling.Linear : ImageTiling.Optimal;
        ImageUsageFlags vkUsage = VkFormats.VdToVkTextureUsage(usage);

        Result result = _vk.GetPhysicalDeviceImageFormatProperties(
            _physicalDevice,
            vkFormat,
            vkType,
            tiling,
            vkUsage,
            ImageCreateFlags.None,
            out ImageFormatProperties vkProps);

        if (result == Result.ErrorFormatNotSupported)
        {
            properties = default;
            return false;
        }

        result.CheckResult();

        properties = new PixelFormatProperties(
           vkProps.MaxExtent.Width,
           vkProps.MaxExtent.Height,
           vkProps.MaxExtent.Depth,
           vkProps.MaxMipLevels,
           vkProps.MaxArrayLayers,
           (uint)vkProps.SampleCounts);
        return true;
    }

    internal Filter GetFormatFilter(Format format)
    {
        if (!_filters.TryGetValue(format, out Filter filter))
        {
            _vk.GetPhysicalDeviceFormatProperties(_physicalDevice, format, out FormatProperties vkFormatProps);
            filter = (vkFormatProps.OptimalTilingFeatures & FormatFeatureFlags.SampledImageFilterLinearBit) != 0
                ? Filter.Linear
                : Filter.Nearest;
            _filters.TryAdd(format, filter);
        }

        return filter;
    }

    public override void ResetFence(Fence fence)
    {
        VkFenceHandle vkFence = Util.AssertSubtype<Fence, Prowl.Graphite.Vk.VkFence>(fence).DeviceFence;
        _vk.ResetFences(_device, 1, &vkFence);
    }

    public override bool WaitForFence(Fence fence, ulong nanosecondTimeout)
    {
        VkFenceHandle vkFence = Util.AssertSubtype<Fence, Prowl.Graphite.Vk.VkFence>(fence).DeviceFence;
        Result result = _vk.WaitForFences(_device, 1, &vkFence, true, nanosecondTimeout);
        return result == Result.Success;
    }

    internal static bool IsSupported()
    {
        return s_isSupported.Value;
    }

    private static bool CheckIsSupported()
    {
        using var vk = VkApi.GetApi();

        if (!vk.IsLoaded())
            return false;

        InstanceCreateInfo instanceCI = new(sType: StructureType.InstanceCreateInfo);
        ApplicationInfo applicationInfo = new(sType: StructureType.ApplicationInfo);
        applicationInfo.ApiVersion = new Version32(1, 0, 0);
        applicationInfo.ApplicationVersion = new Version32(1, 0, 0);
        applicationInfo.EngineVersion = new Version32(1, 0, 0);
        applicationInfo.PApplicationName = s_name;
        applicationInfo.PEngineName = s_name;

        instanceCI.PApplicationInfo = &applicationInfo;

        Result result = vk.CreateInstance(in instanceCI, null, out Instance testInstance);
        if (result != Result.Success)
        {
            return false;
        }

        uint physicalDeviceCount = 0;
        result = vk.EnumeratePhysicalDevices(testInstance, ref physicalDeviceCount, null);
        if (result != Result.Success || physicalDeviceCount == 0)
        {
            vk.DestroyInstance(testInstance, null);
            return false;
        }

        vk.DestroyInstance(testInstance, null);

        HashSet<string> instanceExtensions = [.. vk.EnumerateInstanceExtensionProperties((byte*)0)];

        if (!instanceExtensions.Contains(CommonStrings.VK_KHR_SURFACE_EXTENSION_NAME))
        {
            return false;
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return instanceExtensions.Contains(CommonStrings.VK_KHR_WIN32_SURFACE_EXTENSION_NAME);
        }
#if NET5_0_OR_GREATER
        else if (OperatingSystem.IsAndroid())
        {
            return instanceExtensions.Contains(CommonStrings.VK_KHR_ANDROID_SURFACE_EXTENSION_NAME);
        }
#endif
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            if (RuntimeInformation.OSDescription.Contains("Unix")) // Android
            {
                return instanceExtensions.Contains(CommonStrings.VK_KHR_ANDROID_SURFACE_EXTENSION_NAME);
            }
            else
            {
                return instanceExtensions.Contains(CommonStrings.VK_KHR_XLIB_SURFACE_EXTENSION_NAME);
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            if (RuntimeInformation.OSDescription.Contains("Darwin")) // macOS
            {
                return instanceExtensions.Contains(CommonStrings.VK_MVK_MACOS_SURFACE_EXTENSION_NAME);
            }
            else // iOS
            {
                return instanceExtensions.Contains(CommonStrings.VK_MVK_IOS_SURFACE_EXTENSION_NAME);
            }
        }

        return false;
    }

    internal void ClearColorTexture(VkTexture texture, ClearColorValue color)
    {
        uint effectiveLayers = texture.ArrayLayers;
        if ((texture.Usage & TextureUsage.Cubemap) != 0)
        {
            effectiveLayers *= 6;
        }
        ImageSubresourceRange range = new(
             ImageAspectFlags.ColorBit,
             0,
             texture.MipLevels,
             0,
             effectiveLayers);
        SharedCommandPool pool = GetFreeCommandPool();
        Silk.NET.Vulkan.CommandBuffer cb = pool.BeginNewCommandBuffer();
        texture.TransitionImageLayout(cb, 0, texture.MipLevels, 0, effectiveLayers, ImageLayout.TransferDstOptimal);
        _vk.CmdClearColorImage(cb, texture.OptimalDeviceImage, ImageLayout.TransferDstOptimal, &color, 1, &range);
        ImageLayout colorLayout = texture.IsSwapchainTexture ? ImageLayout.PresentSrcKhr : ImageLayout.ColorAttachmentOptimal;
        texture.TransitionImageLayout(cb, 0, texture.MipLevels, 0, effectiveLayers, colorLayout);
        pool.EndAndSubmit(cb);
    }

    internal void ClearDepthTexture(VkTexture texture, ClearDepthStencilValue clearValue)
    {
        uint effectiveLayers = texture.ArrayLayers;
        if ((texture.Usage & TextureUsage.Cubemap) != 0)
        {
            effectiveLayers *= 6;
        }
        ImageAspectFlags aspect = FormatHelpers.IsStencilFormat(texture.Format)
            ? ImageAspectFlags.DepthBit | ImageAspectFlags.StencilBit
            : ImageAspectFlags.DepthBit;
        ImageSubresourceRange range = new(
            aspect,
            0,
            texture.MipLevels,
            0,
            effectiveLayers);
        SharedCommandPool pool = GetFreeCommandPool();
        Silk.NET.Vulkan.CommandBuffer cb = pool.BeginNewCommandBuffer();
        texture.TransitionImageLayout(cb, 0, texture.MipLevels, 0, effectiveLayers, ImageLayout.TransferDstOptimal);
        _vk.CmdClearDepthStencilImage(
            cb,
            texture.OptimalDeviceImage,
            ImageLayout.TransferDstOptimal,
            &clearValue,
            1,
            &range);
        texture.TransitionImageLayout(cb, 0, texture.MipLevels, 0, effectiveLayers, ImageLayout.DepthStencilAttachmentOptimal);
        pool.EndAndSubmit(cb);
    }

    internal override uint GetUniformBufferMinOffsetAlignmentCore()
        => (uint)_physicalDeviceProperties.Limits.MinUniformBufferOffsetAlignment;

    internal override uint GetStructuredBufferMinOffsetAlignmentCore()
        => (uint)_physicalDeviceProperties.Limits.MinStorageBufferOffsetAlignment;

    internal void TransitionImageLayout(VkTexture texture, ImageLayout layout)
    {
        SharedCommandPool pool = GetFreeCommandPool();
        Silk.NET.Vulkan.CommandBuffer cb = pool.BeginNewCommandBuffer();
        texture.TransitionImageLayout(cb, 0, texture.MipLevels, 0, texture.ActualArrayLayers, layout);
        pool.EndAndSubmit(cb);
    }

}
