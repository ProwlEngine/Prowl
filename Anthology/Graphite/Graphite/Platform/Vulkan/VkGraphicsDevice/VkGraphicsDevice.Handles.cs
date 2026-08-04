using Silk.NET.Vulkan;

using VkApi = Silk.NET.Vulkan.Vk;

namespace Prowl.Graphite.Vk;

internal unsafe partial class VkGraphicsDevice
{
    internal readonly VkApi Vk = VkApi.GetApi();

    internal Instance Instance;
    internal PhysicalDevice PhysicalDevice;
    internal Device Device;
    internal Queue GraphicsQueue;
    internal uint GraphicsQueueIndex;
    internal uint PresentQueueIndex;
    internal PhysicalDeviceMemoryProperties PhysicalDeviceMemProperties;
    internal string DriverName;
    internal string DriverInfo;
    internal VkDeviceMemoryManager MemoryManager;
    internal VkDescriptorPoolManager DescriptorPoolManager;

    /// <summary>
    /// VkPipelineCache handle passed to every pipeline create call, speeds up compiles.
    /// </summary>
    internal PipelineCache DriverPipelineCache;

    private readonly object _graphicsQueueLock = new();
    private CommandPool _graphicsCommandPool;
    private PhysicalDeviceProperties _physicalDeviceProperties;
    private PhysicalDeviceFeatures _physicalDeviceFeatures;
    private string _deviceName;
    private string _vendorName;
    private GraphicsApiVersion _apiVersion;
    private bool _standardClipYDirection;

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

    internal override uint GetUniformBufferMinOffsetAlignmentCore()
        => (uint)_physicalDeviceProperties.Limits.MinUniformBufferOffsetAlignment;

    internal override uint GetStructuredBufferMinOffsetAlignmentCore()
        => (uint)_physicalDeviceProperties.Limits.MinStorageBufferOffsetAlignment;
}
