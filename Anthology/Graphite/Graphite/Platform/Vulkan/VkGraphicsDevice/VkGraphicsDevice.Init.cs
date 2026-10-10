using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Silk.NET.Core;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;

namespace Prowl.Graphite.Vk;

internal unsafe partial class VkGraphicsDevice
{
    private const uint VK_INSTANCE_CREATE_ENUMERATE_PORTABILITY_BIT_KHR = 0x00000001;

    private bool _khronosValidationSupported;
    private bool _surfaceExtensionEnabled;

    private void CreateInstance(bool debug, VulkanDeviceOptions options, SwapchainSource? surface)
    {
        HashSet<string> availableInstanceLayers = [.. Vk.EnumerateInstanceLayers((LayerProperties*)0)];
        HashSet<string> availableInstanceExtensions = [.. Vk.EnumerateInstanceExtensionProperties((byte*)0)];

        InstanceCreateInfo instanceCI = new(sType: StructureType.InstanceCreateInfo);
        ApplicationInfo applicationInfo = new(sType: StructureType.ApplicationInfo)
        {
            ApiVersion = new Version32(1, 2, 0),
            ApplicationVersion = new Version32(1, 0, 0),
            EngineVersion = new Version32(1, 0, 0),
            PApplicationName = Name,
            PEngineName = Name
        };

        instanceCI.PApplicationInfo = &applicationInfo;

        // Capacity = the caller's requested extensions plus the fixed ones added below. The
        // fixed set is at most 8 (portability_enumeration + up to 5 platform surface extensions
        // + properties2); 16 leaves headroom so adding one can't overflow silently.
        int maxInstanceExtensions = (options.InstanceExtensions?.Length ?? 0) + 16;
        IntPtr* instanceExtensions = stackalloc IntPtr[maxInstanceExtensions];
        uint instanceExtensionCount = 0;
        IntPtr* instanceLayers = stackalloc IntPtr[1];
        uint instanceLayerCount = 0;

        if (availableInstanceExtensions.Contains(CommonStrings.VK_KHR_portability_subset))
            instanceExtensions[instanceExtensionCount++] = (nint)CommonStrings.VK_KHR_portability_subsetUtf8;

        if (availableInstanceExtensions.Contains(CommonStrings.VK_KHR_portability_enumeration))
        {
            instanceExtensions[instanceExtensionCount++] = (nint)CommonStrings.VK_KHR_portability_enumerationUtf8;
            instanceCI.Flags |= (InstanceCreateFlags)VK_INSTANCE_CREATE_ENUMERATE_PORTABILITY_BIT_KHR;
        }

        if (surface != null)
        {
            _surfaceExtensionEnabled = true;
            byte** surfaceExtensions = surface.VkSurface.GetRequiredExtensions(out uint extensionCount);
            HashSet<string> addedExtensions = [];

            for (int i = 0; i < extensionCount; i++)
            {
                instanceExtensions[instanceExtensionCount++] = (nint)surfaceExtensions[i];
                addedExtensions.Add(Util.GetString(surfaceExtensions[i]));
            }

            if (!addedExtensions.Contains(CommonStrings.VK_KHR_SURFACE_EXTENSION_NAME))
                instanceExtensions[instanceExtensionCount++] = (nint)CommonStrings.VK_KHR_SURFACE_EXTENSION_NAMEUtf8;
        }

        string[] requestedInstanceExtensions = options.InstanceExtensions ?? Array.Empty<string>();
        List<IntPtr> tempStrings = [];
        try
        {
            foreach (string requiredExt in requestedInstanceExtensions)
            {
                if (!availableInstanceExtensions.Contains(requiredExt))
                    throw new RenderException($"The required instance extension was not available: {requiredExt}");

                IntPtr utf8Str = Marshal.StringToCoTaskMemUTF8(requiredExt);
                instanceExtensions[instanceExtensionCount++] = utf8Str;
                tempStrings.Add(utf8Str);
            }

            if (availableInstanceExtensions.Contains(CommonStrings.VK_EXT_DEBUG_UTILS_EXTENSION_NAME))
            {
                instanceExtensions[instanceExtensionCount++] = (nint)CommonStrings.VK_EXT_DEBUG_UTILS_EXTENSION_NAMEUtf8;
                _debugUtilsEnabled = true;
            }

            if (debug && availableInstanceLayers.Contains(CommonStrings.KhronosValidationLayerName))
            {
                _khronosValidationSupported = true;
                instanceLayers[instanceLayerCount++] = (nint)CommonStrings.KhronosValidationLayerNameUtf8;
            }

            instanceCI.EnabledExtensionCount = instanceExtensionCount;
            instanceCI.PpEnabledExtensionNames = (byte**)instanceExtensions;

            instanceCI.EnabledLayerCount = instanceLayerCount;
            if (instanceLayerCount > 0)
            {
                instanceCI.PpEnabledLayerNames = (byte**)instanceLayers;
            }

            Vk.CreateInstance(in instanceCI, null, out Instance).CheckResult();

            if (_debugUtilsEnabled)
            {
                Vk.TryGetInstanceExtension(Instance, out DebugUtils);
                if (debug)
                    EnableDebugCallback();
            }
        }
        finally
        {
            foreach (IntPtr tempStr in tempStrings)
            {
                Marshal.FreeCoTaskMem(tempStr);
            }
        }
    }

