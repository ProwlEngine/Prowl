using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

using Silk.NET.Core;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Vulkan.Extensions.KHR;

namespace Prowl.Graphite.Vk;

internal unsafe partial class VkGraphicsDevice
{
    private const uint VK_INSTANCE_CREATE_ENUMERATE_PORTABILITY_BIT_KHR = 0x00000001;

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

        _vk.CreateInstance(in instanceCI, null, out _instance).CheckResult();

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

    public void EnableDebugCallback(DebugReportFlagsEXT flags = DebugReportFlagsEXT.WarningBitExt | DebugReportFlagsEXT.ErrorBitExt)
    {
        Debug.WriteLine("Enabling Vulkan Debug callbacks.");
        _debugCallbackFunc = new PfnDebugReportCallbackEXT(&DebugCallback);
        DebugReportCallbackCreateInfoEXT debugCallbackCI = new(sType: StructureType.DebugReportCallbackCreateInfoExt);
        debugCallbackCI.Flags = flags;
        debugCallbackCI.PfnCallback = _debugCallbackFunc;

        if (_vk.TryGetInstanceExtension(_instance, out _extDebugReport))
        {
            _extDebugReport.CreateDebugReportCallback(_instance, in debugCallbackCI, null, out _debugCallbackHandle).CheckResult();
        }
    }

    // Stored validation error from the debug callback (cannot throw from unmanaged callback)
    private static volatile string? _lastValidationError;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static Bool32 DebugCallback(
        DebugReportFlagsEXT flags,
        DebugReportObjectTypeEXT objectType,
        ulong @object,
        nuint location,
        int messageCode,
        byte* pLayerPrefix,
        byte* pMessage,
        void* pUserData)
    {
        string message = Util.GetString(pMessage);
        DebugReportFlagsEXT debugReportFlags = flags;

        string fullMessage = $"[{debugReportFlags}] ({objectType}) {message}";

        if (debugReportFlags == DebugReportFlagsEXT.ErrorBitExt)
        {
            _lastValidationError = fullMessage;
            return true;
        }

        Console.WriteLine(fullMessage);
        return false;
    }

    private void CreatePhysicalDevice()
    {
        uint deviceCount = 0;
        _vk.EnumeratePhysicalDevices(_instance, ref deviceCount, null);
        if (deviceCount == 0)
        {
            throw new InvalidOperationException("No physical devices exist.");
        }

        PhysicalDevice[] physicalDevices = new PhysicalDevice[deviceCount];
        fixed (PhysicalDevice* devicesPtr = physicalDevices)
        {
            _vk.EnumeratePhysicalDevices(_instance, ref deviceCount, devicesPtr);
        }
        // Just use the first enumerated device.
        // apologies to the dual-GPU crowd.
        _physicalDevice = physicalDevices[0];

        _vk.GetPhysicalDeviceProperties(_physicalDevice, out _physicalDeviceProperties);
        fixed (byte* utf8NamePtr = _physicalDeviceProperties.DeviceName)
        {
            _deviceName = Util.GetString(utf8NamePtr);
        }

        _vendorName = "id:" + _physicalDeviceProperties.VendorID.ToString("x8");
        _apiVersion = GraphicsApiVersion.Unknown;
        _driverInfo = "version:" + _physicalDeviceProperties.DriverVersion.ToString("x8");

        _vk.GetPhysicalDeviceFeatures(_physicalDevice, out _physicalDeviceFeatures);

        _vk.GetPhysicalDeviceMemoryProperties(_physicalDevice, out _physicalDeviceMemProperties);
    }

    public ExtensionProperties[] GetDeviceExtensionProperties()
    {
        uint propertyCount = 0;
        _vk.EnumerateDeviceExtensionProperties(_physicalDevice, (byte*)null, &propertyCount, null).CheckResult();
        ExtensionProperties[] props = new ExtensionProperties[(int)propertyCount];
        fixed (ExtensionProperties* properties = props)
        {
            _vk.EnumerateDeviceExtensionProperties(_physicalDevice, (byte*)null, &propertyCount, properties).CheckResult();
        }
        return props;
    }

    private void CreateLogicalDevice(SurfaceKHR surface, bool preferStandardClipY, VulkanDeviceOptions options)
    {
        GetQueueFamilyIndices(surface);

        HashSet<uint> familyIndices = [_graphicsQueueIndex, _presentQueueIndex];
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

            _vk.CreateDevice(_physicalDevice, in deviceCreateInfo, null, out _device).CheckResult();
        }

        _vk.GetDeviceQueue(_device, _graphicsQueueIndex, 0, out _graphicsQueue);

        _vk.TryGetInstanceExtension(_instance, out _khrSurface);
        _vk.TryGetDeviceExtension(_instance, _device, out _khrSwapchain);

