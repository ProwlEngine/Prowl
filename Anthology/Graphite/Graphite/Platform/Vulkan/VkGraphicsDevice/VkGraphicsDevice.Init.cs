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
        if (debug && availableInstanceLayers.Contains(CommonStrings.KhronosValidationLayerName))
            availableInstanceExtensions.UnionWith(Vk.EnumerateInstanceExtensionProperties((byte*)CommonStrings.KhronosValidationLayerNameUtf8));

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
            HashSet<string> enabled = [];
            for (int i = 0; i < instanceExtensionCount; i++)
                enabled.Add(Util.GetString((byte*)instanceExtensions[i]));

            foreach (string requiredExt in requestedInstanceExtensions)
            {
                if (!availableInstanceExtensions.Contains(requiredExt))
                    throw new RenderException($"The required instance extension was not available: {requiredExt}");
                if (!enabled.Add(requiredExt))
                    continue;

                IntPtr utf8Str = Marshal.StringToCoTaskMemUTF8(requiredExt);
                instanceExtensions[instanceExtensionCount++] = utf8Str;
                tempStrings.Add(utf8Str);
            }

            if (availableInstanceExtensions.Contains(CommonStrings.VK_EXT_DEBUG_UTILS_EXTENSION_NAME))
            {
                if (enabled.Add(CommonStrings.VK_EXT_DEBUG_UTILS_EXTENSION_NAME))
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
                _debugUtilsEnabled = Vk.TryGetInstanceExtension(Instance, out DebugUtils);
                if (_debugUtilsEnabled && debug)
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

    private void CreatePhysicalDevice(Func<IntPtr, IntPtr>? pick)
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
            if (candidateProps.ApiVersion < new Version32(1, 1, 0))
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

        if (pick != null)
        {
            IntPtr picked = pick(Instance.Handle);
            if (Array.FindIndex(physicalDevices, d => d.Handle == picked) < 0)
                throw new RenderException("VulkanDeviceOptions.PickPhysicalDevice returned a handle that is not a physical device of this instance.");
            PhysicalDevice = new PhysicalDevice(picked);
        }

        Vk.GetPhysicalDeviceProperties(PhysicalDevice, out _physicalDeviceProperties);
        fixed (byte* utf8NamePtr = _physicalDeviceProperties.DeviceName)
        {
            _deviceName = Util.GetString(utf8NamePtr);
        }

        uint deviceApiVersion = _physicalDeviceProperties.ApiVersion;
        if (deviceApiVersion < new Version32(1, 1, 0))
        {
            throw new RenderException(
                $"Vulkan 1.1 is required, but '{_deviceName}' only supports {deviceApiVersion >> 22}.{(deviceApiVersion >> 12) & 0x3FF}.");
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
            MultiDrawIndirect = supported.MultiDrawIndirect,
            ShaderSampledImageArrayDynamicIndexing = supported.ShaderSampledImageArrayDynamicIndexing,
            ShaderStorageBufferArrayDynamicIndexing = supported.ShaderStorageBufferArrayDynamicIndexing,
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
            ShaderStorageImageReadWithoutFormat = supported.ShaderStorageImageReadWithoutFormat,
            FragmentStoresAndAtomics = supported.FragmentStoresAndAtomics
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
            DeviceQueueCreateInfo queueCreateInfo = new(sType: StructureType.DeviceQueueCreateInfo)
            {
                QueueFamilyIndex = index,
                QueueCount = 1
            };
            float priority = 1f;
            queueCreateInfo.PQueuePriorities = &priority;
            queueCreateInfos[i] = queueCreateInfo;
            i += 1;
        }

        PhysicalDeviceFeatures deviceFeatures = _physicalDeviceFeatures;

        ExtensionProperties[] props = GetDeviceExtensionProperties();

        HashSet<string> requiredInstanceExtensions = [with(options.DeviceExtensions ?? Array.Empty<string>())];

        bool core12 = _physicalDeviceProperties.ApiVersion >= new Version32(1, 2, 0);
        HashSet<string> promotedExtensions = [];

        bool pipelineStatistics = false;
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
                else if (extensionName == "VK_KHR_pipeline_executable_properties" && Environment.GetEnvironmentVariable("GRAPHITE_SHADER_STATS") == "1")
                {
                    activeExtensions[activeExtensionCount++] = (IntPtr)properties[property].ExtensionName;
                    pipelineStatistics = true;
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
                else if (!core12 && extensionName is VK_EXT_descriptor_indexing or VK_KHR_timeline_semaphore or VK_KHR_driver_properties)
                {
                    activeExtensions[activeExtensionCount++] = (IntPtr)properties[property].ExtensionName;
                    requiredInstanceExtensions.Remove(extensionName);
                    promotedExtensions.Add(extensionName);
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

        PhysicalDeviceVulkan11Features supported11 = new(sType: StructureType.PhysicalDeviceVulkan11Features);
        PhysicalDeviceVulkan12Features supported12 = new(sType: StructureType.PhysicalDeviceVulkan12Features, pNext: &supported11);
        if (core12)
        {
            PhysicalDeviceFeatures2 supportedFeatures = new(sType: StructureType.PhysicalDeviceFeatures2, pNext: &supported12);
            Vk.GetPhysicalDeviceFeatures2(PhysicalDevice, &supportedFeatures);
        }
        else
        {
            QueryPromotedFeatures(promotedExtensions, ref supported11, ref supported12);
        }

        _timelineSupported = supported12.TimelineSemaphore;
        if (!supported11.ShaderDrawParameters)
            throw new RenderException($"The Vulkan device '{_deviceName}' does not support shaderDrawParameters, which shaders need for gl_BaseInstance.");

        // Descriptor tables need every one of these. Storage images in a table update after bind only where supported.
        bool descriptorIndexing = _physicalDeviceFeatures.ShaderSampledImageArrayDynamicIndexing
            && _physicalDeviceFeatures.ShaderStorageBufferArrayDynamicIndexing
            && supported12.DescriptorBindingPartiallyBound
            && supported12.DescriptorBindingUpdateUnusedWhilePending
            && supported12.DescriptorBindingSampledImageUpdateAfterBind
            && supported12.DescriptorBindingStorageBufferUpdateAfterBind
            && supported12.ShaderSampledImageArrayNonUniformIndexing
            && supported12.ShaderStorageBufferArrayNonUniformIndexing;

        PhysicalDeviceVulkan12Features vulkan12Features = new(sType: StructureType.PhysicalDeviceVulkan12Features)
        {
            TimelineSemaphore = _timelineSupported,
            DrawIndirectCount = supported12.DrawIndirectCount,
            SamplerFilterMinmax = supported12.SamplerFilterMinmax,
            ShaderOutputViewportIndex = supported12.ShaderOutputViewportIndex,
            ShaderOutputLayer = supported12.ShaderOutputLayer,
            RuntimeDescriptorArray = descriptorIndexing && supported12.RuntimeDescriptorArray,
            DescriptorBindingPartiallyBound = descriptorIndexing,
            DescriptorBindingSampledImageUpdateAfterBind = descriptorIndexing,
            DescriptorBindingStorageImageUpdateAfterBind = descriptorIndexing && supported12.DescriptorBindingStorageImageUpdateAfterBind,
            DescriptorBindingStorageBufferUpdateAfterBind = descriptorIndexing,
            DescriptorBindingUpdateUnusedWhilePending = descriptorIndexing,
            ShaderSampledImageArrayNonUniformIndexing = descriptorIndexing,
            ShaderStorageBufferArrayNonUniformIndexing = descriptorIndexing,
        };
        PhysicalDeviceVulkan11Features vulkan11Features = new(sType: StructureType.PhysicalDeviceVulkan11Features, pNext: &vulkan12Features, shaderDrawParameters: true)
        {
            Multiview = supported11.Multiview,
        };

        PhysicalDeviceShaderDrawParametersFeatures drawParametersFeatures = new(sType: StructureType.PhysicalDeviceShaderDrawParametersFeatures) { ShaderDrawParameters = true };
        PhysicalDeviceMultiviewFeatures multiviewFeatures = new(sType: StructureType.PhysicalDeviceMultiviewFeatures, pNext: &drawParametersFeatures) { Multiview = vulkan11Features.Multiview };
        PhysicalDeviceDescriptorIndexingFeatures indexingFeatures = new(sType: StructureType.PhysicalDeviceDescriptorIndexingFeatures)
        {
            RuntimeDescriptorArray = vulkan12Features.RuntimeDescriptorArray,
            DescriptorBindingPartiallyBound = vulkan12Features.DescriptorBindingPartiallyBound,
            DescriptorBindingSampledImageUpdateAfterBind = vulkan12Features.DescriptorBindingSampledImageUpdateAfterBind,
            DescriptorBindingStorageImageUpdateAfterBind = vulkan12Features.DescriptorBindingStorageImageUpdateAfterBind,
            DescriptorBindingStorageBufferUpdateAfterBind = vulkan12Features.DescriptorBindingStorageBufferUpdateAfterBind,
            DescriptorBindingUpdateUnusedWhilePending = vulkan12Features.DescriptorBindingUpdateUnusedWhilePending,
            ShaderSampledImageArrayNonUniformIndexing = vulkan12Features.ShaderSampledImageArrayNonUniformIndexing,
            ShaderStorageBufferArrayNonUniformIndexing = vulkan12Features.ShaderStorageBufferArrayNonUniformIndexing,
        };
        PhysicalDeviceTimelineSemaphoreFeatures timelineFeatures = new(sType: StructureType.PhysicalDeviceTimelineSemaphoreFeatures) { TimelineSemaphore = _timelineSupported };

        void* featureChain = &vulkan11Features;
        if (!core12)
        {
            featureChain = &multiviewFeatures;
            if (promotedExtensions.Contains(VK_EXT_descriptor_indexing))
            {
                indexingFeatures.PNext = featureChain;
                featureChain = &indexingFeatures;
            }
            if (_timelineSupported)
            {
                timelineFeatures.PNext = featureChain;
                featureChain = &timelineFeatures;
            }
        }

        PhysicalDevicePipelineExecutablePropertiesFeaturesKHR statisticsFeatures = new(sType: StructureType.PhysicalDevicePipelineExecutablePropertiesFeaturesKhr) { PipelineExecutableInfo = true };
        if (pipelineStatistics)
        {
            statisticsFeatures.PNext = featureChain;
            featureChain = &statisticsFeatures;
        }

        DeviceCreateInfo deviceCreateInfo = new(sType: StructureType.DeviceCreateInfo, pNext: featureChain)
        {
            QueueCreateInfoCount = queueCreateInfosCount,
            PQueueCreateInfos = queueCreateInfos,

            PEnabledFeatures = &deviceFeatures
        };

        _enabledVulkan11Features = vulkan11Features with { PNext = null };
        _enabledVulkan12Features = vulkan12Features with { PNext = null };

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
        if (pipelineStatistics && Vk.TryGetDeviceExtension(Instance, Device, out KhrPipelineExecutableProperties statistics))
            PipelineStatistics = statistics;

        if (core12)
        {
            PhysicalDeviceVulkan12Properties driverProps = new(sType: StructureType.PhysicalDeviceVulkan12Properties);
            PhysicalDeviceProperties2 deviceProps = new(sType: StructureType.PhysicalDeviceProperties2, pNext: &driverProps);
            Vk.GetPhysicalDeviceProperties2(PhysicalDevice, &deviceProps);
            _vulkan12Properties = driverProps with { PNext = null };
            SetDriverInfo(driverProps.ConformanceVersion, driverProps.DriverName, driverProps.DriverInfo);
        }
        else
        {
            QueryPromotedProperties(promotedExtensions);
        }
    }

    private const string VK_EXT_descriptor_indexing = "VK_EXT_descriptor_indexing";
    private const string VK_KHR_timeline_semaphore = "VK_KHR_timeline_semaphore";
    private const string VK_KHR_driver_properties = "VK_KHR_driver_properties";

    private void SetDriverInfo(ConformanceVersion conforming, byte* driverName, byte* driverInfo)
    {
        _apiVersion = new GraphicsApiVersion(conforming.Major, conforming.Minor, conforming.Subminor, conforming.Patch);
        DriverName = Marshal.PtrToStringUTF8((nint)driverName) ?? string.Empty;
        DriverInfo = Marshal.PtrToStringUTF8((nint)driverInfo) ?? string.Empty;
    }

    /// <summary>Reads the Vulkan 1.1 and 1.2 features of a 1.1 device from their core 1.1 and extension structs.</summary>
    private void QueryPromotedFeatures(HashSet<string> extensions, ref PhysicalDeviceVulkan11Features supported11, ref PhysicalDeviceVulkan12Features supported12)
    {
        PhysicalDeviceShaderDrawParametersFeatures drawParameters = new(sType: StructureType.PhysicalDeviceShaderDrawParametersFeatures);
        PhysicalDeviceMultiviewFeatures multiview = new(sType: StructureType.PhysicalDeviceMultiviewFeatures, pNext: &drawParameters);
        PhysicalDeviceDescriptorIndexingFeatures indexing = new(sType: StructureType.PhysicalDeviceDescriptorIndexingFeatures);
        PhysicalDeviceTimelineSemaphoreFeatures timeline = new(sType: StructureType.PhysicalDeviceTimelineSemaphoreFeatures);

        void* chain = &multiview;
        if (extensions.Contains(VK_EXT_descriptor_indexing))
        {
            indexing.PNext = chain;
            chain = &indexing;
        }
        if (extensions.Contains(VK_KHR_timeline_semaphore))
        {
            timeline.PNext = chain;
            chain = &timeline;
        }

        PhysicalDeviceFeatures2 features = new(sType: StructureType.PhysicalDeviceFeatures2, pNext: chain);
        Vk.GetPhysicalDeviceFeatures2(PhysicalDevice, &features);

        supported11.ShaderDrawParameters = drawParameters.ShaderDrawParameters;
        supported11.Multiview = multiview.Multiview;
        supported12.TimelineSemaphore = timeline.TimelineSemaphore;
        supported12.RuntimeDescriptorArray = indexing.RuntimeDescriptorArray;
        supported12.DescriptorBindingPartiallyBound = indexing.DescriptorBindingPartiallyBound;
        supported12.DescriptorBindingUpdateUnusedWhilePending = indexing.DescriptorBindingUpdateUnusedWhilePending;
        supported12.DescriptorBindingSampledImageUpdateAfterBind = indexing.DescriptorBindingSampledImageUpdateAfterBind;
        supported12.DescriptorBindingStorageImageUpdateAfterBind = indexing.DescriptorBindingStorageImageUpdateAfterBind;
        supported12.DescriptorBindingStorageBufferUpdateAfterBind = indexing.DescriptorBindingStorageBufferUpdateAfterBind;
        supported12.ShaderSampledImageArrayNonUniformIndexing = indexing.ShaderSampledImageArrayNonUniformIndexing;
        supported12.ShaderStorageBufferArrayNonUniformIndexing = indexing.ShaderStorageBufferArrayNonUniformIndexing;
    }

    /// <summary>Reads the descriptor table limits and driver details of a 1.1 device from their extension structs.</summary>
    private void QueryPromotedProperties(HashSet<string> extensions)
    {
        PhysicalDeviceDescriptorIndexingProperties indexing = new(sType: StructureType.PhysicalDeviceDescriptorIndexingProperties);
        PhysicalDeviceDriverProperties driver = new(sType: StructureType.PhysicalDeviceDriverProperties);

        void* chain = null;
        if (extensions.Contains(VK_EXT_descriptor_indexing))
        {
            indexing.PNext = chain;
            chain = &indexing;
        }
        if (extensions.Contains(VK_KHR_driver_properties))
        {
            driver.PNext = chain;
            chain = &driver;
        }

        PhysicalDeviceProperties2 properties = new(sType: StructureType.PhysicalDeviceProperties2, pNext: chain);
        Vk.GetPhysicalDeviceProperties2(PhysicalDevice, &properties);

        _vulkan12Properties = new PhysicalDeviceVulkan12Properties
        {
            MaxPerStageDescriptorUpdateAfterBindSamplers = indexing.MaxPerStageDescriptorUpdateAfterBindSamplers,
            MaxPerStageDescriptorUpdateAfterBindUniformBuffers = indexing.MaxPerStageDescriptorUpdateAfterBindUniformBuffers,
            MaxPerStageDescriptorUpdateAfterBindStorageBuffers = indexing.MaxPerStageDescriptorUpdateAfterBindStorageBuffers,
            MaxPerStageDescriptorUpdateAfterBindSampledImages = indexing.MaxPerStageDescriptorUpdateAfterBindSampledImages,
            MaxPerStageDescriptorUpdateAfterBindStorageImages = indexing.MaxPerStageDescriptorUpdateAfterBindStorageImages,
            MaxPerStageUpdateAfterBindResources = indexing.MaxPerStageUpdateAfterBindResources,
            MaxDescriptorSetUpdateAfterBindSamplers = indexing.MaxDescriptorSetUpdateAfterBindSamplers,
            MaxDescriptorSetUpdateAfterBindUniformBuffers = indexing.MaxDescriptorSetUpdateAfterBindUniformBuffers,
            MaxDescriptorSetUpdateAfterBindUniformBuffersDynamic = indexing.MaxDescriptorSetUpdateAfterBindUniformBuffersDynamic,
            MaxDescriptorSetUpdateAfterBindStorageBuffers = indexing.MaxDescriptorSetUpdateAfterBindStorageBuffers,
            MaxDescriptorSetUpdateAfterBindSampledImages = indexing.MaxDescriptorSetUpdateAfterBindSampledImages,
            MaxDescriptorSetUpdateAfterBindStorageImages = indexing.MaxDescriptorSetUpdateAfterBindStorageImages,
        };

        if (extensions.Contains(VK_KHR_driver_properties))
            SetDriverInfo(driver.ConformanceVersion, driver.DriverName, driver.DriverInfo);
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

        bool[] presents = new bool[qfp.Length];
        if (surface.Handle != 0 && Vk.TryGetInstanceExtension(Instance, out KhrSurface khrSurface))
        {
            for (uint idx = 0; idx < qfp.Length; idx++)
            {
                khrSurface.GetPhysicalDeviceSurfaceSupport(PhysicalDevice, idx, surface, out Bool32 presentSupported);
                presents[idx] = presentSupported;
            }
        }

        QueueFlags[] flags = Array.ConvertAll(qfp, family => family.QueueFlags);
        (int graphics, int present) = ChooseQueueFamilies(flags, presents, surface.Handle != 0);
        if (graphics < 0)
            throw new RenderException($"The Vulkan device '{_deviceName}' has no queue family that does both graphics and compute.");
        if (present < 0)
            throw new RenderException($"The Vulkan device '{_deviceName}' has no queue family that can present to the surface.");

        GraphicsQueueIndex = (uint)graphics;
        PresentQueueIndex = (uint)present;
        _timestampValidBits = qfp[graphics].TimestampValidBits;
    }

    /// <summary>
    /// The graphics and compute family, preferring one that also presents so no image moves between queues, and the
    /// family presenting. Without a surface the graphics family stands in for present. -1 when none fits.
    /// </summary>
    internal static (int Graphics, int Present) ChooseQueueFamilies(ReadOnlySpan<QueueFlags> families, ReadOnlySpan<bool> presents, bool needsPresent)
    {
        const QueueFlags GraphicsAndCompute = QueueFlags.GraphicsBit | QueueFlags.ComputeBit;
        int graphics = -1, present = -1;
        for (int i = 0; i < families.Length; i++)
        {
            if ((families[i] & GraphicsAndCompute) != GraphicsAndCompute)
                continue;
            if (!needsPresent || presents[i])
                return (i, i);
            if (graphics < 0)
                graphics = i;
        }
        for (int i = 0; i < presents.Length && present < 0; i++)
        {
            if (presents[i])
                present = i;
        }
        return (graphics, present);
    }
}