    private void CreatePhysicalDevice()
    {
        uint deviceCount = 0;
        Vk.EnumeratePhysicalDevices(Instance, ref deviceCount, null);
        if (deviceCount == 0)
        {
            throw new InvalidOperationException("No physical devices exist.");
        }

        PhysicalDevice[] physicalDevices = new PhysicalDevice[deviceCount];
        fixed (PhysicalDevice* devicesPtr = physicalDevices)
        {
            Vk.EnumeratePhysicalDevices(Instance, ref deviceCount, devicesPtr);
        }
        PhysicalDevice = physicalDevices[0];
        int bestScore = -1;
        foreach (PhysicalDevice candidate in physicalDevices)
        {
            Vk.GetPhysicalDeviceProperties(candidate, out PhysicalDeviceProperties candidateProps);
            if (candidateProps.ApiVersion < new Version32(1, 2, 0))
                continue;

            int score = candidateProps.DeviceType switch
            {
                PhysicalDeviceType.DiscreteGpu => 4,
                PhysicalDeviceType.IntegratedGpu => 3,
                PhysicalDeviceType.VirtualGpu => 2,
                PhysicalDeviceType.Cpu => 1,
                _ => 0
            };
            if (score > bestScore)
            {
                bestScore = score;
                PhysicalDevice = candidate;
            }
        }

        Vk.GetPhysicalDeviceProperties(PhysicalDevice, out _physicalDeviceProperties);
        fixed (byte* utf8NamePtr = _physicalDeviceProperties.DeviceName)
        {
            _deviceName = Util.GetString(utf8NamePtr);
        }

        uint deviceApiVersion = _physicalDeviceProperties.ApiVersion;
        if (deviceApiVersion < new Version32(1, 2, 0))
        {
            throw new RenderException(
                $"Vulkan 1.2 is required, but '{_deviceName}' only supports {deviceApiVersion >> 22}.{(deviceApiVersion >> 12) & 0x3FF}.");
        }

        _vendorName = "id:" + _physicalDeviceProperties.VendorID.ToString("x8");
        _apiVersion = GraphicsApiVersion.Unknown;
        DriverInfo = "version:" + _physicalDeviceProperties.DriverVersion.ToString("x8");

        Vk.GetPhysicalDeviceFeatures(PhysicalDevice, out PhysicalDeviceFeatures supported);
        _physicalDeviceFeatures = new PhysicalDeviceFeatures
        {
            GeometryShader = supported.GeometryShader,
            TessellationShader = supported.TessellationShader,
            DrawIndirectFirstInstance = supported.DrawIndirectFirstInstance,
            SamplerAnisotropy = supported.SamplerAnisotropy,
            DepthClamp = supported.DepthClamp,
            DepthBiasClamp = supported.DepthBiasClamp,
            IndependentBlend = supported.IndependentBlend,
            ShaderFloat64 = supported.ShaderFloat64,
            PipelineStatisticsQuery = supported.PipelineStatisticsQuery,
            TextureCompressionBC = supported.TextureCompressionBC,
            TextureCompressionEtc2 = supported.TextureCompressionEtc2,
            TextureCompressionAstcLdr = supported.TextureCompressionAstcLdr,
            ImageCubeArray = supported.ImageCubeArray,
            ShaderStorageImageWriteWithoutFormat = supported.ShaderStorageImageWriteWithoutFormat,
            ShaderStorageImageReadWithoutFormat = supported.ShaderStorageImageReadWithoutFormat
        };

        Vk.GetPhysicalDeviceMemoryProperties(PhysicalDevice, out PhysicalDeviceMemProperties);
    }