        if (_debugMarkerEnabled)
        {
            _setObjectNameDelegate = Marshal.GetDelegateForFunctionPointer<vkDebugMarkerSetObjectNameEXT_t>(
                GetInstanceProcAddr("vkDebugMarkerSetObjectNameEXT"));
            _markerBegin = Marshal.GetDelegateForFunctionPointer<vkCmdDebugMarkerBeginEXT_t>(
                GetInstanceProcAddr("vkCmdDebugMarkerBeginEXT"));
            _markerEnd = Marshal.GetDelegateForFunctionPointer<vkCmdDebugMarkerEndEXT_t>(
                GetInstanceProcAddr("vkCmdDebugMarkerEndEXT"));
            _markerInsert = Marshal.GetDelegateForFunctionPointer<vkCmdDebugMarkerInsertEXT_t>(
                GetInstanceProcAddr("vkCmdDebugMarkerInsertEXT"));
        }
        if (hasDedicatedAllocation && hasMemReqs2)
        {
            _getBufferMemoryRequirements2 = GetDeviceProcAddr<vkGetBufferMemoryRequirements2_t>("vkGetBufferMemoryRequirements2")
                ?? GetDeviceProcAddr<vkGetBufferMemoryRequirements2_t>("vkGetBufferMemoryRequirements2KHR");
            _getImageMemoryRequirements2 = GetDeviceProcAddr<vkGetImageMemoryRequirements2_t>("vkGetImageMemoryRequirements2")
                ?? GetDeviceProcAddr<vkGetImageMemoryRequirements2_t>("vkGetImageMemoryRequirements2KHR");
        }
        if (_getPhysicalDeviceProperties2 != null && hasDriverProperties)
        {
            PhysicalDeviceProperties2KHR deviceProps = new(sType: StructureType.PhysicalDeviceProperties2Khr);
            VkPhysicalDeviceDriverProperties driverProps = VkPhysicalDeviceDriverProperties.New();

            deviceProps.PNext = &driverProps;
            _getPhysicalDeviceProperties2(_physicalDevice, &deviceProps);

            string driverName = Encoding.UTF8.GetString(
                driverProps.driverName, VkPhysicalDeviceDriverProperties.DriverNameLength).TrimEnd('\0');

            string driverInfo = Encoding.UTF8.GetString(
                driverProps.driverInfo, VkPhysicalDeviceDriverProperties.DriverInfoLength).TrimEnd('\0');

            VkConformanceVersion conforming = driverProps.conformanceVersion;
            _apiVersion = new GraphicsApiVersion(conforming.major, conforming.minor, conforming.subminor, conforming.patch);
            _driverName = driverName;
            _driverInfo = driverInfo;
        }
    }

    private IntPtr GetInstanceProcAddr(string name)
    {
        byte* utf8Ptr = stackalloc byte[Utf8Stack.ByteCount(name)];
        Utf8Stack.Write(name, utf8Ptr);

        return (IntPtr)_vk.GetInstanceProcAddr(_instance, utf8Ptr);
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

        return (IntPtr)_vk.GetDeviceProcAddr(_device, utf8Ptr);
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

    private void GetQueueFamilyIndices(SurfaceKHR surface)
    {
        uint queueFamilyCount = 0;
        _vk.GetPhysicalDeviceQueueFamilyProperties(_physicalDevice, ref queueFamilyCount, null);
        QueueFamilyProperties[] qfp = new QueueFamilyProperties[queueFamilyCount];
        fixed (QueueFamilyProperties* qfpPtr = qfp)
        {
            _vk.GetPhysicalDeviceQueueFamilyProperties(_physicalDevice, ref queueFamilyCount, qfpPtr);
        }

        bool foundGraphics = false;
        bool foundPresent = surface.Handle == 0;

        for (uint idx = 0; idx < qfp.Length; idx++)
        {
            if ((qfp[idx].QueueFlags & QueueFlags.GraphicsBit) != 0)
            {
                _graphicsQueueIndex = idx;
                foundGraphics = true;
            }

            if (!foundPresent)
            {
                if (_vk.TryGetInstanceExtension(_instance, out KhrSurface khrSurface))
                {
                    khrSurface.GetPhysicalDeviceSurfaceSupport(_physicalDevice, idx, surface, out Bool32 presentSupported);
                    if (presentSupported)
                    {
                        _presentQueueIndex = idx;
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
        _descriptorPoolManager = new VkDescriptorPoolManager(this);
    }

    private void CreateGraphicsCommandPool()
    {
        CommandPoolCreateInfo commandPoolCI = new(sType: StructureType.CommandPoolCreateInfo);
        commandPoolCI.Flags = CommandPoolCreateFlags.ResetCommandBufferBit;
        commandPoolCI.QueueFamilyIndex = _graphicsQueueIndex;
        _vk.CreateCommandPool(_device, in commandPoolCI, null, out _graphicsCommandPool).CheckResult();
    }
}
