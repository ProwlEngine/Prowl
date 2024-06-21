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
                PreferDepthRangeZeroToOne = true,
                ResourceBindingModel = ResourceBindingModel.Default,
                HasMainSwapchain = true,
                SwapchainDepthFormat = PixelFormat.R16_UNorm,
                SwapchainSrgbFormat = true,
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
            if (!frameBegan)
                throw new Exception("GetCommandList was called before StartFrame or after EndFrame. This is not allowed.");

            CommandList list = Factory.CreateCommandList();

            list.Begin();

            return list;
        }


        public static void SubmitCommands(CommandList list)
        {   
            if (!frameBegan)
                throw new Exception("SubmitCommands was called before StartFrame or after EndFrame. This is not allowed.");

            list.End();

            Device.SubmitCommands(list);
        }

        public static void EndFrame()
        {   
            frameBegan = false;
            Device.SwapBuffers();
        }

        public static Veldrid.Shader[] CreateFromSpirv(string vert, string frag)
        {
            ShaderDescription vertexShaderDesc = new ShaderDescription(ShaderStages.Vertex, Encoding.UTF8.GetBytes(vert), "main");
            ShaderDescription fragmentShaderDesc = new ShaderDescription(ShaderStages.Fragment, Encoding.UTF8.GetBytes(frag), "main");

            return Factory.CreateFromSpirv(vertexShaderDesc, fragmentShaderDesc);
        }

        /*
        public static void DrawMesh(Mesh mesh, Material material, Matrix4x4 matrix)
        {
            mesh.Upload();
            Matrix4x4 proj = Matrix4x4.CreatePerspectiveFieldOfView(fov, (float)Screen.Size.x / Screen.Size.y, 0.5f, 1000f);
            Matrix4x4 view = trs.worldToLocalMatrix;

            Matrix4x4 MVP = matrix * view * proj;

            material.SetMatrix("MVPMatrix", MVP);

            material.Upload(_commandList);

            BindMeshBuffers(_commandList, mesh, material.GetPass().GetVariant(material.Keywords).vertexInputs);

            _commandList.DrawIndexed(
                indexCount: (uint)mesh.IndexCount,
                instanceCount: 1,
                indexStart: 0,
                vertexOffset: 0,
                instanceStart: 0);
        }
        */

        public static void BindMeshBuffers(CommandList commandList, Mesh mesh, Material material, KeywordState? keywords = null)
        {
            commandList.SetIndexBuffer(mesh.IndexBuffer, mesh.IndexFormat);
            var vertexInputs = material.GetPass().GetVariant(keywords).vertexInputs;

            for (uint i = 0; i < vertexInputs.Count; i++)
            {
                MeshResource resource = vertexInputs[(int)i].Item1;

                switch (resource)
                {
                    case MeshResource.Position:     commandList.SetVertexBuffer(i, mesh.VertexBuffer, 0);                           break;
                    case MeshResource.UV0:          commandList.SetVertexBuffer(i, mesh.VertexBuffer, (uint)mesh.UVStart);          break;
                    case MeshResource.UV1:          commandList.SetVertexBuffer(i, mesh.VertexBuffer, (uint)mesh.UV2Start);         break;
                    case MeshResource.Normals:      commandList.SetVertexBuffer(i, mesh.VertexBuffer, (uint)mesh.NormalsStart);     break;
                    case MeshResource.Tangents:     commandList.SetVertexBuffer(i, mesh.VertexBuffer, (uint)mesh.TangentsStart);    break;
                    case MeshResource.Colors:       commandList.SetVertexBuffer(i, mesh.VertexBuffer, (uint)mesh.ColorsStart);      break;
                    case MeshResource.BoneIndices:  commandList.SetVertexBuffer(i, mesh.VertexBuffer, (uint)mesh.BoneIndexStart);   break;
                    case MeshResource.BoneWeights:  commandList.SetVertexBuffer(i, mesh.VertexBuffer, (uint)mesh.BoneWeightStart);  break;
                };
            }
        }

        internal static void Dispose()
        {
            Device.Dispose();
            ResourceCache.Dispose();
        }

        public static void CopyTexture(Texture source, Texture destination, bool waitForOperationCompletion = false)
        {
            InternalCopyTexture(source.InternalTexture, destination.InternalTexture, waitForOperationCompletion);
        }

        public static void CopyTexture(Texture source, Texture destination, uint mipLevel, uint arrayLayer, bool waitForOperationCompletion = false)
        {
            InternalCopyTexture(source.InternalTexture, destination.InternalTexture, mipLevel, arrayLayer, waitForOperationCompletion);
        }

        internal static void InternalCopyTexture(Veldrid.Texture source, Veldrid.Texture destination, bool waitForOperationCompletion = false)
        {
            Fence fence = Factory.CreateFence(false);
            CommandList commandList = Factory.CreateCommandList();

            commandList.Begin();
            commandList.CopyTexture(source, destination);
            commandList.End();

            Device.SubmitCommands(commandList, fence);

            if (waitForOperationCompletion)
                Device.WaitForFence(fence);
            fence.Dispose();
        }

        internal static void InternalCopyTexture(Veldrid.Texture source, Veldrid.Texture destination, uint mipLevel, uint arrayLayer, bool waitForOperationCompletion = false)
        {
            Fence fence = Factory.CreateFence(false);
            CommandList commandList = Factory.CreateCommandList();

            commandList.Begin();
            commandList.CopyTexture(source, destination, mipLevel, arrayLayer);
            commandList.End();

            Device.SubmitCommands(commandList, fence);

            if (waitForOperationCompletion)
                Device.WaitForFence(fence);
            fence.Dispose();
        }
    }
}
