// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Veldrid;
using Veldrid.StartupUtilities;

using Prowl.Runtime.Rendering;
using Prowl.Runtime.Rendering.Pipelines;
using System.Linq;

namespace Prowl.Runtime;

public static partial class Graphics
{
    public static GraphicsDevice Device { get; internal set; }
    public static ResourceFactory Factory => Device.ResourceFactory;

    public static Framebuffer ScreenTarget => Device.SwapchainFramebuffer;

    public static Vector2Int TargetResolution => new Vector2(ScreenTarget.Width, ScreenTarget.Height);

    public static bool IsDX11 => Device.BackendType == GraphicsBackend.Direct3D11;
    public static bool IsOpenGL => Device.BackendType == GraphicsBackend.OpenGL || Device.BackendType == GraphicsBackend.OpenGLES;
    public static bool IsVulkan => Device.BackendType == GraphicsBackend.Vulkan;

    private static readonly List<IDisposable> s_disposables = new();

    public static bool VSync
    {
        get { return Device.SyncToVerticalBlank; }
        set { Device.SyncToVerticalBlank = value; }
    }

    [LibraryImport("Shcore.dll")]
    internal static partial int SetProcessDpiAwareness(int value);

    public static void Initialize(bool VSync = true, GraphicsBackend preferredBackend = GraphicsBackend.OpenGL)
    {
        GraphicsDeviceOptions deviceOptions = new()
        {
            SyncToVerticalBlank = VSync,
            ResourceBindingModel = ResourceBindingModel.Default,
            HasMainSwapchain = true,
            SwapchainDepthFormat = PixelFormat.D24_UNorm_S8_UInt,
            SwapchainSrgbFormat = false,
        };

        Device = VeldridStartup.CreateGraphicsDevice(Screen.InternalWindow, deviceOptions, preferredBackend);

        if (RuntimeUtils.IsWindows())
        {
            Exception? exception = Marshal.GetExceptionForHR(SetProcessDpiAwareness(1));

            if (exception != null)
                Debug.LogException(new Exception("Failed to set DPI awareness", exception));
        }

        GUI.Graphics.UIDrawListRenderer.Initialize(GUI.Graphics.ColorSpaceHandling.Direct);
    }

    public static void EndFrame()
    {
        Device.SwapBuffers();

        Device.WaitForIdle();

        RenderTexture.UpdatePool();
        RenderPipeline.ClearRenderables();

        foreach (IDisposable disposable in s_disposables)
            disposable.Dispose();

        s_disposables.Clear();

        if (Device.SwapchainFramebuffer.Width != Screen.Width || Device.SwapchainFramebuffer.Height != Screen.Height)
            Device.ResizeMainWindow((uint)Screen.Width, (uint)Screen.Height);
    }

    public static CommandList GetCommandList()
    {
        CommandList list = Factory.CreateCommandList();
        list.Begin();

        return list;
    }

    public static void SubmitCommandBuffer(CommandBuffer commandBuffer, GraphicsFence? fence = null)
    {
        commandBuffer.Clear();
        Device.SubmitCommands(commandBuffer._commandList, fence?.Fence);
    }

    public static void SubmitCommandList(CommandList list, GraphicsFence? fence = null)
    {
        list.End();
        Device.SubmitCommands(list, fence?.Fence);
    }

    public static void WaitForFence(GraphicsFence fence, ulong timeout = ulong.MaxValue)
    {
        Device.WaitForFence(fence?.Fence, timeout);
    }

    public static void WaitForFences(GraphicsFence[] fences, bool waitAll, ulong timeout = ulong.MaxValue)
    {
        Device.WaitForFences(fences.Select(x => x.Fence).ToArray(), waitAll, timeout);
    }

    internal static void SubmitResourcesForDisposal(IEnumerable<IDisposable> resources)
    {
        s_disposables.AddRange(resources);
    }

    internal static void Dispose()
    {
        ShaderPipelineCache.Dispose();
        GUI.Graphics.UIDrawListRenderer.Dispose();

        Device.Dispose();
    }

    public static Matrix4x4 GetGPUProjectionMatrix(Matrix4x4 projection)
    {
        // On DX11, flip Y - not sure if this works on Metal.
        if (!IsOpenGL && !IsVulkan)
            projection.M22 = -projection.M22;

        return projection;
    }

    public static FrontFace GetFrontFace()
    {
        if (IsOpenGL)
            return FrontFace.CounterClockwise;

        return FrontFace.Clockwise;
    }
}
