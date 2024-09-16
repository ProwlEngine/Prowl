// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

using Veldrid;
using Veldrid.StartupUtilities;



namespace Prowl.Runtime;

public static partial class Graphics
{
    public static GraphicsDevice Device { get; internal set; }
    public static ResourceFactory Factory => Device.ResourceFactory;

    public static Framebuffer ScreenTarget => Device.SwapchainFramebuffer;

    public static Vector2Int TargetResolution => new Vector2(ScreenTarget.Width, ScreenTarget.Height);

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

        Screen.Resize += ResizeGraphicsResources;

        GUI.Graphics.UIDrawListRenderer.Initialize(Device.SwapchainFramebuffer.OutputDescription, GUI.Graphics.ColorSpaceHandling.Direct);
    }

    private static void ResizeGraphicsResources(Vector2Int newSize)
    {
        Device.ResizeMainWindow((uint)newSize.x, (uint)newSize.y);
    }

    public static void EndFrame()
    {
        Device.SwapBuffers();

        Device.WaitForIdle();

        RenderTexture.UpdatePool();
        RenderPipelines.RenderPipeline.ClearRenderables();
    }

    public static CommandList GetCommandList()
    {
        CommandList list = Factory.CreateCommandList();

        list.Begin();

        return list;
    }

    public static void SubmitCommandBuffer(CommandBuffer commandBuffer, bool awaitComplete = false, ulong timeout = ulong.MaxValue)
    {
        commandBuffer.Clear();

        try
        {
            if (awaitComplete)
            {
                Fence fence = Factory.CreateFence(false);
                Device.SubmitCommands(commandBuffer._commandList, fence);
                Device.WaitForFence(fence, timeout);
                fence.Dispose();

                return;
            }

            Device.SubmitCommands(commandBuffer._commandList);
        }
        catch (Exception ex)
        {
            Debug.LogException(new Exception("Failed to execute command list", ex));
        }
    }

    public static void SubmitCommandList(CommandList list, bool awaitComplete = false, ulong timeout = ulong.MaxValue)
    {
        list.End();

        if (awaitComplete)
        {
            Fence fence = Factory.CreateFence(false);
            Device.SubmitCommands(list, fence);
            Device.WaitForFence(fence, timeout);
            fence.Dispose();

            return;
        }

        Device.SubmitCommands(list);
    }

    internal static void InternalCopyTexture(Veldrid.Texture source, Veldrid.Texture destination, uint mipLevel, uint arrayLayer, bool awaitComplete = false)
    {
        CommandList commandList = GetCommandList();

        commandList.CopyTexture(source, destination, mipLevel, arrayLayer);

        SubmitCommandList(commandList, awaitComplete);

        commandList.Dispose();
    }

    internal static void Dispose()
    {
        ShaderPipelineCache.Dispose();
        GUI.Graphics.UIDrawListRenderer.Dispose();

        Device.Dispose();
    }
}
