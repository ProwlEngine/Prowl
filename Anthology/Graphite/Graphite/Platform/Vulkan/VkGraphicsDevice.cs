using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

using Silk.NET.Core;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;

using VkApi = Silk.NET.Vulkan.Vk;
using VkFenceHandle = Silk.NET.Vulkan.Fence;

namespace Prowl.Graphite.Vk;

internal unsafe partial class VkGraphicsDevice : GraphicsDevice
{
    private static readonly FixedUtf8String s_name = "Prowl.Graphite-VkGraphicsDevice";
    private static readonly Lazy<bool> s_isSupported = new(CheckIsSupported, isThreadSafe: true);

    private readonly BackendInfoVulkan _vulkanInfo;
    private readonly VkSwapchain _mainSwapchain;
    private readonly VkGraphCommandBufferPool _graphCommandBufferPool;
    private readonly VkDescriptorSetCacheRegistry _descriptorSetCaches = new();
    private readonly VkDefaultTextureViewCache _defaultTextureViews;

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

        MemoryManager = new VkDeviceMemoryManager(
            Vk,
            Device,
            PhysicalDevice,
            _physicalDeviceProperties.Limits.BufferImageGranularity,
            GetBufferMemoryRequirements2,
            GetImageMemoryRequirements2);

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
        _graphCommandBufferPool = new VkGraphCommandBufferPool(this);
        _defaultTextureViews = new VkDefaultTextureViewCache(ResourceFactory);

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
        Vk.CreatePipelineCache(Device, in pcCI, null, out DriverPipelineCache).CheckResult();

        for (int i = 0; i < SharedCommandPoolCount; i++)
        {
            _sharedGraphicsCommandPools.Push(new SharedCommandPool(this, true));
        }

        _vulkanInfo = new BackendInfoVulkan(this);

        InitializeSlots();
        PostDeviceCreated();
    }

    public override ResourceFactory ResourceFactory { get; }

    internal void RegisterDescriptorSetCache(VkDescriptorSetCache cache) => _descriptorSetCaches.Register(cache);

    internal void UnregisterDescriptorSetCache(VkDescriptorSetCache cache) => _descriptorSetCaches.Unregister(cache);

    /// <summary>
    /// Gets or creates full-range view for texture. Device-owned, lives til dispose.
    /// </summary>
    internal VkTextureView GetOrCreateDefaultView(VkTexture texture) => _defaultTextureViews.GetOrCreate(texture);

    internal override CommandBuffer RentGraphCommandBuffer() => _graphCommandBufferPool.Rent();

    internal void ReturnGraphCommandBuffer(VkCommandBuffer cb) => _graphCommandBufferPool.Return(cb);

    /// <summary>Test hook: total distinct graph command buffers ever allocated.</summary>
    internal int PooledGraphCommandBufferCount => _graphCommandBufferPool.AllocatedCount;

    private protected override void SwapBuffersCore(Swapchain swapchain)
    {
        VkSwapchain vkSC = Util.AssertSubtype<Swapchain, VkSwapchain>(swapchain);
        SwapchainKHR deviceSwapchain = vkSC.DeviceSwapchain;
        PresentInfoKHR presentInfo = new(sType: StructureType.PresentInfoKhr);
        presentInfo.SwapchainCount = 1;
        presentInfo.PSwapchains = &deviceSwapchain;
        uint imageIndex = vkSC.ImageIndex;
        presentInfo.PImageIndices = &imageIndex;

        object presentLock = vkSC.PresentQueueIndex == GraphicsQueueIndex ? _graphicsQueueLock : vkSC;
        lock (presentLock)
        {
            KhrSwapchain.QueuePresent(vkSC.PresentQueue, &presentInfo);
            if (vkSC.AcquireNextImage(Device, default, vkSC.ImageAvailableFence))
            {
                VkFenceHandle fence = vkSC.ImageAvailableFence;
                Vk.WaitForFences(Device, 1, &fence, true, ulong.MaxValue);
                Vk.ResetFences(Device, 1, &fence);
            }
        }
    }

    private protected override void WaitForIdleCore()
    {
        lock (_graphicsQueueLock)
        {
            Vk.QueueWaitIdle(GraphicsQueue);
        }

        CheckSubmittedFences();
        FlushValidationErrors();
    }

    protected override void PlatformDispose()
    {
        DisposeSlots();

        Debug.Assert(_submittedFences.Count == 0);
        foreach (VkFenceHandle fence in _availableSubmissionFences)
        {
            Vk.DestroyFence(Device, fence, null);
        }

        _mainSwapchain?.Dispose();
        DestroyDebugCallback();

        _graphCommandBufferPool.Dispose();

        DescriptorPoolManager.DestroyAll();
        _defaultTextureViews.Dispose();
        Vk.DestroyCommandPool(Device, _graphicsCommandPool, null);

        DisposeStagingResources();

        Vk.DestroyPipelineCache(Device, DriverPipelineCache, null);

        MemoryManager.Dispose();

        Vk.DeviceWaitIdle(Device).CheckResult();

        Vk.DestroyDevice(Device, null);
        Vk.DestroyInstance(Instance, null);
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
}
