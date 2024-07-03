using Veldrid;
using Veldrid.StartupUtilities;
using System.Collections.Generic;


namespace Prowl.Runtime
{   
    using RenderPipelines;

    public static class Graphics
    {
        public static GraphicsDevice Device { get; internal set; }

        public static Framebuffer ScreenFramebuffer => Device.SwapchainFramebuffer;
        public static Vector2Int ScreenResolution => new Vector2(ScreenFramebuffer.Width, ScreenFramebuffer.Height);

        public static ResourceFactory Factory => Device.ResourceFactory;

        public static RenderPipeline ActivePipeline { get; private set; }

        public static bool VSync
        {
            get { return Device.SyncToVerticalBlank; }
            set { Device.SyncToVerticalBlank = value; }
        }

        [System.Runtime.InteropServices.DllImport("Shcore.dll")]
        internal static extern int SetProcessDpiAwareness(int value);

        public static void Initialize(bool VSync = true, GraphicsBackend preferredBackend = GraphicsBackend.OpenGL)
        {
            GraphicsDeviceOptions deviceOptions = new()
            {
                SyncToVerticalBlank = VSync,
                PreferStandardClipSpaceYDirection = true,
                PreferDepthRangeZeroToOne = false,
                ResourceBindingModel = ResourceBindingModel.Default,
                HasMainSwapchain = true,
                SwapchainDepthFormat = PixelFormat.R16_UNorm,
                SwapchainSrgbFormat = false,
            };

            Device = VeldridStartup.CreateGraphicsDevice(Screen.InternalWindow, deviceOptions, preferredBackend);

            if(RuntimeUtils.IsWindows())
                SetProcessDpiAwareness(1);

            Screen.Resize += (newSize) => Device.ResizeMainWindow((uint)newSize.x, (uint)newSize.y);
        }

        private static void SetRenderPipeline(RenderPipeline renderPipeline)
        {
            if (ActivePipeline == renderPipeline)
                return;
            
            ActivePipeline?.ReleaseResources();
            ActivePipeline = renderPipeline;
            ActivePipeline?.InitializeResources();
        }

        public static void StartFrame(RenderPipeline renderPipeline = null)
        {
            RenderTexture.UpdatePool();
            SetRenderPipeline(renderPipeline ?? Quality.GetQualitySettings().RenderPipeline.Res);
        }

        public static void Render(Camera[] cameras, Framebuffer targetFramebuffer)
        {
            if (ActivePipeline == null)
                return;
            
            RenderingContext context = new()
            {
                TargetFramebuffer = targetFramebuffer
            };

            ActivePipeline.Render(context, cameras);
        }

        public static void EndFrame()
        {   
            Device.SwapBuffers();
        }

        public static CommandList GetCommandList()
        {
            CommandList list = Factory.CreateCommandList();

            list.Begin();

            return list;
        }

        public static void ExecuteCommandBuffer(CommandBuffer commandBuffer, bool waitForCompletion = false)
        {
            CommandList list = GetCommandList();

            RenderState state = new RenderState();

            foreach (var command in commandBuffer.Buffer)
                command.ExecuteCommand(list, ref state);

            ExecuteCommandList(list, waitForCompletion);

            state.Clear();
            list.Dispose();
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
            }
            else
            {
                Device.SubmitCommands(list);
            }
        }

        public static SpecializationConstant[] GetSpecializations()
        {
            bool glOrGles = Device.BackendType == GraphicsBackend.OpenGL || Device.BackendType == GraphicsBackend.OpenGLES;

            List<SpecializationConstant> specializations =
            [
                new SpecializationConstant(0, Device.IsClipSpaceYInverted),
                new SpecializationConstant(1, true),
                new SpecializationConstant(2, glOrGles),
                new SpecializationConstant(3, Device.IsDepthRangeZeroToOne),
            ];

            PixelFormat swapchainFormat = ScreenFramebuffer.OutputDescription.ColorAttachments[0].Format;
            bool swapchainIsSrgb = swapchainFormat == PixelFormat.B8_G8_R8_A8_UNorm_SRgb
                || swapchainFormat == PixelFormat.R8_G8_B8_A8_UNorm_SRgb;

            specializations.Add(new SpecializationConstant(103, swapchainIsSrgb));

            return specializations.ToArray();
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
            Device.Dispose();

            PipelineCache.Dispose();
            ShaderCache.Dispose();
        }
    }
}
