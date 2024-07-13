using Prowl.Runtime.RenderPipelines;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Veldrid;
using Veldrid.StartupUtilities;



namespace Prowl.Runtime
{
    public static class Graphics
    {
        public static GraphicsDevice Device { get; internal set; }

        public static Framebuffer ScreenFramebuffer => Device.SwapchainFramebuffer;
        public static Vector2Int ScreenResolution => new Vector2(ScreenFramebuffer.Width, ScreenFramebuffer.Height);

        public static ResourceFactory Factory => Device.ResourceFactory;

        public static RenderPipelines.RenderPipeline ActivePipeline { get; private set; }

        public readonly static List<Renderable> Renderables = new();

        public static bool VSync
        {
            get { return Device.SyncToVerticalBlank; }
            set { Device.SyncToVerticalBlank = value; }
        }

        [DllImport("Shcore.dll")]
        internal static extern int SetProcessDpiAwareness(int value);

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
                    Debug.LogError("Failed to set DPI awareness", exception);
            }

            Screen.Resize += (newSize) => Device.ResizeMainWindow((uint)newSize.x, (uint)newSize.y);
        }

        private static void SetRenderPipeline(RenderPipelines.RenderPipeline renderPipeline)
        {
            if (ActivePipeline == renderPipeline)
                return;
            
            ActivePipeline?.ReleaseResources();
            ActivePipeline = renderPipeline;
            ActivePipeline?.InitializeResources();
        }

        public static void DrawRenderable(Renderable renderable)
        {
            Renderables.Add(renderable);
        }

        public static void StartFrame(RenderPipelines.RenderPipeline renderPipeline = null)
        {
            RenderTexture.UpdatePool();
            SetRenderPipeline(renderPipeline ?? Quality.GetQualitySettings().RenderPipeline.Res);
        }

        public static void Render(Camera[] cameras, Framebuffer targetFramebuffer)
        {
            if (ActivePipeline == null)
                return;
            
            RenderingContext context = new(Renderables, targetFramebuffer);

            ActivePipeline.Render(context, cameras);
        }

        public static void Render(Camera[] cameras, RenderingContext context)
        {
            if (ActivePipeline == null)
                return;

            ActivePipeline.Render(context, cameras);
        }

        public static void EndFrame()
        {   
            Device.SwapBuffers();
            Renderables.Clear();
        }

        public static CommandList GetCommandList()
        {
            CommandList list = Factory.CreateCommandList();

            list.Begin();

            return list;
        }

        public static void ExecuteCommandBuffer(CommandBuffer commandBuffer)
        {
            CommandList list = GetCommandList();

            RenderState state = new RenderState();

            foreach (var command in commandBuffer.Buffer)
                command.ExecuteCommand(list, state);

            try 
            {
                ExecuteCommandList(list, true);
            }
            catch (Exception ex)
            {
                Debug.LogError("Failed to execute command list", ex);
            }
            finally
            {
                state.Dispose();
                list.Dispose();   
            }
        }

        public static Task AsyncExecuteCommandBuffer(CommandBuffer commandBuffer)
        {
            return new Task(() => ExecuteCommandBuffer(commandBuffer));
        }

        public static void ExecuteCommandList(CommandList list, bool waitForCompletion = false)
        {   
            list.End();

            if (waitForCompletion)
            {
                Fence fence = Factory.CreateFence(false);
                Device.SubmitCommands(list, fence);
                Device.WaitForFence(fence);
                fence.Dispose();

                return;
            }

            Device.SubmitCommands(list);
        }

        public static Task AsyncExecuteCommandList(CommandList list)
        {
            return new Task(() => ExecuteCommandList(list, true));
        }

        public static SpecializationConstant[] GetSpecializations()
        {
            bool glOrGles = Device.BackendType == GraphicsBackend.OpenGL || Device.BackendType == GraphicsBackend.OpenGLES;

            PixelFormat swapchainFormat = ScreenFramebuffer.OutputDescription.ColorAttachments[0].Format;
            bool swapchainIsSrgb = swapchainFormat == PixelFormat.B8_G8_R8_A8_UNorm_SRgb
                || swapchainFormat == PixelFormat.R8_G8_B8_A8_UNorm_SRgb;

            SpecializationConstant[] specializations =
            [
                new SpecializationConstant(0, Device.IsClipSpaceYInverted),
                new SpecializationConstant(1, !swapchainIsSrgb),
                new SpecializationConstant(2, glOrGles),
                new SpecializationConstant(3, Device.IsDepthRangeZeroToOne),
            ];

            return specializations;
        }

        public static void CopyTexture(Texture source, Texture destination, bool waitForCompletion = false)
        {
            InternalCopyTexture(source.InternalTexture, destination.InternalTexture, waitForCompletion);
        }

        public static void CopyTexture(Texture source, Texture destination, uint mipLevel, uint arrayLayer, bool waitForCompletion = false)
        {
            InternalCopyTexture(source.InternalTexture, destination.InternalTexture, mipLevel, arrayLayer, waitForCompletion);
        }

        internal static void InternalCopyTexture(Veldrid.Texture source, Veldrid.Texture destination, bool waitForCompletion = false)
        {
            CommandList commandList = GetCommandList();

            commandList.CopyTexture(source, destination);
            
            ExecuteCommandList(commandList, waitForCompletion);

            commandList.Dispose();
        }

        internal static void InternalCopyTexture(Veldrid.Texture source, Veldrid.Texture destination, uint mipLevel, uint arrayLayer, bool waitForCompletion = false)
        {
            CommandList commandList = GetCommandList();

            commandList.CopyTexture(source, destination, mipLevel, arrayLayer);
            
            ExecuteCommandList(commandList, waitForCompletion);

            commandList.Dispose();
        }

        internal static void Dispose()
        {
            PipelineCache.Dispose();
            GUI.Graphics.UIDrawList.DisposeBuffers();
        
            Device.Dispose();
        }
    }
}
