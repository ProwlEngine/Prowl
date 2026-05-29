using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Core;
using Silk.NET.Core.Native;

using static Prowl.Veldrid.Vk.VulkanUtil;

using VkApi = Silk.NET.Vulkan.Vk;
using VkSemaphore = Silk.NET.Vulkan.Semaphore;
using VkFenceHandle = Silk.NET.Vulkan.Fence;
using Silk.NET.Core.Contexts;

namespace Prowl.Veldrid.Vk;

internal unsafe class VkGraphicsDevice : GraphicsDevice
{
    private const uint VK_INSTANCE_CREATE_ENUMERATE_PORTABILITY_BIT_KHR = 0x00000001;
    private static readonly FixedUtf8String s_name = "Prowl.Veldrid-VkGraphicsDevice";
    private static readonly Lazy<bool> s_isSupported = new Lazy<bool>(CheckIsSupported, isThreadSafe: true);

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
    private readonly object _graphicsCommandPoolLock = new object();
    private Queue _graphicsQueue;
    private readonly object _graphicsQueueLock = new object();
    private DebugReportCallbackEXT _debugCallbackHandle;
    private PfnDebugReportCallbackEXT _debugCallbackFunc;
    private bool _debugMarkerEnabled;
    private vkDebugMarkerSetObjectNameEXT_t _setObjectNameDelegate;
    private vkCmdDebugMarkerBeginEXT_t _markerBegin;
    private vkCmdDebugMarkerEndEXT_t _markerEnd;
    private vkCmdDebugMarkerInsertEXT_t _markerInsert;
    private readonly ConcurrentDictionary<Format, Filter> _filters = new ConcurrentDictionary<Format, Filter>();
    private readonly BackendInfoVulkan _vulkanInfo;

    private ExtDebugReport _extDebugReport;
    private KhrSurface _khrSurface;
    private KhrSwapchain _khrSwapchain;

    private const int SharedCommandPoolCount = 4;
    private Stack<SharedCommandPool> _sharedGraphicsCommandPools = new Stack<SharedCommandPool>();
    private VkDescriptorPoolManager _descriptorPoolManager;
    private VkDescriptorPoolManager[] _frameDescriptorPools;
    private readonly Dictionary<Texture, VkTextureView> _defaultTextureViews = new();
    private readonly object _defaultTextureViewsLock = new object();
    private bool _standardValidationSupported;
    private bool _khronosValidationSupported;
    private bool _standardClipYDirection;
    private vkGetBufferMemoryRequirements2_t _getBufferMemoryRequirements2;
    private vkGetImageMemoryRequirements2_t _getImageMemoryRequirements2;
    private vkGetPhysicalDeviceProperties2_t _getPhysicalDeviceProperties2;

    // Staging Resources
    private const uint MinStagingBufferSize = 64;
    private const uint MaxStagingBufferSize = 512;

    private readonly object _stagingResourcesLock = new object();
    private readonly List<VkTexture> _availableStagingTextures = new List<VkTexture>();
    private readonly List<VkBuffer> _availableStagingBuffers = new List<VkBuffer>();

    private readonly Dictionary<Silk.NET.Vulkan.CommandBuffer, VkTexture> _submittedStagingTextures
        = new Dictionary<Silk.NET.Vulkan.CommandBuffer, VkTexture>();
    private readonly Dictionary<Silk.NET.Vulkan.CommandBuffer, VkBuffer> _submittedStagingBuffers
        = new Dictionary<Silk.NET.Vulkan.CommandBuffer, VkBuffer>();
    private readonly Dictionary<Silk.NET.Vulkan.CommandBuffer, SharedCommandPool> _submittedSharedCommandPools
        = new Dictionary<Silk.NET.Vulkan.CommandBuffer, SharedCommandPool>();

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

    /// <summary>Returns the per-frame descriptor pool for <paramref name="ringSlot"/>. Reset at each BeginFrame.</summary>
    public VkDescriptorPoolManager GetFrameDescriptorPool(uint ringSlot) => _frameDescriptorPools[ringSlot];

    /// <summary>
    /// Returns a lazily-created full-range <see cref="VkTextureView"/> for <paramref name="texture"/>.
    /// Views are device-owned and live until <see cref="PlatformDispose"/> is called.
    /// </summary>
    internal VkTextureView GetOrCreateDefaultView(VkTexture texture)
    {
        lock (_defaultTextureViewsLock)
        {
            if (!_defaultTextureViews.TryGetValue(texture, out VkTextureView view))
            {
                view = (VkTextureView)ResourceFactory.CreateTextureView(texture);
                _defaultTextureViews[texture] = view;
            }
            return view;
        }
    }

    /// <summary>
    /// Driver-side <c>VkPipelineCache</c> handle passed to every
    /// <c>vkCreateGraphicsPipelines</c> call to amortize driver pipeline compile cost.
    /// </summary>
    internal PipelineCache DriverPipelineCache => _driverPipelineCache;
    public vkCmdDebugMarkerBeginEXT_t MarkerBegin => _markerBegin;
    public vkCmdDebugMarkerEndEXT_t MarkerEnd => _markerEnd;
    public vkCmdDebugMarkerInsertEXT_t MarkerInsert => _markerInsert;
    public vkGetBufferMemoryRequirements2_t GetBufferMemoryRequirements2 => _getBufferMemoryRequirements2;
    public vkGetImageMemoryRequirements2_t GetImageMemoryRequirements2 => _getImageMemoryRequirements2;
    public KhrSurface KhrSurface => _khrSurface;
    public KhrSwapchain KhrSwapchain => _khrSwapchain;

