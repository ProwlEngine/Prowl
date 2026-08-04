using System;
using System.Runtime.InteropServices;

using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Vulkan.Extensions.KHR;

namespace Prowl.Graphite.Vk;

internal unsafe partial class VkGraphicsDevice
{
    internal KhrSurface KhrSurface;
    internal KhrSwapchain KhrSwapchain;
    internal vkGetBufferMemoryRequirements2_t? GetBufferMemoryRequirements2;
    internal vkGetImageMemoryRequirements2_t? GetImageMemoryRequirements2;

    private ExtDebugReport _extDebugReport;
    private vkGetPhysicalDeviceProperties2_t? _getPhysicalDeviceProperties2;
    private vkGetPhysicalDeviceMemoryProperties2_t? _getPhysicalDeviceMemoryProperties2;

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

    private IntPtr GetInstanceProcAddr(string name)
    {
        byte* utf8Ptr = stackalloc byte[Utf8Stack.ByteCount(name)];
        Utf8Stack.Write(name, utf8Ptr);

        return (IntPtr)Vk.GetInstanceProcAddr(Instance, utf8Ptr);
    }

    internal T? GetInstanceProcAddr<T>(string name)
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
        byte* utf8Ptr = stackalloc byte[Utf8Stack.ByteCount(name)];
        Utf8Stack.Write(name, utf8Ptr);

        return (IntPtr)Vk.GetDeviceProcAddr(Device, utf8Ptr);
    }

    private T? GetDeviceProcAddr<T>(string name)
    {
        IntPtr funcPtr = GetDeviceProcAddr(name);
        if (funcPtr != IntPtr.Zero)
        {
            return Marshal.GetDelegateForFunctionPointer<T>(funcPtr);
        }
        return default;
    }
}
