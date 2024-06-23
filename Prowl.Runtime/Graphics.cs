using System;
using Veldrid;
using Veldrid.SPIRV;
using Veldrid.StartupUtilities;
using System.Text;
using System.Collections.Generic;

namespace Prowl.Runtime
{

    public static class Graphics
    {
        public static GraphicsDevice Device { get; internal set; }

        public static Framebuffer ScreenFramebuffer => Device.SwapchainFramebuffer;
        public static ResourceFactory Factory => Device.ResourceFactory;

        public static Vector2Int Resolution => new Vector2(ScreenFramebuffer.Width, ScreenFramebuffer.Height);

        private static bool frameBegan;

        public static bool VSync
        {
            get { return Device.SyncToVerticalBlank; }
            set { Device.SyncToVerticalBlank = value; }
        }

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

            Screen.Resize += (newSize) => Device.ResizeMainWindow((uint)newSize.x, (uint)newSize.y);
        }


        public static void StartFrame()
        {
            RenderTexture.UpdatePool();
            frameBegan = true;

            /*
            _commandList.Begin();
            _commandList.SetFramebuffer(ScreenFramebuffer);
            _commandList.ClearColorTarget(0, RgbaFloat.Black);
            _commandList.ClearDepthStencil(1f);
            */
        }

        public static CommandList GetCommandList()
        {
            CommandList list = Factory.CreateCommandList();

            list.Begin();

            return list;
        }


        public static void SubmitCommands(CommandList list, bool waitForCompletion = false)
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

        public static void EndFrame()
        {   
            frameBegan = false;
            Device.SwapBuffers();
        }


        public static SpecializationConstant[] GetSpecializations()
        {
            bool glOrGles = Device.BackendType == GraphicsBackend.OpenGL || Device.BackendType == GraphicsBackend.OpenGLES;

            List<SpecializationConstant> specializations =
            [
                new SpecializationConstant(100, Device.IsClipSpaceYInverted),
                new SpecializationConstant(101, glOrGles), // TextureCoordinatesInvertedY
                new SpecializationConstant(102, Device.IsDepthRangeZeroToOne),
            ];

            PixelFormat swapchainFormat = ScreenFramebuffer.OutputDescription.ColorAttachments[0].Format;
            bool swapchainIsSrgb = swapchainFormat == PixelFormat.B8_G8_R8_A8_UNorm_SRgb
                || swapchainFormat == PixelFormat.R8_G8_B8_A8_UNorm_SRgb;

            specializations.Add(new SpecializationConstant(103, swapchainIsSrgb));

            return specializations.ToArray();
        }

        public static Veldrid.Shader[] CreateFromSpirv(string vert, string frag)
        {
            CrossCompileOptions options = new()
            {
                FixClipSpaceZ = (Device.BackendType == GraphicsBackend.OpenGL || Device.BackendType == GraphicsBackend.OpenGLES) && !Device.IsDepthRangeZeroToOne,
                InvertVertexOutputY = false,
                Specializations = GetSpecializations()
            };

            ShaderDescription vertexShaderDesc = new ShaderDescription(ShaderStages.Vertex, Encoding.UTF8.GetBytes(vert), "main");
            ShaderDescription fragmentShaderDesc = new ShaderDescription(ShaderStages.Fragment, Encoding.UTF8.GetBytes(frag), "main");

            return Factory.CreateFromSpirv(vertexShaderDesc, fragmentShaderDesc, options);
        }

        internal static void Dispose()
        {
            Device.Dispose();
            ResourceCache.Dispose();
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
            
            SubmitCommands(commandList, waitForCompletion);

            commandList.Dispose();
        }

        internal static void InternalCopyTexture(Veldrid.Texture source, Veldrid.Texture destination, uint mipLevel, uint arrayLayer, bool waitForCompletion = false)
        {
            CommandList commandList = GetCommandList();

            commandList.CopyTexture(source, destination, mipLevel, arrayLayer);
            
            SubmitCommands(commandList, waitForCompletion);

            commandList.Dispose();
        }
    }
}