    private readonly object _submittedFencesLock = new object();
    private readonly ConcurrentQueue<VkFenceHandle> _availableSubmissionFences = new ConcurrentQueue<VkFenceHandle>();
    private readonly List<FenceSubmissionInfo> _submittedFences = new List<FenceSubmissionInfo>();
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
            CommandBufferDebugMarkers: _debugMarkerEnabled,
            bufferRangeBinding: true,
            shaderFloat64: _physicalDeviceFeatures.ShaderFloat64);

        ResourceFactory = new VkResourceFactory(this);

        if (scDesc != null)
        {
            SwapchainDescription desc = scDesc.Value;
            _mainSwapchain = new VkSwapchain(this, ref desc, surface);
        }

        CreateDescriptorPool();
        CreateGraphicsCommandPool();

        PipelineCacheCreateInfo pcCI = new PipelineCacheCreateInfo
        {
            SType = StructureType.PipelineCacheCreateInfo,
            InitialDataSize = 0,
            PInitialData = null,
        };
        Result pcResult = _vk.CreatePipelineCache(_device, in pcCI, null, out _driverPipelineCache);
        CheckResult(pcResult);

        for (int i = 0; i < SharedCommandPoolCount; i++)
        {
            _sharedGraphicsCommandPools.Push(new SharedCommandPool(this, true));
        }

        _vulkanInfo = new BackendInfoVulkan(this);

        InitializeFrameOptions(options);
        InitializeSlots();
        PostDeviceCreated();
    }

    public override ResourceFactory ResourceFactory { get; }

    // --------------- Frame lifecycle slot state ---------------

    private struct SlotState
    {
        public VkFenceHandle Fence;
        public VkFence FenceWrapper;
        public VkBuffer TransientPrimary;
        public byte* TransientMapped;
        public List<VkBuffer> TransientOverflow;
        public ulong CurrentFrameId;
    }

    private SlotState[] _slots;
    private readonly List<VkBuffer> _transientFreePool = new List<VkBuffer>();
    private readonly object _transientFreePoolLock = new object();

    private void InitializeSlots()
    {
        _slots = new SlotState[_maxFramesInFlight];
        _frameDescriptorPools = new VkDescriptorPoolManager[_maxFramesInFlight];
        for (uint i = 0; i < _maxFramesInFlight; i++)
        {
            _frameDescriptorPools[i] = new VkDescriptorPoolManager(this, freeDescriptorSets: false);
            VkFence slotWrapper = new VkFence(this, false);

            VkBuffer primary = new VkBuffer(this, _transientInitialSize,
                BufferUsage.Dynamic | BufferUsage.VertexBuffer | BufferUsage.IndexBuffer
                | BufferUsage.UniformBuffer | BufferUsage.StructuredBufferReadOnly);
            byte* mapped = (byte*)primary.Memory.BlockMappedPointer;

            _slots[i] = new SlotState
            {
                Fence = slotWrapper.DeviceFence,
                FenceWrapper = slotWrapper,
                TransientPrimary = primary,
                TransientMapped = mapped,
                TransientOverflow = new List<VkBuffer>(),
                CurrentFrameId = 0,
            };
        }
    }

    // --------------- Frame lifecycle implementations ---------------

    private protected override Frame BeginFrameCore(ulong frameId, uint ringSlot)
    {
        ref SlotState slot = ref _slots[ringSlot];

        if (slot.CurrentFrameId != 0)
        {
            VkFenceHandle fence = slot.Fence;
            CheckResult(_vk.WaitForFences(_device, 1, in fence, true, ulong.MaxValue));
            ulong completed = slot.CurrentFrameId;
            Volatile.Write(ref _lastCompletedFrameId, Math.Max(Volatile.Read(ref _lastCompletedFrameId), completed));
        }

        VkFenceHandle slotFence = slot.Fence;
        CheckResult(_vk.ResetFences(_device, 1, in slotFence));
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

        // Reset the per-frame descriptor pool for this slot (safe: prior frame's fence was waited above).
        _frameDescriptorPools[ringSlot].ResetAll();

        slot.CurrentFrameId = frameId;

        return new VkFrame(this, frameId, ringSlot, slot.FenceWrapper,
            slot.TransientPrimary, slot.TransientOverflow);
    }

    private protected override unsafe void EndFrameCore(Frame frame)
    {
        uint ringSlot = frame.RingSlot;
        VkFenceHandle slotFence = _slots[ringSlot].Fence;

        SubmitInfo si = new SubmitInfo(sType: StructureType.SubmitInfo);
        si.CommandBufferCount = 0;

        lock (_graphicsQueueLock)
        {
            CheckResult(_vk.QueueSubmit(_graphicsQueue, 1, in si, slotFence));
            FlushValidationErrors();
        }
    }

    private protected override bool IsFrameCompleteCore(ulong frameId)
    {
        uint ringSlot = (uint)((frameId - 1) % _maxFramesInFlight);
        ref SlotState slot = ref _slots[ringSlot];

        if (slot.CurrentFrameId > frameId)
            return true;

        if (slot.CurrentFrameId == frameId)
        {
            Result status = _vk.GetFenceStatus(_device, slot.Fence);
            return status == Result.Success;
        }

        return true;
    }

    private protected override bool WaitForFrameCore(ulong frameId, ulong nanosecondTimeout)
    {
        uint ringSlot = (uint)((frameId - 1) % _maxFramesInFlight);
        ref SlotState slot = ref _slots[ringSlot];

        if (slot.CurrentFrameId > frameId)
            return true;

        if (slot.CurrentFrameId == frameId)
        {
            VkFenceHandle fence = slot.Fence;
            Result result = _vk.WaitForFences(_device, 1, in fence, true, nanosecondTimeout);
            return result == Result.Success;
        }

        return true;
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

        return new VkBuffer(this, sizeInBytes, BufferUsage.Dynamic | BufferUsage.UniformBuffer);
    }

    internal void SubmitCommandBufferInternal(CommandBuffer cl)
    {
        SubmitCommandBuffer(cl, 0, null, 0, null, null);
    }

    private void SubmitCommandBuffer(
        CommandBuffer cl,
        uint waitSemaphoreCount,
        VkSemaphore* waitSemaphoresPtr,
        uint signalSemaphoreCount,
        VkSemaphore* signalSemaphoresPtr,
        Fence fence)
    {
        VkCommandBuffer vkCL = Util.AssertSubtype<CommandBuffer, VkCommandBuffer>(cl);
        Silk.NET.Vulkan.CommandBuffer vkCB = vkCL.CommandBuffer;

        vkCL.CommandBufferSubmitted(vkCB);
        SubmitCommandBuffer(vkCL, vkCB, waitSemaphoreCount, waitSemaphoresPtr, signalSemaphoreCount, signalSemaphoresPtr, fence);
    }

    private void SubmitCommandBuffer(
        VkCommandBuffer vkCL,
        Silk.NET.Vulkan.CommandBuffer vkCB,
        uint waitSemaphoreCount,
        VkSemaphore* waitSemaphoresPtr,
        uint signalSemaphoreCount,
        VkSemaphore* signalSemaphoresPtr,
        Fence fence)
    {
        CheckSubmittedFences();

        bool useExtraFence = fence != null;
        SubmitInfo si = new SubmitInfo(sType: StructureType.SubmitInfo);
        si.CommandBufferCount = 1;
        si.PCommandBuffers = &vkCB;
        PipelineStageFlags waitDstStageMask = PipelineStageFlags.ColorAttachmentOutputBit;
        si.PWaitDstStageMask = &waitDstStageMask;

        si.PWaitSemaphores = waitSemaphoresPtr;
        si.WaitSemaphoreCount = waitSemaphoreCount;
        si.PSignalSemaphores = signalSemaphoresPtr;
        si.SignalSemaphoreCount = signalSemaphoreCount;

        VkFenceHandle vkFence = default;
        VkFenceHandle submissionFence = default;
        if (useExtraFence)
        {
            vkFence = Util.AssertSubtype<Fence, Prowl.Veldrid.Vk.VkFence>(fence).DeviceFence;
            submissionFence = GetFreeSubmissionFence();
        }
        else
        {
            vkFence = GetFreeSubmissionFence();
            submissionFence = vkFence;
        }

        lock (_graphicsQueueLock)
        {
            Result result = _vk.QueueSubmit(_graphicsQueue, 1, &si, vkFence);
            CheckResult(result);
            FlushValidationErrors();
            if (useExtraFence)
            {
                result = _vk.QueueSubmit(_graphicsQueue, 0, (SubmitInfo*)null, submissionFence);
                CheckResult(result);
            }
        }

        lock (_submittedFencesLock)
        {
            _submittedFences.Add(new FenceSubmissionInfo(submissionFence, vkCL, vkCB));
        }
    }

    private void CheckSubmittedFences()
    {
        lock (_submittedFencesLock)
        {
            for (int i = 0; i < _submittedFences.Count; i++)
            {
                FenceSubmissionInfo fsi = _submittedFences[i];
                if (_vk.GetFenceStatus(_device, fsi.Fence) == Result.Success)
                {
                    CompleteFenceSubmission(fsi);
                    _submittedFences.RemoveAt(i);
                    i -= 1;
                }
                else
                {
                    break; // Submissions are in order; later submissions cannot complete if this one hasn't.
                }
            }
        }
    }

    private void CompleteFenceSubmission(FenceSubmissionInfo fsi)
    {
        VkFenceHandle fence = fsi.Fence;
        Silk.NET.Vulkan.CommandBuffer completedCB = fsi.VulkanCommandBuffer;
        fsi.CommandBuffer?.CommandBufferCompleted(completedCB);
        Result resetResult = _vk.ResetFences(_device, 1, &fence);
        CheckResult(resetResult);
        ReturnSubmissionFence(fence);
        lock (_stagingResourcesLock)
        {
            if (_submittedStagingTextures.TryGetValue(completedCB, out VkTexture stagingTex))
            {
                _submittedStagingTextures.Remove(completedCB);
                _availableStagingTextures.Add(stagingTex);
            }
            if (_submittedStagingBuffers.TryGetValue(completedCB, out VkBuffer stagingBuffer))
            {
                _submittedStagingBuffers.Remove(completedCB);
                if (stagingBuffer.SizeInBytes <= MaxStagingBufferSize)
                {
                    _availableStagingBuffers.Add(stagingBuffer);
                }
                else
                {
                    stagingBuffer.Dispose();
                }
            }
            if (_submittedSharedCommandPools.TryGetValue(completedCB, out SharedCommandPool sharedPool))
            {
                _submittedSharedCommandPools.Remove(completedCB);
                lock (_graphicsCommandPoolLock)
                {
                    if (sharedPool.IsCached)
                    {
                        _sharedGraphicsCommandPools.Push(sharedPool);
                    }
                    else
                    {
                        sharedPool.Destroy();
                    }
                }
            }
        }
    }

    private void ReturnSubmissionFence(VkFenceHandle fence)
    {
        _availableSubmissionFences.Enqueue(fence);
    }

    private VkFenceHandle GetFreeSubmissionFence()
    {
        if (_availableSubmissionFences.TryDequeue(out VkFenceHandle availableFence))
        {
            return availableFence;
        }
        else
        {
            FenceCreateInfo fenceCI = new FenceCreateInfo(sType: StructureType.FenceCreateInfo);
            VkFenceHandle newFence;
            Result result = _vk.CreateFence(_device, &fenceCI, null, &newFence);
            CheckResult(result);
            return newFence;
        }
    }

    private protected override void SwapBuffersCore(Swapchain swapchain)
    {
        VkSwapchain vkSC = Util.AssertSubtype<Swapchain, VkSwapchain>(swapchain);
        SwapchainKHR deviceSwapchain = vkSC.DeviceSwapchain;
        PresentInfoKHR presentInfo = new PresentInfoKHR(sType: StructureType.PresentInfoKhr);
        presentInfo.SwapchainCount = 1;
        presentInfo.PSwapchains = &deviceSwapchain;
        uint imageIndex = vkSC.ImageIndex;
        presentInfo.PImageIndices = &imageIndex;

        object presentLock = vkSC.PresentQueueIndex == _graphicsQueueIndex ? _graphicsQueueLock : vkSC;
        lock (presentLock)
        {
            _khrSwapchain.QueuePresent(vkSC.PresentQueue, &presentInfo);
            if (vkSC.AcquireNextImage(_device, default(VkSemaphore), vkSC.ImageAvailableFence))
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
                case VkShaderProgram shaderProgram:
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

        DebugMarkerObjectNameInfoEXT nameInfo = new DebugMarkerObjectNameInfoEXT(sType: StructureType.DebugMarkerObjectNameInfoExt);
        nameInfo.ObjectType = type;
        nameInfo.Object = target;

        int byteCount = Encoding.UTF8.GetByteCount(name);
        byte* utf8Ptr = stackalloc byte[byteCount + 1];
        fixed (char* namePtr = name)
        {
            Encoding.UTF8.GetBytes(namePtr, name.Length, utf8Ptr, byteCount);
        }
        utf8Ptr[byteCount] = 0;

        nameInfo.PObjectName = utf8Ptr;
        Result result = _setObjectNameDelegate(_device, &nameInfo);
        CheckResult(result);
    }

    private void CreateInstance(bool debug, VulkanDeviceOptions options, VkSurfaceSwapchainSource? surface)
    {
        HashSet<string> availableInstanceLayers = [.. EnumerateInstanceLayers()];
        HashSet<string> availableInstanceExtensions = [.. GetInstanceExtensions()];

        InstanceCreateInfo instanceCI = new InstanceCreateInfo(sType: StructureType.InstanceCreateInfo);
        ApplicationInfo applicationInfo = new ApplicationInfo(sType: StructureType.ApplicationInfo)
        {
            ApiVersion = new Version32(1, 0, 0),
            ApplicationVersion = new Version32(1, 0, 0),
            EngineVersion = new Version32(1, 0, 0),
            PApplicationName = s_name,
            PEngineName = s_name
        };

        instanceCI.PApplicationInfo = &applicationInfo;

        // Capacity = the caller's requested extensions plus the fixed ones added below. The
        // fixed set is at most 8 (portability_enumeration + up to 5 platform surface extensions
        // + properties2 + debug_report); 16 leaves headroom so adding one can't overflow silently.
        int maxInstanceExtensions = (options.InstanceExtensions?.Length ?? 0) + 16;
        IntPtr* instanceExtensions = stackalloc IntPtr[maxInstanceExtensions];
        uint instanceExtensionCount = 0;
        IntPtr* instanceLayers = stackalloc IntPtr[2];
        uint instanceLayerCount = 0;

        if (availableInstanceExtensions.Contains(CommonStrings.VK_KHR_portability_subset))
            instanceExtensions[instanceExtensionCount++] = CommonStrings.VK_KHR_portability_subset;

        if (availableInstanceExtensions.Contains(CommonStrings.VK_KHR_portability_enumeration))
        {
            instanceExtensions[instanceExtensionCount++] = CommonStrings.VK_KHR_portability_enumeration;
            instanceCI.Flags |= (InstanceCreateFlags)VK_INSTANCE_CREATE_ENUMERATE_PORTABILITY_BIT_KHR;
        }

        if (surface != null)
        {
            byte** surfaceExtensions = surface.VkSurface.GetRequiredExtensions(out uint extensionCount);

            for (int i = 0; i < extensionCount; i++)
                instanceExtensions[instanceExtensionCount++] = (nint)surfaceExtensions[i];
        }

        bool hasDeviceProperties2 = availableInstanceExtensions.Contains(CommonStrings.VK_KHR_get_physical_device_properties2);
        if (hasDeviceProperties2)
            instanceExtensions[instanceExtensionCount++] = CommonStrings.VK_KHR_get_physical_device_properties2;

        string[] requestedInstanceExtensions = options.InstanceExtensions ?? Array.Empty<string>();
        List<FixedUtf8String> tempStrings = new List<FixedUtf8String>();
        foreach (string requiredExt in requestedInstanceExtensions)
        {
            if (!availableInstanceExtensions.Contains(requiredExt))
                throw new RenderException($"The required instance extension was not available: {requiredExt}");

            FixedUtf8String utf8Str = new FixedUtf8String(requiredExt);
            instanceExtensions[instanceExtensionCount++] = utf8Str;
            tempStrings.Add(utf8Str);
        }

        bool debugReportExtensionAvailable = false;
        if (debug)
        {
            if (availableInstanceExtensions.Contains(CommonStrings.VK_EXT_DEBUG_REPORT_EXTENSION_NAME))
            {
                debugReportExtensionAvailable = true;
                instanceExtensions[instanceExtensionCount++] = CommonStrings.VK_EXT_DEBUG_REPORT_EXTENSION_NAME;
            }
            if (availableInstanceLayers.Contains(CommonStrings.StandardValidationLayerName))
            {
                _standardValidationSupported = true;
                instanceLayers[instanceLayerCount++] = CommonStrings.StandardValidationLayerName;
            }
            if (availableInstanceLayers.Contains(CommonStrings.KhronosValidationLayerName))
            {
                _khronosValidationSupported = true;
                instanceLayers[instanceLayerCount++] = CommonStrings.KhronosValidationLayerName;
            }
        }

        instanceCI.EnabledExtensionCount = instanceExtensionCount;
        instanceCI.PpEnabledExtensionNames = (byte**)instanceExtensions;

        instanceCI.EnabledLayerCount = instanceLayerCount;
        if (instanceLayerCount > 0)
        {
            instanceCI.PpEnabledLayerNames = (byte**)instanceLayers;
        }

        Result result = _vk.CreateInstance(in instanceCI, null, out _instance);
        CheckResult(result);

        if (debug && debugReportExtensionAvailable)
        {
            EnableDebugCallback();
        }

        if (hasDeviceProperties2)
        {
            _getPhysicalDeviceProperties2 = GetInstanceProcAddr<vkGetPhysicalDeviceProperties2_t>("vkGetPhysicalDeviceProperties2")
                ?? GetInstanceProcAddr<vkGetPhysicalDeviceProperties2_t>("vkGetPhysicalDeviceProperties2KHR");
        }

        foreach (FixedUtf8String tempStr in tempStrings)
        {
            tempStr.Dispose();
        }
    }

    public void EnableDebugCallback(DebugReportFlagsEXT flags = DebugReportFlagsEXT.WarningBitExt | DebugReportFlagsEXT.ErrorBitExt)
    {
        Debug.WriteLine("Enabling Vulkan Debug callbacks.");
        _debugCallbackFunc = new PfnDebugReportCallbackEXT(&DebugCallback);
        DebugReportCallbackCreateInfoEXT debugCallbackCI = new DebugReportCallbackCreateInfoEXT(sType: StructureType.DebugReportCallbackCreateInfoExt);
        debugCallbackCI.Flags = flags;
        debugCallbackCI.PfnCallback = _debugCallbackFunc;

        if (_vk.TryGetInstanceExtension(_instance, out _extDebugReport))
        {
            Result result = _extDebugReport.CreateDebugReportCallback(_instance, in debugCallbackCI, null, out _debugCallbackHandle);
            CheckResult(result);
        }
    }

    // Stored validation error from the debug callback (cannot throw from unmanaged callback)
    private static volatile string _lastValidationError;

    /// <summary>
    /// Checks if a Vulkan validation error was reported and throws if so.
    /// Called after operations that may trigger validation errors.
    /// </summary>
    internal static void FlushValidationErrors()
    {
        string error = _lastValidationError;
        if (error != null)
        {
            _lastValidationError = null;
            throw new RenderException("A Vulkan validation error was encountered: " + error);
        }
    }

    [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    private static Bool32 DebugCallback(
        DebugReportFlagsEXT flags,
        DebugReportObjectTypeEXT objectType,
        ulong @object,
        nuint location,
        int messageCode,
        byte* pLayerPrefix,
        byte* pMessage,
        void* pUserData)
    {
        string message = Util.GetString(pMessage);
        DebugReportFlagsEXT debugReportFlags = flags;

        string fullMessage = $"[{debugReportFlags}] ({objectType}) {message}";

        if (debugReportFlags == DebugReportFlagsEXT.ErrorBitExt)
        {
            _lastValidationError = fullMessage;
            return true;
        }

        Console.WriteLine(fullMessage);
        return false;
    }

    private void CreatePhysicalDevice()
    {
        uint deviceCount = 0;
        _vk.EnumeratePhysicalDevices(_instance, ref deviceCount, null);
        if (deviceCount == 0)
        {
            throw new InvalidOperationException("No physical devices exist.");
        }

        PhysicalDevice[] physicalDevices = new PhysicalDevice[deviceCount];
        fixed (PhysicalDevice* devicesPtr = physicalDevices)
        {
            _vk.EnumeratePhysicalDevices(_instance, ref deviceCount, devicesPtr);
        }
        // Just use the first one.
        _physicalDevice = physicalDevices[0];

        _vk.GetPhysicalDeviceProperties(_physicalDevice, out _physicalDeviceProperties);
        fixed (byte* utf8NamePtr = _physicalDeviceProperties.DeviceName)
        {
            _deviceName = Util.GetString(utf8NamePtr);
        }

        _vendorName = "id:" + _physicalDeviceProperties.VendorID.ToString("x8");
        _apiVersion = GraphicsApiVersion.Unknown;
        _driverInfo = "version:" + _physicalDeviceProperties.DriverVersion.ToString("x8");

        _vk.GetPhysicalDeviceFeatures(_physicalDevice, out _physicalDeviceFeatures);

        _vk.GetPhysicalDeviceMemoryProperties(_physicalDevice, out _physicalDeviceMemProperties);
    }

    public ExtensionProperties[] GetDeviceExtensionProperties()
    {
        uint propertyCount = 0;
        Result result = _vk.EnumerateDeviceExtensionProperties(_physicalDevice, (byte*)null, &propertyCount, null);
        CheckResult(result);
        ExtensionProperties[] props = new ExtensionProperties[(int)propertyCount];
        fixed (ExtensionProperties* properties = props)
        {
            result = _vk.EnumerateDeviceExtensionProperties(_physicalDevice, (byte*)null, &propertyCount, properties);
            CheckResult(result);
        }
        return props;
    }

    private void CreateLogicalDevice(SurfaceKHR surface, bool preferStandardClipY, VulkanDeviceOptions options)
    {
        GetQueueFamilyIndices(surface);

        HashSet<uint> familyIndices = new HashSet<uint> { _graphicsQueueIndex, _presentQueueIndex };
        DeviceQueueCreateInfo* queueCreateInfos = stackalloc DeviceQueueCreateInfo[familyIndices.Count];
        uint queueCreateInfosCount = (uint)familyIndices.Count;

        int i = 0;
        foreach (uint index in familyIndices)
        {
            DeviceQueueCreateInfo queueCreateInfo = new DeviceQueueCreateInfo(sType: StructureType.DeviceQueueCreateInfo);
            queueCreateInfo.QueueFamilyIndex = index;
            queueCreateInfo.QueueCount = 1;
            float priority = 1f;
            queueCreateInfo.PQueuePriorities = &priority;
            queueCreateInfos[i] = queueCreateInfo;
            i += 1;
        }

        PhysicalDeviceFeatures deviceFeatures = _physicalDeviceFeatures;

        ExtensionProperties[] props = GetDeviceExtensionProperties();

        HashSet<string> requiredInstanceExtensions = new HashSet<string>(options.DeviceExtensions ?? Array.Empty<string>());

        bool hasMemReqs2 = false;
        bool hasDedicatedAllocation = false;
        bool hasDriverProperties = false;
        IntPtr[] activeExtensions = new IntPtr[props.Length];
        uint activeExtensionCount = 0;

        fixed (ExtensionProperties* properties = props)
        {
            for (int property = 0; property < props.Length; property++)
            {
                string extensionName = Util.GetString(properties[property].ExtensionName);
                if (extensionName == "VK_EXT_debug_marker")
                {
                    activeExtensions[activeExtensionCount++] = CommonStrings.VK_EXT_DEBUG_MARKER_EXTENSION_NAME;
                    requiredInstanceExtensions.Remove(extensionName);
                    _debugMarkerEnabled = true;
                }
                else if (extensionName == "VK_KHR_swapchain")
                {
                    activeExtensions[activeExtensionCount++] = (IntPtr)properties[property].ExtensionName;
                    requiredInstanceExtensions.Remove(extensionName);
                }
                else if (preferStandardClipY && extensionName == "VK_KHR_maintenance1")
                {
                    activeExtensions[activeExtensionCount++] = (IntPtr)properties[property].ExtensionName;
                    requiredInstanceExtensions.Remove(extensionName);
                    _standardClipYDirection = true;
                }
                else if (extensionName == "VK_KHR_get_memory_requirements2")
                {
                    activeExtensions[activeExtensionCount++] = (IntPtr)properties[property].ExtensionName;
                    requiredInstanceExtensions.Remove(extensionName);
                    hasMemReqs2 = true;
                }
                else if (extensionName == "VK_KHR_dedicated_allocation")
                {
                    activeExtensions[activeExtensionCount++] = (IntPtr)properties[property].ExtensionName;
                    requiredInstanceExtensions.Remove(extensionName);
                    hasDedicatedAllocation = true;
                }
                else if (extensionName == "VK_KHR_driver_properties")
                {
                    activeExtensions[activeExtensionCount++] = (IntPtr)properties[property].ExtensionName;
                    requiredInstanceExtensions.Remove(extensionName);
                    hasDriverProperties = true;
                }
                else if (extensionName == CommonStrings.VK_KHR_portability_subset)
                {
                    activeExtensions[activeExtensionCount++] = (IntPtr)properties[property].ExtensionName;
                    requiredInstanceExtensions.Remove(extensionName);
                }
                else if (requiredInstanceExtensions.Remove(extensionName))
                {
                    activeExtensions[activeExtensionCount++] = (IntPtr)properties[property].ExtensionName;
                }
            }
        }

        if (requiredInstanceExtensions.Count != 0)
        {
            string missingList = string.Join(", ", requiredInstanceExtensions);
            throw new RenderException(
                $"The following Vulkan device extensions were not available: {missingList}");
        }

        DeviceCreateInfo deviceCreateInfo = new DeviceCreateInfo(sType: StructureType.DeviceCreateInfo);
        deviceCreateInfo.QueueCreateInfoCount = queueCreateInfosCount;
        deviceCreateInfo.PQueueCreateInfos = queueCreateInfos;

        deviceCreateInfo.PEnabledFeatures = &deviceFeatures;

        IntPtr* layerNames = stackalloc IntPtr[2];
        uint layerNameCount = 0;
        if (_standardValidationSupported)
        {
            layerNames[layerNameCount++] = CommonStrings.StandardValidationLayerName;
        }
        if (_khronosValidationSupported)
        {
            layerNames[layerNameCount++] = CommonStrings.KhronosValidationLayerName;
        }
        deviceCreateInfo.EnabledLayerCount = layerNameCount;
        deviceCreateInfo.PpEnabledLayerNames = (byte**)layerNames;

        fixed (IntPtr* activeExtensionsPtr = activeExtensions)
        {
            deviceCreateInfo.EnabledExtensionCount = activeExtensionCount;
            deviceCreateInfo.PpEnabledExtensionNames = (byte**)activeExtensionsPtr;

            Result result = _vk.CreateDevice(_physicalDevice, in deviceCreateInfo, null, out _device);
            CheckResult(result);
        }

        _vk.GetDeviceQueue(_device, _graphicsQueueIndex, 0, out _graphicsQueue);

        _vk.TryGetInstanceExtension(_instance, out _khrSurface);
        _vk.TryGetDeviceExtension(_instance, _device, out _khrSwapchain);

        if (_debugMarkerEnabled)
        {
            _setObjectNameDelegate = Marshal.GetDelegateForFunctionPointer<vkDebugMarkerSetObjectNameEXT_t>(
                GetInstanceProcAddr("vkDebugMarkerSetObjectNameEXT"));
            _markerBegin = Marshal.GetDelegateForFunctionPointer<vkCmdDebugMarkerBeginEXT_t>(
                GetInstanceProcAddr("vkCmdDebugMarkerBeginEXT"));
            _markerEnd = Marshal.GetDelegateForFunctionPointer<vkCmdDebugMarkerEndEXT_t>(
                GetInstanceProcAddr("vkCmdDebugMarkerEndEXT"));
            _markerInsert = Marshal.GetDelegateForFunctionPointer<vkCmdDebugMarkerInsertEXT_t>(
                GetInstanceProcAddr("vkCmdDebugMarkerInsertEXT"));
        }
        if (hasDedicatedAllocation && hasMemReqs2)
        {
            _getBufferMemoryRequirements2 = GetDeviceProcAddr<vkGetBufferMemoryRequirements2_t>("vkGetBufferMemoryRequirements2")
                ?? GetDeviceProcAddr<vkGetBufferMemoryRequirements2_t>("vkGetBufferMemoryRequirements2KHR");
            _getImageMemoryRequirements2 = GetDeviceProcAddr<vkGetImageMemoryRequirements2_t>("vkGetImageMemoryRequirements2")
                ?? GetDeviceProcAddr<vkGetImageMemoryRequirements2_t>("vkGetImageMemoryRequirements2KHR");
        }
        if (_getPhysicalDeviceProperties2 != null && hasDriverProperties)
        {
            PhysicalDeviceProperties2KHR deviceProps = new PhysicalDeviceProperties2KHR(sType: StructureType.PhysicalDeviceProperties2Khr);
            VkPhysicalDeviceDriverProperties driverProps = VkPhysicalDeviceDriverProperties.New();

            deviceProps.PNext = &driverProps;
            _getPhysicalDeviceProperties2(_physicalDevice, &deviceProps);

            string driverName = Encoding.UTF8.GetString(
                driverProps.driverName, VkPhysicalDeviceDriverProperties.DriverNameLength).TrimEnd('\0');

            string driverInfo = Encoding.UTF8.GetString(
                driverProps.driverInfo, VkPhysicalDeviceDriverProperties.DriverInfoLength).TrimEnd('\0');

            VkConformanceVersion conforming = driverProps.conformanceVersion;
            _apiVersion = new GraphicsApiVersion(conforming.major, conforming.minor, conforming.subminor, conforming.patch);
            _driverName = driverName;
            _driverInfo = driverInfo;
        }
    }

    private IntPtr GetInstanceProcAddr(string name)
    {
        int byteCount = Encoding.UTF8.GetByteCount(name);
        byte* utf8Ptr = stackalloc byte[byteCount + 1];

        fixed (char* namePtr = name)
        {
            Encoding.UTF8.GetBytes(namePtr, name.Length, utf8Ptr, byteCount);
        }
        utf8Ptr[byteCount] = 0;

        return (IntPtr)_vk.GetInstanceProcAddr(_instance, utf8Ptr);
    }

    internal T GetInstanceProcAddr<T>(string name)
    {
        IntPtr funcPtr = GetInstanceProcAddr(name);
        if (funcPtr != IntPtr.Zero)
        {
            return Marshal.GetDelegateForFunctionPointer<T>(funcPtr);
        }
        return default;
    }

    private IntPtr GetDeviceProcAddr(string name)
    {
        int byteCount = Encoding.UTF8.GetByteCount(name);
        byte* utf8Ptr = stackalloc byte[byteCount + 1];

        fixed (char* namePtr = name)
        {
            Encoding.UTF8.GetBytes(namePtr, name.Length, utf8Ptr, byteCount);
        }
        utf8Ptr[byteCount] = 0;

        return (IntPtr)_vk.GetDeviceProcAddr(_device, utf8Ptr);
    }

    private T GetDeviceProcAddr<T>(string name)
    {
        IntPtr funcPtr = GetDeviceProcAddr(name);
        if (funcPtr != IntPtr.Zero)
        {
            return Marshal.GetDelegateForFunctionPointer<T>(funcPtr);
        }
        return default;
    }

    private void GetQueueFamilyIndices(SurfaceKHR surface)
    {
        uint queueFamilyCount = 0;
        _vk.GetPhysicalDeviceQueueFamilyProperties(_physicalDevice, ref queueFamilyCount, null);
        QueueFamilyProperties[] qfp = new QueueFamilyProperties[queueFamilyCount];
        fixed (QueueFamilyProperties* qfpPtr = qfp)
        {
            _vk.GetPhysicalDeviceQueueFamilyProperties(_physicalDevice, ref queueFamilyCount, qfpPtr);
        }

        bool foundGraphics = false;
        bool foundPresent = surface.Handle == 0;

        for (uint idx = 0; idx < qfp.Length; idx++)
        {
            if ((qfp[idx].QueueFlags & QueueFlags.GraphicsBit) != 0)
            {
                _graphicsQueueIndex = idx;
                foundGraphics = true;
            }

            if (!foundPresent)
            {
                if (_vk.TryGetInstanceExtension(_instance, out KhrSurface khrSurface))
                {
                    khrSurface.GetPhysicalDeviceSurfaceSupport(_physicalDevice, idx, surface, out Bool32 presentSupported);
                    if (presentSupported)
                    {
                        _presentQueueIndex = idx;
                        foundPresent = true;
                    }
                }
            }

            if (foundGraphics && foundPresent)
            {
                return;
            }
        }
    }

    private void CreateDescriptorPool()
    {
        _descriptorPoolManager = new VkDescriptorPoolManager(this);
    }

    private void CreateGraphicsCommandPool()
    {
        CommandPoolCreateInfo commandPoolCI = new CommandPoolCreateInfo(sType: StructureType.CommandPoolCreateInfo);
        commandPoolCI.Flags = CommandPoolCreateFlags.ResetCommandBufferBit;
        commandPoolCI.QueueFamilyIndex = _graphicsQueueIndex;
        Result result = _vk.CreateCommandPool(_device, in commandPoolCI, null, out _graphicsCommandPool);
        CheckResult(result);
    }

    protected override MappedResource MapCore(MappableResource resource, MapMode mode, uint subresource)
    {
        VkMemoryBlock memoryBlock = default(VkMemoryBlock);
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
        VkMemoryBlock memoryBlock = default(VkMemoryBlock);
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

    protected override unsafe void PlatformDispose()
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

        _descriptorPoolManager.DestroyAll();
        foreach (VkDescriptorPoolManager pool in _frameDescriptorPools)
        {
            pool.DestroyAll();
        }
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

        Result result = _vk.DeviceWaitIdle(_device);
        CheckResult(result);
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
            properties = default(PixelFormatProperties);
            return false;
        }
        CheckResult(result);

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

    private protected override void UpdateBufferCore(DeviceBuffer buffer, uint bufferOffsetInBytes, IntPtr source, uint sizeInBytes)
    {
        VkBuffer vkBuffer = Util.AssertSubtype<DeviceBuffer, VkBuffer>(buffer);
        VkBuffer copySrcVkBuffer = null;
        IntPtr mappedPtr;
        byte* destPtr;
        bool isPersistentMapped = vkBuffer.Memory.IsPersistentMapped;
        if (isPersistentMapped)
        {
            mappedPtr = (IntPtr)vkBuffer.Memory.BlockMappedPointer;
            destPtr = (byte*)mappedPtr + bufferOffsetInBytes;
        }
        else
        {
            copySrcVkBuffer = GetFreeStagingBuffer(sizeInBytes);
            mappedPtr = (IntPtr)copySrcVkBuffer.Memory.BlockMappedPointer;
            destPtr = (byte*)mappedPtr;
        }

        Unsafe.CopyBlock(destPtr, source.ToPointer(), sizeInBytes);

        if (!isPersistentMapped)
        {
            SharedCommandPool pool = GetFreeCommandPool();
            Silk.NET.Vulkan.CommandBuffer cb = pool.BeginNewCommandBuffer();

            BufferCopy copyRegion = new BufferCopy
            {
                DstOffset = bufferOffsetInBytes,
                Size = sizeInBytes
            };
            _vk.CmdCopyBuffer(cb, copySrcVkBuffer.DeviceBuffer, vkBuffer.DeviceBuffer, 1, in copyRegion);

            pool.EndAndSubmit(cb);
            lock (_stagingResourcesLock)
            {
                _submittedStagingBuffers.Add(cb, copySrcVkBuffer);
            }
        }
    }

    private SharedCommandPool GetFreeCommandPool()
    {
        SharedCommandPool sharedPool = null;
        lock (_graphicsCommandPoolLock)
        {
            if (_sharedGraphicsCommandPools.Count > 0)
                sharedPool = _sharedGraphicsCommandPools.Pop();
        }

        if (sharedPool == null)
            sharedPool = new SharedCommandPool(this, false);

        return sharedPool;
    }

    private IntPtr MapBuffer(VkBuffer buffer, uint numBytes)
    {
        if (buffer.Memory.IsPersistentMapped)
        {
            return (IntPtr)buffer.Memory.BlockMappedPointer;
        }
        else
        {
            void* mappedPtr;
            Result result = _vk.MapMemory(Device, buffer.Memory.DeviceMemory, buffer.Memory.Offset, numBytes, 0, &mappedPtr);
            CheckResult(result);
            return (IntPtr)mappedPtr;
        }
    }

    private void UnmapBuffer(VkBuffer buffer)
    {
        if (!buffer.Memory.IsPersistentMapped)
        {
            _vk.UnmapMemory(Device, buffer.Memory.DeviceMemory);
        }
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
        VkTexture vkTex = Util.AssertSubtype<Texture, VkTexture>(texture);
        bool isStaging = (vkTex.Usage & TextureUsage.Staging) != 0;
        if (isStaging)
        {
            VkMemoryBlock memBlock = vkTex.Memory;
            uint subresource = texture.CalculateSubresource(mipLevel, arrayLayer);
            SubresourceLayout layout = vkTex.GetSubresourceLayout(subresource);
            byte* imageBasePtr = (byte*)memBlock.BlockMappedPointer + layout.Offset;

            uint srcRowPitch = FormatHelpers.GetRowPitch(width, texture.Format);
            uint srcDepthPitch = FormatHelpers.GetDepthPitch(srcRowPitch, height, texture.Format);
            Util.CopyTextureRegion(
                source.ToPointer(),
                0, 0, 0,
                srcRowPitch, srcDepthPitch,
                imageBasePtr,
                x, y, z,
                (uint)layout.RowPitch, (uint)layout.DepthPitch,
                width, height, depth,
                texture.Format);
        }
        else
        {
            VkTexture stagingTex = GetFreeStagingTexture(width, height, depth, texture.Format);
            UpdateTexture(stagingTex, source, sizeInBytes, 0, 0, 0, width, height, depth, 0, 0);
            SharedCommandPool pool = GetFreeCommandPool();
            Silk.NET.Vulkan.CommandBuffer cb = pool.BeginNewCommandBuffer();
            VkCommandBuffer.CopyTextureCore_VkCommandBuffer(
                _vk,
                cb,
                stagingTex, 0, 0, 0, 0, 0,
                texture, x, y, z, mipLevel, arrayLayer,
                width, height, depth, 1);
            lock (_stagingResourcesLock)
            {
                _submittedStagingTextures.Add(cb, stagingTex);
            }
            pool.EndAndSubmit(cb);
        }
    }

    private VkTexture GetFreeStagingTexture(uint width, uint height, uint depth, PixelFormat format)
    {
        uint totalSize = FormatHelpers.GetRegionSize(width, height, depth, format);
        lock (_stagingResourcesLock)
        {
            for (int i = 0; i < _availableStagingTextures.Count; i++)
            {
                VkTexture tex = _availableStagingTextures[i];
                if (tex.Memory.Size >= totalSize)
                {
                    _availableStagingTextures.RemoveAt(i);
                    tex.SetStagingDimensions(width, height, depth, format);
                    return tex;
                }
            }
        }

        uint texWidth = Math.Max(256, width);
        uint texHeight = Math.Max(256, height);
        VkTexture newTex = (VkTexture)ResourceFactory.CreateTexture(TextureDescription.Texture3D(
            texWidth, texHeight, depth, 1, format, TextureUsage.Staging));
        newTex.SetStagingDimensions(width, height, depth, format);

        return newTex;
    }

    private VkBuffer GetFreeStagingBuffer(uint size)
    {
        lock (_stagingResourcesLock)
        {
            for (int i = 0; i < _availableStagingBuffers.Count; i++)
            {
                VkBuffer buffer = _availableStagingBuffers[i];
                if (buffer.SizeInBytes >= size)
                {
                    _availableStagingBuffers.RemoveAt(i);
                    return buffer;
                }
            }
        }

        uint newBufferSize = Math.Max(MinStagingBufferSize, size);
        VkBuffer newBuffer = (VkBuffer)ResourceFactory.CreateBuffer(
            new BufferDescription(newBufferSize, BufferUsage.Staging));
        return newBuffer;
    }

    public override void ResetFence(Fence fence)
    {
        VkFenceHandle vkFence = Util.AssertSubtype<Fence, Prowl.Veldrid.Vk.VkFence>(fence).DeviceFence;
        _vk.ResetFences(_device, 1, &vkFence);
    }

    public override bool WaitForFence(Fence fence, ulong nanosecondTimeout)
    {
        VkFenceHandle vkFence = Util.AssertSubtype<Fence, Prowl.Veldrid.Vk.VkFence>(fence).DeviceFence;
        Result result = _vk.WaitForFences(_device, 1, &vkFence, true, nanosecondTimeout);
        return result == Result.Success;
    }

    internal static bool IsSupported()
    {
        return s_isSupported.Value;
    }

    private static bool CheckIsSupported()
    {
        if (!IsVulkanLoaded())
        {
            return false;
        }

        using var vk = VkApi.GetApi();
        InstanceCreateInfo instanceCI = new InstanceCreateInfo(sType: StructureType.InstanceCreateInfo);
        ApplicationInfo applicationInfo = new ApplicationInfo(sType: StructureType.ApplicationInfo);
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

        HashSet<string> instanceExtensions = new HashSet<string>(GetInstanceExtensions());
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
        ImageSubresourceRange range = new ImageSubresourceRange(
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
        ImageSubresourceRange range = new ImageSubresourceRange(
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

    private class SharedCommandPool
    {
        private readonly VkGraphicsDevice _gd;
        private readonly CommandPool _pool;
        private readonly Silk.NET.Vulkan.CommandBuffer _cb;

        public bool IsCached { get; }

        public SharedCommandPool(VkGraphicsDevice gd, bool isCached)
        {
            _gd = gd;
            IsCached = isCached;

            CommandPoolCreateInfo commandPoolCI = new CommandPoolCreateInfo(sType: StructureType.CommandPoolCreateInfo);
            commandPoolCI.Flags = CommandPoolCreateFlags.TransientBit | CommandPoolCreateFlags.ResetCommandBufferBit;
            commandPoolCI.QueueFamilyIndex = _gd.GraphicsQueueIndex;
            Result result = _gd._vk.CreateCommandPool(_gd.Device, in commandPoolCI, null, out _pool);
            CheckResult(result);

            CommandBufferAllocateInfo allocateInfo = new CommandBufferAllocateInfo(sType: StructureType.CommandBufferAllocateInfo);
            allocateInfo.CommandBufferCount = 1;
            allocateInfo.Level = CommandBufferLevel.Primary;
            allocateInfo.CommandPool = _pool;
            Result allocResult;
            fixed (Silk.NET.Vulkan.CommandBuffer* cbPtr = &_cb)
            {
                allocResult = _gd._vk.AllocateCommandBuffers(_gd.Device, &allocateInfo, cbPtr);
            }
            CheckResult(allocResult);
        }

        public Silk.NET.Vulkan.CommandBuffer BeginNewCommandBuffer()
        {
            CommandBufferBeginInfo beginInfo = new CommandBufferBeginInfo(sType: StructureType.CommandBufferBeginInfo);
            beginInfo.Flags = CommandBufferUsageFlags.OneTimeSubmitBit;
            Result result = _gd._vk.BeginCommandBuffer(_cb, in beginInfo);
            CheckResult(result);

            return _cb;
        }

        public void EndAndSubmit(Silk.NET.Vulkan.CommandBuffer cb)
        {
            Result result = _gd._vk.EndCommandBuffer(cb);
            CheckResult(result);
            _gd.SubmitCommandBuffer(null, cb, 0, null, 0, null, null);
            lock (_gd._stagingResourcesLock)
            {
                _gd._submittedSharedCommandPools.Add(cb, this);
            }
        }

        internal void Destroy()
        {
            _gd._vk.DestroyCommandPool(_gd.Device, _pool, null);
        }
    }

    private struct FenceSubmissionInfo
    {
        public VkFenceHandle Fence;
        public VkCommandBuffer CommandBuffer;
        public Silk.NET.Vulkan.CommandBuffer VulkanCommandBuffer;
        public FenceSubmissionInfo(VkFenceHandle fence, VkCommandBuffer commandBuffer, Silk.NET.Vulkan.CommandBuffer vulkanCommandBuffer)
        {
            Fence = fence;
            CommandBuffer = commandBuffer;
            VulkanCommandBuffer = vulkanCommandBuffer;
        }
    }
}

internal unsafe delegate Result vkDebugMarkerSetObjectNameEXT_t(Device device, DebugMarkerObjectNameInfoEXT* pNameInfo);
internal unsafe delegate void vkCmdDebugMarkerBeginEXT_t(Silk.NET.Vulkan.CommandBuffer commandBuffer, DebugMarkerMarkerInfoEXT* pMarkerInfo);
internal unsafe delegate void vkCmdDebugMarkerEndEXT_t(Silk.NET.Vulkan.CommandBuffer commandBuffer);
internal unsafe delegate void vkCmdDebugMarkerInsertEXT_t(Silk.NET.Vulkan.CommandBuffer commandBuffer, DebugMarkerMarkerInfoEXT* pMarkerInfo);

internal unsafe delegate void vkGetBufferMemoryRequirements2_t(Device device, BufferMemoryRequirementsInfo2KHR* pInfo, MemoryRequirements2KHR* pMemoryRequirements);
internal unsafe delegate void vkGetImageMemoryRequirements2_t(Device device, ImageMemoryRequirementsInfo2KHR* pInfo, MemoryRequirements2KHR* pMemoryRequirements);

internal unsafe delegate void vkGetPhysicalDeviceProperties2_t(PhysicalDevice physicalDevice, void* properties);

// VK_MVK_macos_surface (legacy, no Silk.NET extension class available)
internal unsafe delegate Result vkCreateMacOSSurfaceMVK_t(
    Instance instance,
    MacOSSurfaceCreateInfoMVK* pCreateInfo,
    AllocationCallbacks* pAllocator,
    SurfaceKHR* pSurface);

internal unsafe struct VkPhysicalDeviceDriverProperties
{
    public const int DriverNameLength = 256;
    public const int DriverInfoLength = 256;
    public const StructureType VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_DRIVER_PROPERTIES = (StructureType)1000196000;

    public StructureType sType;
    public void* pNext;
    public int driverID;
    public fixed byte driverName[DriverNameLength];
    public fixed byte driverInfo[DriverInfoLength];
    public VkConformanceVersion conformanceVersion;

    public static VkPhysicalDeviceDriverProperties New()
    {
        return new VkPhysicalDeviceDriverProperties() { sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_DRIVER_PROPERTIES };
    }
}

internal struct VkConformanceVersion
{
    public byte major;
    public byte minor;
    public byte subminor;
    public byte patch;
}
