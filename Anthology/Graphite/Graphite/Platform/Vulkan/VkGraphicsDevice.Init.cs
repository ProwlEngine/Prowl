using System;
using System.Collections.Generic;
using System.Text;

using Silk.NET.Core;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;

namespace Prowl.Graphite.Vk;

internal unsafe partial class VkGraphicsDevice
{
    private const uint VK_INSTANCE_CREATE_ENUMERATE_PORTABILITY_BIT_KHR = 0x00000001;

    private bool _standardValidationSupported;
    private bool _khronosValidationSupported;

    private void CreateInstance(bool debug, VulkanDeviceOptions options, VkSurfaceSwapchainSource? surface)
    {
        HashSet<string> availableInstanceLayers = [.. Vk.EnumerateInstanceLayers((LayerProperties*)0)];
        HashSet<string> availableInstanceExtensions = [.. Vk.EnumerateInstanceExtensionProperties((byte*)0)];

        InstanceCreateInfo instanceCI = new(sType: StructureType.InstanceCreateInfo);
        ApplicationInfo applicationInfo = new(sType: StructureType.ApplicationInfo)
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
            HashSet<string> addedExtensions = [];
            string[] requested = [
                "VK_KHR_surface"
            ];

            for (int i = 0; i < extensionCount; i++)
            {
                instanceExtensions[instanceExtensionCount++] = (nint)surfaceExtensions[i];
                addedExtensions.Add(new FixedUtf8String(surfaceExtensions[i]));
            }

            for (int r = 0; r < requested.Length; r++)
            {
                if (addedExtensions.Contains(requested[r]))
                    continue;

                instanceExtensions[instanceExtensionCount++] = new FixedUtf8String(requested[r]);
            }
        }

        bool hasDeviceProperties2 = availableInstanceExtensions.Contains(CommonStrings.VK_KHR_get_physical_device_properties2);
        if (hasDeviceProperties2)
            instanceExtensions[instanceExtensionCount++] = CommonStrings.VK_KHR_get_physical_device_properties2;

        string[] requestedInstanceExtensions = options.InstanceExtensions ?? Array.Empty<string>();
        List<FixedUtf8String> tempStrings = [];
        foreach (string requiredExt in requestedInstanceExtensions)
        {
            if (!availableInstanceExtensions.Contains(requiredExt))
                throw new RenderException($"The required instance extension was not available: {requiredExt}");

            FixedUtf8String utf8Str = new(requiredExt);
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

        Vk.CreateInstance(in instanceCI, null, out Instance).CheckResult();

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
        // Just use the first enumerated device.
        // apologies to the dual-GPU crowd.
        PhysicalDevice = physicalDevices[0];

        Vk.GetPhysicalDeviceProperties(PhysicalDevice, out _physicalDeviceProperties);
        fixed (byte* utf8NamePtr = _physicalDeviceProperties.DeviceName)
        {
            _deviceName = Util.GetString(utf8NamePtr);
        }

        _vendorName = "id:" + _physicalDeviceProperties.VendorID.ToString("x8");
        _apiVersion = GraphicsApiVersion.Unknown;
        DriverInfo = "version:" + _physicalDeviceProperties.DriverVersion.ToString("x8");

        Vk.GetPhysicalDeviceFeatures(PhysicalDevice, out _physicalDeviceFeatures);

        Vk.GetPhysicalDeviceMemoryProperties(PhysicalDevice, out PhysicalDeviceMemProperties);
    }

    private void CreateLogicalDevice(SurfaceKHR surface, bool preferStandardClipY, VulkanDeviceOptions options)
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

        bool hasMemReqs2 = false;
        bool hasDedicatedAllocation = false;
        bool hasDriverProperties = false;
        bool hasMemoryBudget = false;
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
                else if (extensionName == "VK_EXT_memory_budget")
                {
                    activeExtensions[activeExtensionCount++] = (IntPtr)properties[property].ExtensionName;
                    requiredInstanceExtensions.Remove(extensionName);
                    hasMemoryBudget = true;
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

        DeviceCreateInfo deviceCreateInfo = new(sType: StructureType.DeviceCreateInfo);
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

            Vk.CreateDevice(PhysicalDevice, in deviceCreateInfo, null, out Device).CheckResult();
        }

        Vk.GetDeviceQueue(Device, GraphicsQueueIndex, 0, out GraphicsQueue);

        Vk.TryGetInstanceExtension(Instance, out KhrSurface);
        Vk.TryGetDeviceExtension(Instance, Device, out KhrSwapchain);

        if (_debugMarkerEnabled)
        {
            LoadDebugMarkerFunctions();
        }
        if (hasDedicatedAllocation && hasMemReqs2)
        {
            GetBufferMemoryRequirements2 = GetDeviceProcAddr<vkGetBufferMemoryRequirements2_t>("vkGetBufferMemoryRequirements2")
                ?? GetDeviceProcAddr<vkGetBufferMemoryRequirements2_t>("vkGetBufferMemoryRequirements2KHR");
            GetImageMemoryRequirements2 = GetDeviceProcAddr<vkGetImageMemoryRequirements2_t>("vkGetImageMemoryRequirements2")
                ?? GetDeviceProcAddr<vkGetImageMemoryRequirements2_t>("vkGetImageMemoryRequirements2KHR");
        }
        if (_getPhysicalDeviceProperties2 != null && hasMemoryBudget)
        {
            _getPhysicalDeviceMemoryProperties2 = GetInstanceProcAddr<vkGetPhysicalDeviceMemoryProperties2_t>("vkGetPhysicalDeviceMemoryProperties2")
                ?? GetInstanceProcAddr<vkGetPhysicalDeviceMemoryProperties2_t>("vkGetPhysicalDeviceMemoryProperties2KHR");
        }
        if (_getPhysicalDeviceProperties2 != null && hasDriverProperties)
        {
            PhysicalDeviceProperties2KHR deviceProps = new(sType: StructureType.PhysicalDeviceProperties2Khr);
            VkPhysicalDeviceDriverProperties driverProps = VkPhysicalDeviceDriverProperties.New();

            deviceProps.PNext = &driverProps;
            _getPhysicalDeviceProperties2(PhysicalDevice, &deviceProps);

            string driverName = Encoding.UTF8.GetString(
                driverProps.driverName, VkPhysicalDeviceDriverProperties.DriverNameLength).TrimEnd('\0');

            string driverInfo = Encoding.UTF8.GetString(
                driverProps.driverInfo, VkPhysicalDeviceDriverProperties.DriverInfoLength).TrimEnd('\0');

            VkConformanceVersion conforming = driverProps.conformanceVersion;
            _apiVersion = new GraphicsApiVersion(conforming.major, conforming.minor, conforming.subminor, conforming.patch);
            DriverName = driverName;
            DriverInfo = driverInfo;
        }
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

    private void CreateDescriptorPool()
    {
        DescriptorPoolManager = new VkDescriptorPoolManager(this);
    }

    private void CreateGraphicsCommandPool()
    {
        CommandPoolCreateInfo commandPoolCI = new(sType: StructureType.CommandPoolCreateInfo);
        commandPoolCI.Flags = CommandPoolCreateFlags.ResetCommandBufferBit;
        commandPoolCI.QueueFamilyIndex = GraphicsQueueIndex;
        Vk.CreateCommandPool(Device, in commandPoolCI, null, out _graphicsCommandPool).CheckResult();
    }
}
