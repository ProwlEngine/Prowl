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

    /// <summary>
    /// VkPipelineCache handle passed to every pipeline create call, speeds up compiles.
    /// </summary>
    internal PipelineCache DriverPipelineCache;

    private readonly object _graphicsQueueLock = new();

    internal object GraphicsQueueLock => _graphicsQueueLock;
    private PhysicalDeviceProperties _physicalDeviceProperties;
    private PhysicalDeviceFeatures _physicalDeviceFeatures;
    private PhysicalDeviceVulkan11Features _enabledVulkan11Features;
    private PhysicalDeviceVulkan12Features _enabledVulkan12Features;
    private string _deviceName;
    private string _vendorName;
    private GraphicsApiVersion _apiVersion;

    public override string DeviceName => _deviceName;

    public override string VendorName => _vendorName;

    public override GraphicsApiVersion ApiVersion => _apiVersion;

    public override GraphicsBackend BackendType => GraphicsBackend.Vulkan;

    public override bool IsUvOriginTopLeft => true;

    public override bool IsDepthRangeZeroToOne => true;

    public override Swapchain MainSwapchain => _mainSwapchain;

    public override GraphicsDeviceFeatures Features { get; }

    // The cache can grow between the size query and the copy when another thread builds a pipeline, so retry.
    public override byte[] GetPipelineCacheData()
    {
        while (true)
        {
            nuint size = 0;
            Vk.GetPipelineCacheData(Device, DriverPipelineCache, ref size, null).CheckResult();
            byte[] data = new byte[size];
            Result result;
            fixed (byte* destination = data)
                result = Vk.GetPipelineCacheData(Device, DriverPipelineCache, ref size, destination);

            if (result == Result.Incomplete)
                continue;

            result.CheckResult();
            return size == (nuint)data.Length ? data : data[..(int)size];
        }
    }

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
