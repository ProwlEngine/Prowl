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
    internal vkCmdDebugMarkerBeginEXT_t MarkerBegin;
    internal vkCmdDebugMarkerEndEXT_t MarkerEnd;
    internal vkCmdDebugMarkerInsertEXT_t MarkerInsert;

    private DebugReportCallbackEXT _debugCallbackHandle;
    private PfnDebugReportCallbackEXT _debugCallbackFunc;
    private bool _debugMarkerEnabled;
    private vkDebugMarkerSetObjectNameEXT_t _setObjectNameDelegate;

    // Stored validation error from the debug callback (cannot throw from unmanaged callback)
    private static volatile string? _lastValidationError;

    public void EnableDebugCallback(DebugReportFlagsEXT flags = DebugReportFlagsEXT.WarningBitExt | DebugReportFlagsEXT.ErrorBitExt)
    {
        Debug.WriteLine("Enabling Vulkan Debug callbacks.");
        _debugCallbackFunc = new PfnDebugReportCallbackEXT(&DebugCallback);
        DebugReportCallbackCreateInfoEXT debugCallbackCI = new(sType: StructureType.DebugReportCallbackCreateInfoExt);
        debugCallbackCI.Flags = flags;
        debugCallbackCI.PfnCallback = _debugCallbackFunc;

        if (Vk.TryGetInstanceExtension(Instance, out _extDebugReport))
        {
            _extDebugReport.CreateDebugReportCallback(Instance, in debugCallbackCI, null, out _debugCallbackHandle).CheckResult();
        }
    }

    private void DestroyDebugCallback()
    {
        if (_debugCallbackFunc.Handle != default)
        {
            _extDebugReport?.DestroyDebugReportCallback(Instance, _debugCallbackHandle, null);
        }
    }

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
        if (!_debugMarkerEnabled)
            return;

        switch (resource)
        {
            case VkBuffer buffer:
                SetDebugMarkerName(DebugReportObjectTypeEXT.BufferExt, buffer.DeviceBuffer.Handle, name);
                break;
            case VkCommandBuffer CommandBuffer:
                SetDebugMarkerName(
                    DebugReportObjectTypeEXT.CommandBufferExt,
                    (ulong)CommandBuffer.CommandBuffer.Handle,
                    string.Format("{0}_CommandBuffer", name));
                SetDebugMarkerName(
                    DebugReportObjectTypeEXT.CommandPoolExt,
                    CommandBuffer.CommandPool.Handle,
                    string.Format("{0}_CommandPool", name));
                break;
            case VkFramebuffer framebuffer:
                SetDebugMarkerName(
                    DebugReportObjectTypeEXT.FramebufferExt,
                    framebuffer.CurrentFramebuffer.Handle,
                    name);
                break;
            case VkSampler sampler:
                SetDebugMarkerName(DebugReportObjectTypeEXT.SamplerExt, sampler.DeviceSampler.Handle, name);
                break;
            case VkGraphicsProgram shaderProgram:
                foreach (ShaderModule module in shaderProgram.Modules.Values)
                {
                    SetDebugMarkerName(DebugReportObjectTypeEXT.ShaderModuleExt, module.Handle, name);
                }
                break;
            case VkComputeProgram computeProgram:
                SetDebugMarkerName(DebugReportObjectTypeEXT.PipelineExt, computeProgram.DevicePipeline.Handle, name);
                break;
            case VkTexture tex:
                SetDebugMarkerName(DebugReportObjectTypeEXT.ImageExt, tex.OptimalDeviceImage.Handle, name);
                break;
            case VkTextureView texView:
                SetDebugMarkerName(DebugReportObjectTypeEXT.ImageViewExt, texView.ImageView.Handle, name);
                break;
            case VkFence fence:
                SetDebugMarkerName(DebugReportObjectTypeEXT.FenceExt, fence.DeviceFence.Handle, name);
                break;
            case VkSwapchain sc:
                SetDebugMarkerName(DebugReportObjectTypeEXT.SwapchainKhrExt, sc.DeviceSwapchain.Handle, name);
                break;
            default:
                break;
        }
    }

    private void SetDebugMarkerName(DebugReportObjectTypeEXT type, ulong target, string name)
    {
        Debug.Assert(_setObjectNameDelegate != null);

        DebugMarkerObjectNameInfoEXT nameInfo = new(sType: StructureType.DebugMarkerObjectNameInfoExt);
        nameInfo.ObjectType = type;
        nameInfo.Object = target;

        byte* utf8Ptr = stackalloc byte[Utf8Stack.ByteCount(name)];
        Utf8Stack.Write(name, utf8Ptr);

        nameInfo.PObjectName = utf8Ptr;
        _setObjectNameDelegate(Device, &nameInfo).CheckResult();
    }

    private void LoadDebugMarkerFunctions()
    {
        _setObjectNameDelegate = Marshal.GetDelegateForFunctionPointer<vkDebugMarkerSetObjectNameEXT_t>(
            GetInstanceProcAddr("vkDebugMarkerSetObjectNameEXT"));
        MarkerBegin = Marshal.GetDelegateForFunctionPointer<vkCmdDebugMarkerBeginEXT_t>(
            GetInstanceProcAddr("vkCmdDebugMarkerBeginEXT"));
        MarkerEnd = Marshal.GetDelegateForFunctionPointer<vkCmdDebugMarkerEndEXT_t>(
            GetInstanceProcAddr("vkCmdDebugMarkerEndEXT"));
        MarkerInsert = Marshal.GetDelegateForFunctionPointer<vkCmdDebugMarkerInsertEXT_t>(
            GetInstanceProcAddr("vkCmdDebugMarkerInsertEXT"));
    }
}
