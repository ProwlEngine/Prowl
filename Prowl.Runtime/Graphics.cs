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
        private static DeviceBuffer _vertexBuffer;
        private static DeviceBuffer _indexBuffer;
        private static Veldrid.Shader[] _shaders;
        private static Pipeline _pipeline;

        private static TextureView _textureView;
        private static Sampler _textureSampler;
        private static ResourceSet _textureSet;

        private const string VertexCode = @"
        #version 450

layout(location = 0) in vec2 Position;
layout(location = 1) in vec4 Color;
layout(location = 2) in vec2 Uv;

layout(location = 0) out vec4 fsin_Color;
layout(location = 1) out vec2 fsin_Uv;

void main()
{
    gl_Position = vec4(Position, 0, 1);
    fsin_Color = Color;
    fsin_Uv = Uv;
}";

        private const string FragmentCode = @"
        #version 450

layout(location = 0) in vec4 fsin_Color;
layout(location = 1) in vec2 fsin_Uv;
layout(location = 0) out vec4 fsout_Color;

layout(set = 0, binding = 0) uniform texture2D SurfaceTexture;
layout(set = 0, binding = 1) uniform sampler SurfaceSampler;

void main()
{
    fsout_Color = texture(sampler2D(SurfaceTexture, SurfaceSampler), fsin_Uv);
}";
        unsafe struct VertexPositionColor
        {
            public System.Numerics.Vector2 Position; // This is the position, in normalized device coordinates.
            public RgbaFloat Color; // This is the color of the vertex.
            public System.Numerics.Vector2 UV;

            public VertexPositionColor(Vector2 position, RgbaFloat color, Vector2 uv)
            {
                Position = position;
                Color = color;
                UV = uv;
            }

            public static readonly uint SizeInBytes = (uint)sizeof(VertexPositionColor);
        }

        static VertexPositionColor[] quadVertices =
        {
            new VertexPositionColor(new Vector2(-0.75f, 0.75f), RgbaFloat.Red, new Vector2(0, 1)),
            new VertexPositionColor(new Vector2(0.75f, 0.75f), RgbaFloat.Green, new Vector2(1, 1)),
            new VertexPositionColor(new Vector2(-0.75f, -0.75f), RgbaFloat.Blue, new Vector2(0, 0)),
            new VertexPositionColor(new Vector2(0.75f, -0.75f), RgbaFloat.Yellow, new Vector2(1, 0))
        };

        private static ushort[] quadIndices = { 0, 1, 2, 3 };
        // End veldrid quad stuff

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

            _vertexBuffer = factory.CreateBuffer(new BufferDescription(4 * VertexPositionColor.SizeInBytes, BufferUsage.VertexBuffer));
            _indexBuffer = factory.CreateBuffer(new BufferDescription(4 * sizeof(ushort), BufferUsage.IndexBuffer));
            _textureView = factory.CreateTextureView(tex.InternalTexture);

            Device.UpdateBuffer(_vertexBuffer, 0, quadVertices);
            Device.UpdateBuffer(_indexBuffer, 0, quadIndices);

            VertexLayoutDescription vertexLayout = new VertexLayoutDescription(
                new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
                new VertexElementDescription("Color", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4),
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

            GraphicsPipelineDescription pipelineDescription = new GraphicsPipelineDescription();
            pipelineDescription.BlendState = BlendStateDescription.SingleOverrideBlend;

            pipelineDescription.DepthStencilState = new DepthStencilStateDescription(
                depthTestEnabled: true,
                depthWriteEnabled: true,
                comparisonKind: ComparisonKind.LessEqual);

            pipelineDescription.RasterizerState = new RasterizerStateDescription(
                cullMode: FaceCullMode.Back,
                fillMode: PolygonFillMode.Solid,
                frontFace: FrontFace.Clockwise,
                depthClipEnabled: true,
                scissorTestEnabled: false);

            pipelineDescription.PrimitiveTopology = PrimitiveTopology.TriangleStrip;
            pipelineDescription.ResourceLayouts = System.Array.Empty<ResourceLayout>();

            pipelineDescription.ShaderSet = new ShaderSetDescription(
                vertexLayouts: new VertexLayoutDescription[] { vertexLayout },
                shaders: _shaders);

            pipelineDescription.Outputs = Device.SwapchainFramebuffer.OutputDescription;

            pipelineDescription.ResourceLayouts = [ worldTextureLayout ];

            _pipeline = factory.CreateGraphicsPipeline(pipelineDescription);

            _commandList = factory.CreateCommandList();

            SamplerDescription desc = new();
            desc.Filter = SamplerFilter.MinLinear_MagLinear_MipPoint;
            desc.AddressModeU = SamplerAddressMode.Wrap;
            desc.AddressModeV = SamplerAddressMode.Wrap;

            _textureSampler = factory.CreateSampler(desc);

            _textureSet = factory.CreateResourceSet(new ResourceSetDescription(
                worldTextureLayout,
                _textureView,
                _textureSampler));

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

        public static void DrawNDCQuad()
        {
            if (_commandList == null)
                return;

            _commandList.SetPipeline(_pipeline);

            _commandList.SetVertexBuffer(0, _vertexBuffer);
            _commandList.SetIndexBuffer(_indexBuffer, Veldrid.IndexFormat.UInt16);
            _commandList.SetGraphicsResourceSet(0, _textureSet);
            _commandList.DrawIndexed(
                indexCount: 4,
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
            _vertexBuffer.Dispose();
            _indexBuffer.Dispose();

            _textureView.Dispose();
            _textureSet.Dispose();
            _textureSampler.Dispose();

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
