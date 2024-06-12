using System;
using Veldrid;
using Veldrid.SPIRV;
using Veldrid.StartupUtilities;
using Vector2 = System.Numerics.Vector2;

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
        private static Veldrid.Shader[] _shaders;
        private static Pipeline _pipeline;
        
        private static ResourceSet _textureSet;

        private const string VertexCode = @"
        #version 450

layout(location = 0) in vec2 Position;
layout(location = 1) in vec2 Uv;

layout(location = 0) out vec2 fsin_Uv;

void main()
{
    gl_Position = vec4(Position, 0, 1);
    fsin_Uv = Uv;
}";

        private const string FragmentCode = @"
        #version 450

layout(location = 0) in vec2 fsin_Uv;

layout(location = 0) out vec4 fsout_Color;

layout(set = 0, binding = 0) uniform texture2D SurfaceTexture;
layout(set = 0, binding = 1) uniform sampler SurfaceSampler;

void main()
{
    fsout_Color = texture(sampler2D(SurfaceTexture, SurfaceSampler), fsin_Uv);
}";

        public static void Initialize(bool VSync = true, GraphicsBackend preferredBackend = GraphicsBackend.OpenGL)
        {
            GraphicsDeviceOptions deviceOptions = new()
            {
                SyncToVerticalBlank = VSync,
                PreferStandardClipSpaceYDirection = true,
                PreferDepthRangeZeroToOne = true,
                ResourceBindingModel = ResourceBindingModel.Default,
                HasMainSwapchain = true,
            };

            Device = VeldridStartup.CreateGraphicsDevice(Screen.InternalWindow, deviceOptions, preferredBackend);
        }


        private static void EnsureResources(Texture2D tex)
        {
            if (createdResources)
                return;

            createdResources = true;

            ResourceFactory factory = Device.ResourceFactory;

            VertexLayoutDescription positionLayout = new VertexLayoutDescription(
                new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3));

            VertexLayoutDescription uvLayout = new VertexLayoutDescription(
                new VertexElementDescription("Uv", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2));

            ResourceLayout worldTextureLayout = factory.CreateResourceLayout(
                new ResourceLayoutDescription(
                    new ResourceLayoutElementDescription("SurfaceTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                    new ResourceLayoutElementDescription("SurfaceSampler", ResourceKind.Sampler, ShaderStages.Fragment)));

            ShaderDescription vertexShaderDesc = new ShaderDescription(
                ShaderStages.Vertex,
                System.Text.Encoding.UTF8.GetBytes(VertexCode),
                "main");
            ShaderDescription fragmentShaderDesc = new ShaderDescription(
                ShaderStages.Fragment,
                System.Text.Encoding.UTF8.GetBytes(FragmentCode),
                "main");

            _shaders = factory.CreateFromSpirv(vertexShaderDesc, fragmentShaderDesc);

            GraphicsPipelineDescription pipelineDescription = new GraphicsPipelineDescription
            {
                BlendState = BlendStateDescription.SingleOverrideBlend,

                DepthStencilState = new DepthStencilStateDescription(
                    depthTestEnabled: true,
                    depthWriteEnabled: true,
                    comparisonKind: ComparisonKind.LessEqual
                ),

                RasterizerState = new RasterizerStateDescription(
                    cullMode: FaceCullMode.Back,
                    fillMode: PolygonFillMode.Solid,
                    frontFace: FrontFace.Clockwise,
                    depthClipEnabled: true,
                    scissorTestEnabled: false
                ),

                PrimitiveTopology = PrimitiveTopology.TriangleList,

                ShaderSet = new ShaderSetDescription(
                    vertexLayouts: [ positionLayout, uvLayout ],
                    shaders: _shaders
                ),

                Outputs = Device.SwapchainFramebuffer.OutputDescription,
                ResourceLayouts = [ worldTextureLayout ],
            };

            _pipeline = factory.CreateGraphicsPipeline(pipelineDescription);

            _commandList = factory.CreateCommandList();

            _textureSet = factory.CreateResourceSet(new ResourceSetDescription(
                worldTextureLayout,
                tex.TextureView,
                TextureSampler.Aniso4x.InternalSampler));

            Console.WriteLine("Initialized resources");
        }


        public static void StartFrame(Texture2D quadTex)
        {
            RenderTexture.UpdatePool();

            EnsureResources(quadTex);

            if (_commandList == null)
                return;

            _commandList.Begin();
            _commandList.SetFramebuffer(Device.SwapchainFramebuffer);
            _commandList.ClearColorTarget(0, RgbaFloat.Black);
        }

        public static void EndFrame()
        {   
            if (_commandList == null)
                return;

            _commandList.End();
            Device.SubmitCommands(_commandList);
            Device.SwapBuffers();
        }

        public static void DrawNDCQuad(Mesh mesh)
        {
            if (_commandList == null)
                return;

            _commandList.SetPipeline(_pipeline);

            mesh.Upload();

            _commandList.SetVertexBuffer(0, mesh.VertexBuffer, 0);
            _commandList.SetVertexBuffer(1, mesh.VertexBuffer, (uint)mesh.UVStart);

            _commandList.SetIndexBuffer(mesh.IndexBuffer, mesh.IndexFormat);

            _commandList.SetGraphicsResourceSet(0, _textureSet);
            _commandList.DrawIndexed(
                indexCount: (uint)mesh.IndexCount,
                instanceCount: 1,
                indexStart: 0,
                vertexOffset: 0,
                instanceStart: 0);
        }

        internal static void Dispose()
        {
            _pipeline.Dispose();
            _shaders[0].Dispose();
            _shaders[1].Dispose();
            _commandList.Dispose();

            _textureSet.Dispose();

            Device.Dispose();
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
        }
    }
}
