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

        public static Swapchain MainSwapchain => Device.MainSwapchain;
        public static Framebuffer Framebuffer => Device.SwapchainFramebuffer;
        public static ResourceFactory ResourceFactory => Device.ResourceFactory;

        public static bool VSync
        {
            get { return Device.SyncToVerticalBlank; }
            set { Device.SyncToVerticalBlank = value; }
        }

        // Veldrid quad stuff
        private static bool createdResources = false;

        private static CommandList _commandList;

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


        private static void EnsureResources()
        {
            if (createdResources)
                return;

            createdResources = true;

            _commandList = ResourceFactory.CreateCommandList();

            Console.WriteLine("Initialized resources");
        }


        public static void StartFrame()
        {
            RenderTexture.UpdatePool();

            EnsureResources();

            if (_commandList == null)
                return;

            _commandList.Begin();
            _commandList.SetFramebuffer(Framebuffer);
            _commandList.ClearColorTarget(0, RgbaFloat.Black);
            _commandList.ClearDepthStencil(1f);
        }

        public static void EndFrame()
        {   
            if (_commandList == null)
                return;

            _commandList.End();
            Device.SubmitCommands(_commandList);
            Device.SwapBuffers();
        }

        public static Veldrid.Shader[] CreateFromSpirv(string vert, string frag)
        {
            ShaderDescription vertexShaderDesc = new ShaderDescription(ShaderStages.Vertex, Encoding.UTF8.GetBytes(vert), "main");
            ShaderDescription fragmentShaderDesc = new ShaderDescription(ShaderStages.Fragment, Encoding.UTF8.GetBytes(frag), "main");

            return ResourceFactory.CreateFromSpirv(vertexShaderDesc, fragmentShaderDesc);
        }

        public static void DrawMesh(Mesh mesh, Material material, Matrix4x4 matrix, int pass = 0, PolygonFillMode fill = PolygonFillMode.Solid)
        {
            mesh.Upload();
            Matrix4x4 proj = Matrix4x4.CreatePerspectiveFieldOfView(1.0f, (float)Screen.Size.x / Screen.Size.y, 0.5f, 100f);
            Matrix4x4 view = Matrix4x4.CreateLookAt(new Vector3(0, -1.5, -3), Vector3.zero, Vector3.up);

            material.SetMatrix("ProjectionMatrix", proj);
            material.SetMatrix("ViewMatrix", view);
            material.SetMatrix("WorldMatrix", matrix);

            material.SetPass(_commandList, true, pass, fill);
            BindMeshBuffers(_commandList, mesh, material.Shader.Res.GetPass(pass).GetVariant(material.Keywords).vertexInputs);

            _commandList.DrawIndexed(
                indexCount: (uint)mesh.IndexCount,
                instanceCount: 1,
                indexStart: 0,
                vertexOffset: 0,
                instanceStart: 0);
        }

        private static void BindMeshBuffers(CommandList commandList, Mesh mesh, List<MeshResource> vertexInputs)
        {
            commandList.SetIndexBuffer(mesh.IndexBuffer, mesh.IndexFormat);

            for (uint i = 0; i < vertexInputs.Count; i++)
            {
                MeshResource resource = vertexInputs[(int)i];

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
            _commandList.Dispose();

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
            Fence fence = ResourceFactory.CreateFence(false);
            CommandList commandList = ResourceFactory.CreateCommandList();

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
            Fence fence = ResourceFactory.CreateFence(false);
            CommandList commandList = ResourceFactory.CreateCommandList();

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