    private void CreateLogicalDevice(SurfaceKHR surface, VulkanDeviceOptions options)
    {
        GetQueueFamilyIndices(surface);

        HashSet<uint> familyIndices = [GraphicsQueueIndex, PresentQueueIndex];
        DeviceQueueCreateInfo* queueCreateInfos = stackalloc DeviceQueueCreateInfo[familyIndices.Count];
        uint queueCreateInfosCount = (uint)familyIndices.Count;

        int i = 0;
        foreach (uint index in familyIndices)
        {
            DeviceQueueCreateInfo queueCreateInfo = new(sType: StructureType.DeviceQueueCreateInfo);
            queueCreateInfo.QueueFamilyIndex = index;
            queueCreateInfo.QueueCount = 1;
            float priority = 1f;
            queueCreateInfo.PQueuePriorities = &priority;
            queueCreateInfos[i] = queueCreateInfo;
            i += 1;
        }

        PhysicalDeviceFeatures deviceFeatures = _physicalDeviceFeatures;

        ExtensionProperties[] props = GetDeviceExtensionProperties();

        HashSet<string> requiredInstanceExtensions = new(options.DeviceExtensions ?? Array.Empty<string>());

        bool hasMaintenance1 = false;
        IntPtr[] activeExtensions = new IntPtr[props.Length];
        uint activeExtensionCount = 0;

        fixed (ExtensionProperties* properties = props)
        {
            for (int property = 0; property < props.Length; property++)
            {
                string extensionName = Util.GetString(properties[property].ExtensionName);
                if (extensionName == "VK_KHR_swapchain")
                {
                    if (!_surfaceExtensionEnabled)
                        continue;

                    activeExtensions[activeExtensionCount++] = (IntPtr)properties[property].ExtensionName;
                    requiredInstanceExtensions.Remove(extensionName);
                }
                else if (extensionName == "VK_KHR_maintenance1")
                {
                    activeExtensions[activeExtensionCount++] = (IntPtr)properties[property].ExtensionName;
                    requiredInstanceExtensions.Remove(extensionName);
                    hasMaintenance1 = true;
                }
                else if (extensionName == "VK_EXT_memory_budget")
                {
                    activeExtensions[activeExtensionCount++] = (IntPtr)properties[property].ExtensionName;
                    requiredInstanceExtensions.Remove(extensionName);
                    _memoryBudgetSupported = true;
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

        if (!hasMaintenance1)
        {
            throw new RenderException("VK_KHR_maintenance1 is required for the fixed clip space Y direction.");
        }

        if (requiredInstanceExtensions.Count != 0)
        {
            string missingList = string.Join(", ", requiredInstanceExtensions);
            throw new RenderException(
                $"The following Vulkan device extensions were not available: {missingList}");
        }

        PhysicalDeviceVulkan12Features vulkan12Features = new(sType: StructureType.PhysicalDeviceVulkan12Features);
        PhysicalDeviceFeatures2 supportedFeatures = new(sType: StructureType.PhysicalDeviceFeatures2, pNext: &vulkan12Features);
        Vk.GetPhysicalDeviceFeatures2(PhysicalDevice, &supportedFeatures);
        if (!vulkan12Features.TimelineSemaphore)
            throw new RenderException($"The Vulkan device '{_deviceName}' does not support timeline semaphores.");

        vulkan12Features = new(sType: StructureType.PhysicalDeviceVulkan12Features, timelineSemaphore: true);
        PhysicalDeviceVulkan11Features vulkan11Features = new(sType: StructureType.PhysicalDeviceVulkan11Features, pNext: &vulkan12Features, shaderDrawParameters: true);

        DeviceCreateInfo deviceCreateInfo = new(sType: StructureType.DeviceCreateInfo, pNext: &vulkan11Features);
        deviceCreateInfo.QueueCreateInfoCount = queueCreateInfosCount;
        deviceCreateInfo.PQueueCreateInfos = queueCreateInfos;

        deviceCreateInfo.PEnabledFeatures = &deviceFeatures;

        IntPtr* layerNames = stackalloc IntPtr[1];
        uint layerNameCount = 0;
        if (_khronosValidationSupported)
        {
            layerNames[layerNameCount++] = (nint)CommonStrings.KhronosValidationLayerNameUtf8;
        }
        deviceCreateInfo.EnabledLayerCount = layerNameCount;
        deviceCreateInfo.PpEnabledLayerNames = (byte**)layerNames;

        fixed (IntPtr* activeExtensionsPtr = activeExtensions)
        {
            deviceCreateInfo.EnabledExtensionCount = activeExtensionCount;
            deviceCreateInfo.PpEnabledExtensionNames = (byte**)activeExtensionsPtr;

            Vk.CreateDevice(PhysicalDevice, in deviceCreateInfo, null, out Device).CheckResult();
        }

        Vk.GetDeviceQueue(Device, GraphicsQueueIndex, 0, out GraphicsQueue);

        CreateTimelineSemaphore();

        Vk.TryGetInstanceExtension(Instance, out KhrSurface);
        Vk.TryGetDeviceExtension(Instance, Device, out KhrSwapchain);

        PhysicalDeviceVulkan12Properties driverProps = new(sType: StructureType.PhysicalDeviceVulkan12Properties);
        PhysicalDeviceProperties2 deviceProps = new(sType: StructureType.PhysicalDeviceProperties2, pNext: &driverProps);
        Vk.GetPhysicalDeviceProperties2(PhysicalDevice, &deviceProps);

        ConformanceVersion conforming = driverProps.ConformanceVersion;
        _apiVersion = new GraphicsApiVersion(conforming.Major, conforming.Minor, conforming.Subminor, conforming.Patch);
        DriverName = Marshal.PtrToStringUTF8((nint)driverProps.DriverName) ?? string.Empty;
        DriverInfo = Marshal.PtrToStringUTF8((nint)driverProps.DriverInfo) ?? string.Empty;
    }

    private void GetQueueFamilyIndices(SurfaceKHR surface)
    {
        uint queueFamilyCount = 0;
        Vk.GetPhysicalDeviceQueueFamilyProperties(PhysicalDevice, ref queueFamilyCount, null);
        QueueFamilyProperties[] qfp = new QueueFamilyProperties[queueFamilyCount];
        fixed (QueueFamilyProperties* qfpPtr = qfp)
        {
            Vk.GetPhysicalDeviceQueueFamilyProperties(PhysicalDevice, ref queueFamilyCount, qfpPtr);
        }

        bool foundGraphics = false;
        bool foundPresent = surface.Handle == 0;

        for (uint idx = 0; idx < qfp.Length; idx++)
        {
            if ((qfp[idx].QueueFlags & QueueFlags.GraphicsBit) != 0)
            {
                GraphicsQueueIndex = idx;
                foundGraphics = true;
            }

            if (!foundPresent)
            {
                if (Vk.TryGetInstanceExtension(Instance, out KhrSurface khrSurface))
                {
                    khrSurface.GetPhysicalDeviceSurfaceSupport(PhysicalDevice, idx, surface, out Bool32 presentSupported);
                    if (presentSupported)
                    {
                        PresentQueueIndex = idx;
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
}
