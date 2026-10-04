using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

using Silk.NET.Core;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;

using VkApi = Silk.NET.Vulkan.Vk;
using VkSemaphore = Silk.NET.Vulkan.Semaphore;

namespace Prowl.Graphite.Vk;

internal unsafe partial class VkGraphicsDevice : GraphicsDevice
{
    private static byte* Name => CommonStrings.Utf8("Prowl.Graphite-VkGraphicsDevice"u8);
    private static readonly Lazy<bool> s_isSupported = new(CheckIsSupported, isThreadSafe: true);

    private readonly BackendInfoVulkan _vulkanInfo;
    private readonly VkSwapchain _mainSwapchain;
    private readonly VkDescriptorSetCacheRegistry _descriptorSetCaches = new();
    private readonly VkDefaultTextureViewCache _defaultTextureViews;

    public VkGraphicsDevice(GraphicsDeviceOptions options, SwapchainDescription? scDesc)
        : this(options, scDesc, new VulkanDeviceOptions()) { }

    public VkGraphicsDevice(GraphicsDeviceOptions options, SwapchainDescription? scDesc, VulkanDeviceOptions vkOptions)
    {
        VkSurfaceSwapchainSource? surfaceSource = scDesc != null ?
            Util.AssertSubtype<SwapchainSource, VkSurfaceSwapchainSource>(scDesc.Value.Source) : null;

        CreateInstance(options.VulkanValidationLayers, vkOptions, surfaceSource);

        SurfaceKHR surface = default;
        if (surfaceSource != null)
            surface = surfaceSource.GetSurface(Instance);

        CreatePhysicalDevice();
        CreateLogicalDevice(surface, vkOptions);

        MemoryManager = new VkDeviceMemoryManager(
            Vk,
            Device,
            PhysicalDevice,
            _physicalDeviceProperties.Limits.BufferImageGranularity);

        Features = new GraphicsDeviceFeatures(
            geometryShader: _physicalDeviceFeatures.GeometryShader,
            tessellationShaders: _physicalDeviceFeatures.TessellationShader,
            drawIndirectBaseInstance: _physicalDeviceFeatures.DrawIndirectFirstInstance,
            samplerAnisotropy: _physicalDeviceFeatures.SamplerAnisotropy,
            depthClipDisable: _physicalDeviceFeatures.DepthClamp,
            independentBlend: _physicalDeviceFeatures.IndependentBlend,
            commandBufferDebugMarkers: _debugUtilsEnabled,
            shaderFloat64: _physicalDeviceFeatures.ShaderFloat64);

        ResourceFactory = new VkResourceFactory(this);
        _defaultTextureViews = new VkDefaultTextureViewCache(ResourceFactory);

        InitializeFrameOptions(options);

        if (scDesc != null)
        {
            SwapchainDescription desc = scDesc.Value;
            _mainSwapchain = new VkSwapchain(this, ref desc, surface);
        }

        PipelineCacheCreateInfo pcCI = new()
        {
            SType = StructureType.PipelineCacheCreateInfo,
            InitialDataSize = 0,
            PInitialData = null,
        };
        Vk.CreatePipelineCache(Device, in pcCI, null, out DriverPipelineCache).CheckResult();

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

    internal void ReleaseDefaultView(VkTexture texture) => _defaultTextureViews.Remove(texture);

    internal override CommandBuffer RentGraphCommandBuffer(ExecutionTask task)
    {
        ref SlotState slot = ref _slots[task.RingSlot];
        lock (slot.Wrappers)
        {
            if (slot.WrappersInUse < slot.Wrappers.Count)
                return slot.Wrappers[slot.WrappersInUse++];

            VkCommandBuffer cb = new(this, slot.Pool);
            slot.Wrappers.Add(cb);
            slot.WrappersInUse++;
            return cb;
        }
    }

    /// <summary>Test hook: total distinct graph command buffers ever allocated.</summary>
    internal int PooledGraphCommandBufferCount
    {
        get
        {
            int total = 0;
            foreach (ref SlotState slot in _slots.AsSpan())
            {
                lock (slot.Wrappers)
                    total += slot.Wrappers.Count;
            }
            return total;
        }
    }

    private protected override void SwapBuffersCore(Swapchain swapchain)
    {
        VkSwapchain vkSC = Util.AssertSubtype<Swapchain, VkSwapchain>(swapchain);
        if (!vkSC.ImageAcquired)
        {
            vkSC.AcquireNextImage();
            return;
        }

        VkSemaphore presentSemaphore = vkSC.PresentSemaphore;
        SignalPresentSemaphore(vkSC, presentSemaphore);

        SwapchainKHR deviceSwapchain = vkSC.DeviceSwapchain;
        uint imageIndex = vkSC.ImageIndex;
        PresentInfoKHR presentInfo = new(sType: StructureType.PresentInfoKhr)
        {
            WaitSemaphoreCount = 1,
            PWaitSemaphores = &presentSemaphore,
            SwapchainCount = 1,
            PSwapchains = &deviceSwapchain,
            PImageIndices = &imageIndex,
        };

        object presentLock = vkSC.PresentQueueIndex == GraphicsQueueIndex ? _graphicsQueueLock : vkSC;
        Result presentResult;
        lock (presentLock)
            presentResult = KhrSwapchain.QueuePresent(vkSC.PresentQueue, &presentInfo);

        if (presentResult != Result.Success && presentResult != Result.SuboptimalKhr && presentResult != Result.ErrorOutOfDateKhr)
            throw new RenderException($"Could not present the Vulkan swapchain: {presentResult}.");

        vkSC.MarkPresented(presentResult);
        vkSC.AcquireNextImage();
    }

    private protected override void WaitForIdleCore()
    {
        FlushPendingInitCommands();
        lock (_graphicsQueueLock)
        {
            Vk.QueueWaitIdle(GraphicsQueue);
        }

        PollSubmissions();
        FlushValidationErrors();
    }

    protected override void PlatformDispose()
    {
        DisposeSlots();

        Debug.Assert(_pending.Count == 0);

        _mainSwapchain?.Dispose();
        DestroyDebugCallback();

        _defaultTextureViews.Dispose();

        DisposeCommandPools();

        WaitForGraphicsQueueIdle();
        FlushAllRetired();

        Vk.DestroyPipelineCache(Device, DriverPipelineCache, null);

        MemoryManager.Dispose();

        Vk.DeviceWaitIdle(Device).CheckResult();

        Vk.DestroySemaphore(Device, _timelineSemaphore, null);
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
        applicationInfo.PApplicationName = Name;
        applicationInfo.PEngineName = Name;

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
        else if (OperatingSystem.IsAndroid())
        {
            return instanceExtensions.Contains(CommonStrings.VK_KHR_ANDROID_SURFACE_EXTENSION_NAME);
        }
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
