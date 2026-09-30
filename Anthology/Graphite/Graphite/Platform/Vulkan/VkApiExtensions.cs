using System;
using System.Diagnostics;
using System.Linq;

using Silk.NET.Vulkan;

using VkApi = Silk.NET.Vulkan.Vk;


namespace Prowl.Graphite.Vk;


internal static class VkApiExtensions
{
    private static volatile bool s_isLoaded;

    public static unsafe bool IsLoaded(this VkApi vk)
    {
        if (s_isLoaded)
            return true;

        try
        {
            uint propCount;
            vk.EnumerateInstanceExtensionProperties((byte*)null, &propCount, null);
            return s_isLoaded = true;
        }
        catch
        {
            return s_isLoaded = false;
        }
    }


    public static unsafe string[] EnumerateInstanceExtensionProperties(this VkApi vk, ReadOnlySpan<byte> pLayerName)
    {
        fixed (byte* layerName = pLayerName)
            return EnumerateInstanceExtensionProperties(vk, layerName);
    }


    public static unsafe string[] EnumerateInstanceExtensionProperties(this VkApi vk, byte* pLayerName)
    {
        if (!vk.IsLoaded())
            return [];

        uint propCount = 0;
        Result result = vk.EnumerateInstanceExtensionProperties(pLayerName, ref propCount, null);

        if (result != Result.Success || propCount == 0)
            return [];

        ExtensionProperties* props = stackalloc ExtensionProperties[(int)propCount];
        vk.EnumerateInstanceExtensionProperties(pLayerName, ref propCount, props);

        string[] extensions = new string[propCount];
        for (int i = 0; i < propCount; i++)
            extensions[i] = Util.GetString(props[i].ExtensionName);

        return extensions;
    }


    public static unsafe string[] EnumerateInstanceLayers(this VkApi vk, ReadOnlySpan<LayerProperties> layerProperties)
    {
        fixed (LayerProperties* layerProps = layerProperties)
            return EnumerateInstanceLayers(vk, layerProps);
    }


    public static unsafe string[] EnumerateInstanceLayers(this VkApi vk, LayerProperties* layerProperties)
    {
        if (!vk.IsLoaded())
            return [];

        uint propCount = 0;
        vk.EnumerateInstanceLayerProperties(ref propCount, layerProperties).CheckResult();

        if (propCount == 0)
            return [];

        LayerProperties* props = stackalloc LayerProperties[(int)propCount];
        vk.EnumerateInstanceLayerProperties(ref propCount, props);

        string[] ret = new string[propCount];
        for (int i = 0; i < propCount; i++)
            ret[i] = Util.GetString(props[i].LayerName);

        return ret;
    }


    public static bool TryFindMemoryType(this VkApi vk, PhysicalDeviceMemoryProperties memProperties, uint typeFilter, MemoryPropertyFlags properties, out uint typeIndex)
    {
        typeIndex = 0;

        for (int i = 0; i < memProperties.MemoryTypeCount; i++)
        {
            if (((typeFilter & (1 << i)) != 0)
                && (memProperties.MemoryTypes[i].PropertyFlags & properties) == properties)
            {
                typeIndex = (uint)i;
                return true;
            }
        }

        return false;
    }


    public static void CheckResult(this Result result)
    {
        if (result != Result.Success)
            throw new RenderException($"Unsuccessful VkResult: {result}");
    }
}
