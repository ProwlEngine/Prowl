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

        private static DeviceBuffer _projectionBuffer;
        private static DeviceBuffer _viewBuffer;
        private static DeviceBuffer _worldBuffer;
        
        private static ResourceSet _projViewSet;
        private static ResourceSet _worldTextureSet;

        private const string VertexCode = @"
#version 450

layout(set = 0, binding = 0) uniform ProjectionBuffer
{
    mat4 Projection;
};

layout(set = 0, binding = 1) uniform ViewBuffer
{
    mat4 View;
};

layout(set = 1, binding = 0) uniform WorldBuffer
{
    mat4 World;
};

layout(location = 0) in vec3 Position;
layout(location = 1) in vec2 TexCoords;
layout(location = 0) out vec2 fsin_texCoords;

void main()
{
    vec4 worldPosition = World * vec4(Position, 1);
    vec4 viewPosition = View * worldPosition;
    vec4 clipPosition = Projection * viewPosition;
    
    clipPosition.y *= -1.0;

    gl_Position = clipPosition;
    fsin_texCoords = TexCoords;
}";

        private const string FragmentCode = @"
#version 450

layout(location = 0) in vec2 fsin_texCoords;
layout(location = 0) out vec4 fsout_color;

layout(set = 1, binding = 1) uniform texture2D SurfaceTexture;
layout(set = 1, binding = 2) uniform sampler SurfaceSampler;

void main()
{
    fsout_color =  texture(sampler2D(SurfaceTexture, SurfaceSampler), fsin_texCoords);
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
                SwapchainDepthFormat = PixelFormat.R16_UNorm,
                SwapchainSrgbFormat = true,
            };

            Device = VeldridStartup.CreateGraphicsDevice(Screen.InternalWindow, deviceOptions, preferredBackend);
        }


        private static void EnsureResources(Texture2D tex)
        {
            if (createdResources)
                return;

            createdResources = true;

            ResourceFactory factory = Device.ResourceFactory;

            _projectionBuffer = factory.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer));
            _viewBuffer = factory.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer));
            _worldBuffer = factory.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer));

            VertexLayoutDescription positionLayout = new VertexLayoutDescription(
                new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3));

            VertexLayoutDescription uvLayout = new VertexLayoutDescription(
                new VertexElementDescription("TexCoords", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2));

            ResourceLayout projViewLayout = factory.CreateResourceLayout(
                new ResourceLayoutDescription(
                    new ResourceLayoutElementDescription("ProjectionBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex),
                    new ResourceLayoutElementDescription("ViewBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex)));

            ResourceLayout worldTextureLayout = factory.CreateResourceLayout(
                new ResourceLayoutDescription(
                    new ResourceLayoutElementDescription("WorldBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex),
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
                ResourceLayouts = [ projViewLayout, worldTextureLayout ],
            };

            _pipeline = factory.CreateGraphicsPipeline(pipelineDescription);

            _commandList = factory.CreateCommandList();

            _projViewSet = factory.CreateResourceSet(new ResourceSetDescription(
                projViewLayout,
                _projectionBuffer,
                _viewBuffer));

            _worldTextureSet = factory.CreateResourceSet(new ResourceSetDescription(
                worldTextureLayout,
                _worldBuffer,
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

        public static void DrawNDCQuad(Mesh mesh)
        {
            if (_commandList == null)
                return;

            _commandList.SetPipeline(_pipeline);

            mesh.Upload();

            _commandList.UpdateBuffer(_projectionBuffer, 0, System.Numerics.Matrix4x4.CreatePerspectiveFieldOfView(
                1.0f,
                (float)Screen.Size.x / Screen.Size.y,
                0.5f,
                100f));

            _commandList.UpdateBuffer(_viewBuffer, 0, System.Numerics.Matrix4x4.CreateLookAt(Vector3.forward, Vector3.zero, Vector3.up));
            

            System.Numerics.Matrix4x4 rotation = System.Numerics.Matrix4x4.CreateWorld(Vector3.zero, Vector3.forward, Vector3.up);

            _commandList.UpdateBuffer(_worldBuffer, 0, ref rotation);

            _commandList.SetVertexBuffer(0, mesh.VertexBuffer, 0);
            _commandList.SetVertexBuffer(1, mesh.VertexBuffer, (uint)mesh.UVStart);

            _commandList.SetIndexBuffer(mesh.IndexBuffer, mesh.IndexFormat);

            _commandList.SetGraphicsResourceSet(0, _projViewSet);
            _commandList.SetGraphicsResourceSet(1, _worldTextureSet);

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

            _projViewSet.Dispose();
            _worldTextureSet.Dispose();

            _projectionBuffer.Dispose();
            _viewBuffer.Dispose();
            _worldBuffer.Dispose();

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
