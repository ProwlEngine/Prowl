using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;

namespace Prowl.Graphite.Vk;

internal unsafe delegate Result vkSetDebugUtilsObjectNameEXT_t(Device device, DebugUtilsObjectNameInfoEXT* pNameInfo);
internal unsafe delegate void vkCmdBeginDebugUtilsLabelEXT_t(Silk.NET.Vulkan.CommandBuffer commandBuffer, DebugUtilsLabelEXT* pLabelInfo);
internal delegate void vkCmdEndDebugUtilsLabelEXT_t(Silk.NET.Vulkan.CommandBuffer commandBuffer);
internal unsafe delegate void vkCmdInsertDebugUtilsLabelEXT_t(Silk.NET.Vulkan.CommandBuffer commandBuffer, DebugUtilsLabelEXT* pLabelInfo);

internal unsafe delegate void vkGetBufferMemoryRequirements2_t(Device device, BufferMemoryRequirementsInfo2KHR* pInfo, MemoryRequirements2KHR* pMemoryRequirements);
internal unsafe delegate void vkGetImageMemoryRequirements2_t(Device device, ImageMemoryRequirementsInfo2KHR* pInfo, MemoryRequirements2KHR* pMemoryRequirements);

internal unsafe delegate void vkGetPhysicalDeviceProperties2_t(PhysicalDevice physicalDevice, void* properties);
internal unsafe delegate void vkGetPhysicalDeviceMemoryProperties2_t(PhysicalDevice physicalDevice, void* memoryProperties);

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
