using System;
using Veldrid;
using Veldrid.SPIRV;
using Veldrid.StartupUtilities;
using System.Text;

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
        private static Shader shader;

        private static DeviceBuffer _matrixBuffer;
        private static DeviceBuffer _colorBuffer;
        
        private static ResourceSet _matrixSet;
        private static ResourceSet _multiSet;

        private const string VertexCode = @"
#version 450

layout(set = 0, binding = 0) uniform MatrixBuffer
{
    mat4 Projection;
    mat4 View;
    mat4 World;
};

layout(set = 1, binding = 0) uniform texture2D SurfaceTexture;
layout(set = 1, binding = 1) uniform sampler SurfaceSampler;

layout(set = 1, binding = 2) uniform ColorBuffer
{
    vec4 ColorValue;
};

layout(location = 0) in vec3 Position;
layout(location = 1) in vec2 TexCoords;

layout(location = 0) out vec2 fsin_texCoords;

void main()
{
    vec4 worldPosition = World * vec4(Position, 1);

    float lat = acos(worldPosition.y / length(worldPosition)); //theta
    float lon = atan(worldPosition.x / worldPosition.z); // phi

    worldPosition *= texture(sampler2D(SurfaceTexture, SurfaceSampler), vec2(lat, lon));

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

layout(set = 1, binding = 0) uniform texture2D SurfaceTexture;
layout(set = 1, binding = 1) uniform sampler SurfaceSampler;

layout(set = 1, binding = 2) uniform ColorBuffer
{
    vec4 ColorValue;
};

void main()
{
    fsout_color = texture(sampler2D(SurfaceTexture, SurfaceSampler), fsin_texCoords) * ColorValue;
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

            _matrixBuffer = factory.CreateBuffer(new BufferDescription(64 * 3, BufferUsage.UniformBuffer));
            _colorBuffer = factory.CreateBuffer(new BufferDescription(sizeof(float) * 4, BufferUsage.UniformBuffer));

            ShaderDescription vertexShaderDesc = new ShaderDescription(ShaderStages.Vertex, Encoding.UTF8.GetBytes(VertexCode), "main");
            ShaderDescription fragmentShaderDesc = new ShaderDescription(ShaderStages.Fragment, Encoding.UTF8.GetBytes(FragmentCode), "main");

            // Pass creation info (Name, tags, compiled programs, etc...)
            Pass pass = new Pass("DrawCube", []);

            pass.CreateProgram(factory.CreateFromSpirv(vertexShaderDesc, fragmentShaderDesc));

            // The input channels the vertex shader expects
            pass.AddVertexInput("Position", VertexElementSemantic.Position, VertexElementFormat.Float3);
            pass.AddVertexInput("TexCoords", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2);

            // MVP matrix resources
            pass.AddResourceElement([ new ResourceLayoutElementDescription("MatrixBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex) ]);
            
            // Other shader resources
            pass.AddResourceElement([
                new ResourceLayoutElementDescription("SurfaceTexture", ResourceKind.TextureReadOnly, ShaderStages.Vertex | ShaderStages.Fragment),
                new ResourceLayoutElementDescription("SurfaceSampler", ResourceKind.Sampler, ShaderStages.Vertex | ShaderStages.Fragment),
                new ResourceLayoutElementDescription("ColorValue", ResourceKind.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment) ]);

            pass.cullMode = FaceCullMode.None;

            shader = new Shader();

            shader.AddPass(pass);

            _commandList = factory.CreateCommandList();

            PipelineCache.PipelineInfo pipeline = PipelineCache.GetPipelineForPass(shader.GetPass(0), fillMode: PolygonFillMode.Wireframe);

            _matrixSet = factory.CreateResourceSet(new ResourceSetDescription(
                pipeline.description.ResourceLayouts[0],
                _matrixBuffer));

            _multiSet = factory.CreateResourceSet(new ResourceSetDescription(
                pipeline.description.ResourceLayouts[1],
                tex.TextureView,
                tex.Sampler.InternalSampler,
                _colorBuffer));

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

            PipelineCache.PipelineInfo pipeline = PipelineCache.GetPipelineForPass(shader.GetPass(0), fillMode: PolygonFillMode.Wireframe);

            _commandList.SetPipeline(pipeline.pipeline);

            mesh.Upload();

            System.Numerics.Matrix4x4 proj = System.Numerics.Matrix4x4.CreatePerspectiveFieldOfView(1.0f, (float)Screen.Size.x / Screen.Size.y, 0.5f, 100f);
            System.Numerics.Matrix4x4 view = System.Numerics.Matrix4x4.CreateLookAt(new Vector3(0, -1.5, -3), Vector3.zero, Vector3.up);
            System.Numerics.Matrix4x4 world = System.Numerics.Matrix4x4.CreateWorld(Vector3.zero, Vector3.forward, Vector3.up);

            world *= System.Numerics.Matrix4x4.CreateFromAxisAngle(Vector3.up, (float)Time.time * 0.25f);

            _commandList.UpdateBuffer(_matrixBuffer, 0, [ proj, view, world ]);
            
            _commandList.UpdateBuffer(_colorBuffer, 0, new System.Numerics.Vector4((float)Math.Sin(Time.time), (float)Math.Cos(Time.time), 0.75f, 1.0f));

            _commandList.SetVertexBuffer(0, mesh.VertexBuffer, 0);
            _commandList.SetVertexBuffer(1, mesh.VertexBuffer, (uint)mesh.UVStart);

            _commandList.SetIndexBuffer(mesh.IndexBuffer, mesh.IndexFormat);

            _commandList.SetGraphicsResourceSet(0, _matrixSet);
            _commandList.SetGraphicsResourceSet(1, _multiSet);

            _commandList.DrawIndexed(
                indexCount: (uint)mesh.IndexCount,
                instanceCount: 1,
                indexStart: 0,
                vertexOffset: 0,
                instanceStart: 0);
        }

        internal static void Dispose()
        {
            _commandList.Dispose();

            _matrixSet.Dispose();
            _multiSet.Dispose();

            _matrixBuffer.Dispose();
            _colorBuffer.Dispose();

            Device.Dispose();
            PipelineCache.Dispose();
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
