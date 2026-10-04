using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Vulkan.Extensions.KHR;

namespace Prowl.Graphite.Vk;

internal unsafe partial class VkGraphicsDevice
{
    internal KhrSurface KhrSurface;
    internal KhrSwapchain KhrSwapchain;
    internal ExtDebugUtils? DebugUtils;

    private bool _memoryBudgetSupported;

    public ExtensionProperties[] GetDeviceExtensionProperties()
    {
        uint propertyCount = 0;
        Vk.EnumerateDeviceExtensionProperties(PhysicalDevice, (byte*)null, &propertyCount, null).CheckResult();
        ExtensionProperties[] props = new ExtensionProperties[(int)propertyCount];
        fixed (ExtensionProperties* properties = props)
        {
            Vk.EnumerateDeviceExtensionProperties(PhysicalDevice, (byte*)null, &propertyCount, properties).CheckResult();
        }
        return props;
    }
}
