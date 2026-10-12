using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Silk.NET.Core;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;

namespace Prowl.Graphite.Vk;

internal unsafe partial class VkGraphicsDevice
{
    private DebugUtilsMessengerEXT _debugMessenger;
    private bool _debugUtilsEnabled;

    // Stored validation error from the debug callback (cannot throw from unmanaged callback)
    private static volatile string? _lastValidationError;

    public void EnableDebugCallback(
        DebugUtilsMessageSeverityFlagsEXT severity = DebugUtilsMessageSeverityFlagsEXT.WarningBitExt | DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt)
    {
        if (DebugUtils == null)
            return;

        System.Diagnostics.Debug.WriteLine("Enabling Vulkan Debug callbacks.");
        DebugUtilsMessengerCreateInfoEXT createInfo = new(sType: StructureType.DebugUtilsMessengerCreateInfoExt)
        {
            MessageSeverity = severity,
            MessageType = DebugUtilsMessageTypeFlagsEXT.GeneralBitExt
                | DebugUtilsMessageTypeFlagsEXT.ValidationBitExt
                | DebugUtilsMessageTypeFlagsEXT.PerformanceBitExt,
            PfnUserCallback = new PfnDebugUtilsMessengerCallbackEXT(&DebugCallback)
        };

        DebugUtils.CreateDebugUtilsMessenger(Instance, in createInfo, null, out _debugMessenger).CheckResult();
    }

    private void DestroyDebugCallback()
    {
        if (_debugMessenger.Handle != 0)
        {
            DebugUtils!.DestroyDebugUtilsMessenger(Instance, _debugMessenger, null);
            _debugMessenger = default;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static Bool32 DebugCallback(
        DebugUtilsMessageSeverityFlagsEXT severity,
        DebugUtilsMessageTypeFlagsEXT types,
        DebugUtilsMessengerCallbackDataEXT* pCallbackData,
        void* pUserData)
    {
        string fullMessage = $"[{severity}] ({types}) {Util.GetString(pCallbackData->PMessage)}";

        if ((severity & DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt) != 0)
            _lastValidationError = fullMessage;
        else
            Console.WriteLine(fullMessage);

        return false;
    }

    /// <summary>
    /// Throws if Vulkan reported a validation error. Call after ops that could trigger one.
    /// </summary>
    internal static void FlushValidationErrors()
    {
        if (_lastValidationError == null)
            return;

        string error = _lastValidationError;
        _lastValidationError = null;
        throw new RenderException("A Vulkan validation error was encountered: " + error);
    }

    internal void SetResourceName(GraphicsResource resource, string name)
    {
        if (!_debugUtilsEnabled)
            return;

        switch (resource)
        {
            case VkBuffer buffer:
                SetDebugUtilsName(ObjectType.Buffer, buffer.DeviceBuffer.Handle, name);
                break;
            case VkCommandBuffer CommandBuffer:
                SetDebugUtilsName(
                    ObjectType.CommandBuffer,
                    (ulong)CommandBuffer.CommandBuffer.Handle,
                    $"{name}_CommandBuffer");
                SetDebugUtilsName(
                    ObjectType.CommandPool,
                    CommandBuffer.CommandPool.Handle,
                    $"{name}_CommandPool");
                break;
            case VkFramebuffer framebuffer:
                SetDebugUtilsName(
                    ObjectType.Framebuffer,
                    framebuffer.CurrentFramebuffer.Handle,
                    name);
                break;
            case VkSampler sampler:
                SetDebugUtilsName(ObjectType.Sampler, sampler.DeviceSampler.Handle, name);
                break;
            case VkDescriptorTable table:
                SetDebugUtilsName(ObjectType.DescriptorSet, table.Set.Handle, name);
                break;
            case VkGraphicsProgram shaderProgram:
                foreach (ShaderModule module in shaderProgram.Modules.Values)
                {
                    SetDebugUtilsName(ObjectType.ShaderModule, module.Handle, name);
                }
                break;
            case VkComputeProgram computeProgram:
                SetDebugUtilsName(ObjectType.Pipeline, computeProgram.DevicePipeline.Handle, name);
                break;
            case VkTexture tex:
                SetDebugUtilsName(ObjectType.Image, tex.OptimalDeviceImage.Handle, name);
                break;
            case VkTextureView texView:
                SetDebugUtilsName(ObjectType.ImageView, texView.ImageView.Handle, name);
                break;
            case VkSwapchain sc:
                SetDebugUtilsName(ObjectType.SwapchainKhr, sc.DeviceSwapchain.Handle, name);
                break;
            default:
                break;
        }
    }

    /// <summary>Prints what the driver reports about each stage of a pipeline, such as its register count.</summary>
    internal void PrintPipelineStatistics(Pipeline pipeline, string name)
    {
        if (PipelineStatistics == null)
            return;
        PipelineInfoKHR info = new(sType: StructureType.PipelineInfoKhr, pipeline: pipeline);
        uint executables = 0;
        PipelineStatistics.GetPipelineExecutableProperties(Device, &info, &executables, null);
        PipelineExecutablePropertiesKHR* properties = stackalloc PipelineExecutablePropertiesKHR[(int)executables];
        for (uint i = 0; i < executables; i++)
            properties[i] = new PipelineExecutablePropertiesKHR(sType: StructureType.PipelineExecutablePropertiesKhr);
        PipelineStatistics.GetPipelineExecutableProperties(Device, &info, &executables, properties);

        for (uint i = 0; i < executables; i++)
        {
            PipelineExecutableInfoKHR executable = new(sType: StructureType.PipelineExecutableInfoKhr, pipeline: pipeline, executableIndex: i);
            uint count = 0;
            PipelineStatistics.GetPipelineExecutableStatistic(Device, &executable, &count, null);
            PipelineExecutableStatisticKHR* statistics = stackalloc PipelineExecutableStatisticKHR[(int)count];
            for (uint s = 0; s < count; s++)
                statistics[s] = new PipelineExecutableStatisticKHR(sType: StructureType.PipelineExecutableStatisticKhr);
            PipelineStatistics.GetPipelineExecutableStatistic(Device, &executable, &count, statistics);

            System.Text.StringBuilder line = new($"[Graphite stats] {name} {Util.GetString(properties[i].Name)}:");
            for (uint s = 0; s < count; s++)
            {
                PipelineExecutableStatisticKHR stat = statistics[s];
                string value = stat.Format switch
                {
                    PipelineExecutableStatisticFormatKHR.Bool32Khr => stat.Value.B32.ToString(),
                    PipelineExecutableStatisticFormatKHR.Int64Khr => stat.Value.I64.ToString(),
                    PipelineExecutableStatisticFormatKHR.Uint64Khr => stat.Value.U64.ToString(),
                    _ => stat.Value.F64.ToString("0.##"),
                };
                line.Append($" {Util.GetString(stat.Name)}={value};");
            }
            Console.WriteLine(line.ToString());
        }
    }

    private void SetDebugUtilsName(ObjectType type, ulong target, string name)
    {
        System.Diagnostics.Debug.Assert(DebugUtils != null);

        DebugUtilsObjectNameInfoEXT nameInfo = new(sType: StructureType.DebugUtilsObjectNameInfoExt);
        nameInfo.ObjectType = type;
        nameInfo.ObjectHandle = target;

        byte* utf8Ptr = stackalloc byte[Utf8Stack.ByteCount(name)];
        Utf8Stack.Write(name, utf8Ptr);

        nameInfo.PObjectName = utf8Ptr;
        DebugUtils!.SetDebugUtilsObjectName(Device, &nameInfo).CheckResult();
    }
}
