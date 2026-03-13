using System.Numerics;
using Veldrid;
using SampleBase;
using System.Runtime.CompilerServices;
using System.IO;
using System;
using Common;
using System.Runtime.InteropServices;
using AssetPrimitives;
using System.Diagnostics;
using Veldrid.SPIRV;

// This example has been adapted from Sascha Willem's "instancing" example from https://github.com/SaschaWillems/Vulkan.

namespace Instancing
{
    public class InstancingApplication : SampleApplication
    {
        private CommandList _commandList;

        // Shared resources
        private ResourceSet _sharedResourceSet;
        private DeviceBuffer _cameraProjViewBuffer;
        private DeviceBuffer _lightInfoBuffer;

        // Resources for instanced rocks
        private Pipeline _instancePipeline;
        private uint _instanceCount;
        private DeviceBuffer _instanceVB;
        private ResourceSet _instanceTextureSet;
        private ModelResources _rockModel;

        // Resources for central planet
        private Pipeline _planetPipeline;
        private ResourceSet _planetTextureSet;
        private ModelResources _planetModel;

        // Resources for the background starfield
        private Pipeline _starfieldPipeline;
        private DeviceBuffer _viewInfoBuffer;
        private ResourceSet _viewInfoSet;

        // Dynamic data
        private Vector3 _lightDir;
        private bool _lightFromCamera = false; // Press F1 to switch where the directional light originates
        private DeviceBuffer _rotationInfoBuffer; // Contains the local and global rotation values.
        private float _localRotation = 0f; // Causes individual rocks to rotate around their centers
        private float _globalRotation = 0f; // Causes rocks to rotate around the global origin (where the planet is)

        public InstancingApplication(ApplicationWindow window) : base(window) { }

        protected override void CreateResources(ResourceFactory factory)
        {
            _instanceCount = 8000u;

            _camera.Position = new Vector3(-36f, 20f, 100f);
            _camera.Pitch = -0.3f;
            _camera.Yaw = 0.1f;

            _cameraProjViewBuffer = factory.CreateBuffer(
                new BufferDescription((uint)(Unsafe.SizeOf<Matrix4x4>() * 2), BufferUsage.UniformBuffer | BufferUsage.Dynamic));
            _lightInfoBuffer = factory.CreateBuffer(new BufferDescription(32, BufferUsage.UniformBuffer | BufferUsage.Dynamic));
            _rotationInfoBuffer = factory.CreateBuffer(new BufferDescription(16, BufferUsage.UniformBuffer | BufferUsage.Dynamic));
            _lightDir = Vector3.Normalize(new Vector3(0.3f, -0.75f, -0.3f));

            VertexLayoutDescription sharedVertexLayout = new VertexLayoutDescription(
                new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
                new VertexElementDescription("Normal", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
                new VertexElementDescription("TexCoord", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2));

            bool etc2Supported = GraphicsDevice.GetPixelFormatSupport(
                PixelFormat.ETC2_R8_G8_B8_UNorm,
                TextureType.Texture2D,
                TextureUsage.Sampled);
            PixelFormat pixelFormat = etc2Supported ? PixelFormat.ETC2_R8_G8_B8_UNorm : PixelFormat.BC3_UNorm;

            byte[] rockTextureData = LoadEmbeddedAsset<byte[]>(
                etc2Supported
                    ? "texturearray_rocks_etc2_unorm.binary"
                    : "texturearray_rocks_bc3_unorm.binary");
            Texture rockTexture = KtxFile.LoadTexture(
                GraphicsDevice,
                ResourceFactory,
                rockTextureData,
                pixelFormat);
            TextureView rockTextureView = ResourceFactory.CreateTextureView(rockTexture);

            ResourceLayoutElementDescription[] resourceLayoutElementDescriptions =
            {
                new ResourceLayoutElementDescription("LightInfo", ResourceKind.UniformBuffer, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("ProjView", ResourceKind.UniformBuffer, ShaderStages.Vertex),
                new ResourceLayoutElementDescription("RotationInfo", ResourceKind.UniformBuffer, ShaderStages.Vertex),
            };
            ResourceLayoutDescription resourceLayoutDescription = new ResourceLayoutDescription(resourceLayoutElementDescriptions);
            ResourceLayout sharedLayout = factory.CreateResourceLayout(resourceLayoutDescription);

            ResourceLayoutElementDescription[] textureLayoutDescriptions =
            {
                new ResourceLayoutElementDescription("Tex", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("Samp", ResourceKind.Sampler, ShaderStages.Fragment)
            };
            ResourceLayout textureLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(textureLayoutDescriptions));

            BindableResource[] bindableResources = new BindableResource[] { _lightInfoBuffer, _cameraProjViewBuffer, _rotationInfoBuffer };
            ResourceSetDescription resourceSetDescription = new ResourceSetDescription(sharedLayout, bindableResources);
            _sharedResourceSet = factory.CreateResourceSet(resourceSetDescription);

            BindableResource[] instanceBindableResources = { rockTextureView, GraphicsDevice.LinearSampler };
            _instanceTextureSet = factory.CreateResourceSet(new ResourceSetDescription(textureLayout, instanceBindableResources));

            ProcessedModel rock = LoadEmbeddedAsset<ProcessedModel>("rock01.binary");
            _rockModel = rock.MeshParts[0].CreateDeviceResources(GraphicsDevice, ResourceFactory);

            VertexLayoutDescription vertexLayoutPerInstance = new VertexLayoutDescription(
                new VertexElementDescription("InstancePosition", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
                new VertexElementDescription("InstanceRotation", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
                new VertexElementDescription("InstanceScale", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
                new VertexElementDescription("InstanceTexArrayIndex", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Int1));
            vertexLayoutPerInstance.InstanceStepRate = 1;
            _instanceVB = ResourceFactory.CreateBuffer(new BufferDescription(InstanceInfo.Size * _instanceCount, BufferUsage.VertexBuffer));
            InstanceInfo[] infos = new InstanceInfo[_instanceCount];
            Random r = new Random();
            float orbitDistance = 50f;
            for (uint i = 0; i < _instanceCount / 2; i++)
            {
                float angle = (float)(r.NextDouble() * Math.PI * 2);
                infos[i] = new InstanceInfo(
                    new Vector3(
                        ((float)Math.Cos(angle) * orbitDistance) + (float)(-10 + r.NextDouble() * 20),
                        (float)(-1.5 + r.NextDouble() * 3),
                        ((float)Math.Sin(angle) * orbitDistance) + (float)(-10 + r.NextDouble() * 20)),
                    new Vector3(
                        (float)(r.NextDouble() * Math.PI * 2),
                        (float)(r.NextDouble() * Math.PI * 2),
                        (float)(r.NextDouble() * Math.PI * 2)),
                    new Vector3((float)(0.65 + r.NextDouble() * 0.35)),
                    r.Next(0, (int)rockTexture.ArrayLayers));
            }

            orbitDistance = 100f;
            for (uint i = _instanceCount / 2; i < _instanceCount; i++)
            {
                float angle = (float)(r.NextDouble() * Math.PI * 2);
                infos[i] = new InstanceInfo(
                    new Vector3(
                        ((float)Math.Cos(angle) * orbitDistance) + (float)(-10 + r.NextDouble() * 20),
                        (float)(-1.5 + r.NextDouble() * 3),
                        ((float)Math.Sin(angle) * orbitDistance) + (float)(-10 + r.NextDouble() * 20)),
                    new Vector3(
                        (float)(r.NextDouble() * Math.PI * 2),
                        (float)(r.NextDouble() * Math.PI * 2),
                        (float)(r.NextDouble() * Math.PI * 2)),
                    new Vector3((float)(0.65 + r.NextDouble() * 0.35)),
                    r.Next(0, (int)rockTexture.ArrayLayers));
            }

            GraphicsDevice.UpdateBuffer(_instanceVB, 0, infos);

            GraphicsPipelineDescription pipelineDescription = new GraphicsPipelineDescription()
            {
                BlendState = BlendStateDescription.SingleOverrideBlend,
                DepthStencilState = new DepthStencilStateDescription(
                    depthTestEnabled: true,
                    depthWriteEnabled: true,
                    comparisonKind: ComparisonKind.LessEqual),
                RasterizerState = new RasterizerStateDescription(
                    cullMode: FaceCullMode.Back,
                    fillMode: PolygonFillMode.Solid,
                    frontFace: FrontFace.Clockwise,
                    depthClipEnabled: true,
                    scissorTestEnabled: false
                ),
                PrimitiveTopology = PrimitiveTopology.TriangleList,
                ResourceLayouts = new ResourceLayout[] { sharedLayout, textureLayout },
                ShaderSet = new ShaderSetDescription(
                    // The ordering of layouts directly impacts shader layout schemes
                    vertexLayouts: new VertexLayoutDescription[] { sharedVertexLayout, vertexLayoutPerInstance },
                    shaders: LoadShaders("Instance")
                ),
                Outputs = MainSwapchain.Framebuffer.OutputDescription
            };

            _instancePipeline = factory.CreateGraphicsPipeline(pipelineDescription);

            // Create planet Pipeline
            // Almost everything is the same as the rock Pipeline,
            // except no instance vertex buffer is needed, and different shaders are used.
            pipelineDescription.ShaderSet = new ShaderSetDescription(
                new[] { sharedVertexLayout },
                LoadShaders("Planet"));
            _planetPipeline = ResourceFactory.CreateGraphicsPipeline(pipelineDescription);

            ProcessedModel planet = LoadEmbeddedAsset<ProcessedModel>("sphere.binary");
            _planetModel = planet.MeshParts[0].CreateDeviceResources(GraphicsDevice, ResourceFactory);

            byte[] planetTexData = LoadEmbeddedAsset<byte[]>(
                etc2Supported
                    ? "lavaplanet_etc2_unorm.binary"
                    : "lavaplanet_bc3_unorm.binary");
            Texture planetTexture = KtxFile.LoadTexture(GraphicsDevice, ResourceFactory, planetTexData, pixelFormat);
            TextureView planetTextureView = ResourceFactory.CreateTextureView(planetTexture);
            _planetTextureSet = ResourceFactory.CreateResourceSet(new ResourceSetDescription(textureLayout, planetTextureView, GraphicsDevice.Aniso4xSampler));

            // Starfield resources
            ResourceLayout invCameraInfoLayout = ResourceFactory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("InvCameraInfo", ResourceKind.UniformBuffer, ShaderStages.Fragment)));
            _viewInfoBuffer = ResourceFactory.CreateBuffer(new BufferDescription((uint)Unsafe.SizeOf<MatrixPair>(), BufferUsage.UniformBuffer | BufferUsage.Dynamic));
            _viewInfoSet = ResourceFactory.CreateResourceSet(new ResourceSetDescription(invCameraInfoLayout, _viewInfoBuffer));

            ShaderSetDescription starfieldShaders = new ShaderSetDescription(
                Array.Empty<VertexLayoutDescription>(),
                LoadShaders("Starfield"));

            _starfieldPipeline = ResourceFactory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
                BlendStateDescription.SingleOverrideBlend,
                DepthStencilStateDescription.Disabled,
                RasterizerStateDescription.CullNone,
                PrimitiveTopology.TriangleList,
                starfieldShaders,
                new[] { invCameraInfoLayout },
                MainSwapchain.Framebuffer.OutputDescription));

            _commandList = factory.CreateCommandList();
        }

        protected override void Draw(float delta)
        {
            if (InputTracker.GetKeyDown(Key.F1))
            {
                _lightFromCamera = !_lightFromCamera;
            }

            // Begin() must be called before commands can be issued.
            _commandList.Begin();

            // Update per-frame resources.
            _commandList.UpdateBuffer(_cameraProjViewBuffer, 0, new MatrixPair(_camera.ViewMatrix, _camera.ProjectionMatrix));

            if (_lightFromCamera)
            {
                _commandList.UpdateBuffer(_lightInfoBuffer, 0, new LightInfo(_camera.Forward, _camera.Position));
            }
            else
            {
                _commandList.UpdateBuffer(_lightInfoBuffer, 0, new LightInfo(_lightDir, _camera.Position));
            }

            _localRotation += delta * ((float)Math.PI * 2 / 9);
            _globalRotation += -delta * ((float)Math.PI * 2 / 240);
            _commandList.UpdateBuffer(_rotationInfoBuffer, 0, new Vector4(_localRotation, _globalRotation, 0, 0));

            Matrix4x4.Invert(_camera.ProjectionMatrix, out Matrix4x4 inverseProjection);
            Matrix4x4.Invert(_camera.ViewMatrix, out Matrix4x4 inverseView);
            _commandList.UpdateBuffer(_viewInfoBuffer, 0, new MatrixPair(
                inverseProjection,
                inverseView));

            // We want to render directly to the output window.
            _commandList.SetFramebuffer(MainSwapchain.Framebuffer);
            _commandList.ClearColorTarget(0, RgbaFloat.White);
            _commandList.ClearDepthStencil(1f);

            //// First, draw the background starfield.
            _commandList.SetPipeline(_starfieldPipeline);
            _commandList.SetGraphicsResourceSet(0, _viewInfoSet);
            _commandList.Draw(4);

            // Next, draw our orbiting rocks with instanced drawing.
            _commandList.SetPipeline(_instancePipeline);
            // Set uniforms
            _commandList.SetGraphicsResourceSet(0, _sharedResourceSet); // Always after SetPipeline
            _commandList.SetGraphicsResourceSet(1, _instanceTextureSet);

            _commandList.SetVertexBuffer(0, _rockModel.VertexBuffer);
            _commandList.SetIndexBuffer(_rockModel.IndexBuffer, _rockModel.IndexFormat);
            _commandList.SetVertexBuffer(1, _instanceVB);

            // Issue a Draw command for two instances with 4 indices.
            _commandList.DrawIndexed(
                indexCount: _rockModel.IndexCount,
                instanceCount: _instanceCount,
                indexStart: 0,
                vertexOffset: 0,
                instanceStart: 0);

            // Next, we draw our central planet.
            _commandList.SetPipeline(_planetPipeline);
            _commandList.SetGraphicsResourceSet(1, _planetTextureSet);
            _commandList.SetVertexBuffer(0, _planetModel.VertexBuffer);
            _commandList.SetIndexBuffer(_planetModel.IndexBuffer, _planetModel.IndexFormat);

            // The planet is drawn with regular indexed drawing -- not instanced.
            _commandList.DrawIndexed(_planetModel.IndexCount);

            // End() must be called before commands can be submitted for execution.
            _commandList.End();
            GraphicsDevice.SubmitCommands(_commandList);
            GraphicsDevice.WaitForIdle();

            // Once commands have been submitted, the rendered image can be presented to the application window.
            GraphicsDevice.SwapBuffers(MainSwapchain);
        }

        private Shader[] LoadShaders(string setName)
        {
            return ResourceFactory.CreateFromSpirv(
                new ShaderDescription(ShaderStages.Vertex, ReadEmbeddedAssetBytes(setName + "-vertex.glsl"), "main"),
                new ShaderDescription(ShaderStages.Fragment, ReadEmbeddedAssetBytes(setName + "-fragment.glsl"), "main"));
        }
    }

    public struct InstanceInfo
    {
        public static uint Size { get; } = (uint)Unsafe.SizeOf<InstanceInfo>();

        public Vector3 Position;
        public Vector3 Rotation;
        public Vector3 Scale;
        public int TexArrayIndex;

        public InstanceInfo(Vector3 position, Vector3 rotation, Vector3 scale, int texArrayIndex)
        {
            Position = position;
            Rotation = rotation;
            Scale = scale;
            TexArrayIndex = texArrayIndex;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LightInfo
    {
        public Vector3 LightDirection;
        private float padding0;
        public Vector3 CameraPosition;
        private float padding1;

        public LightInfo(Vector3 lightDirection, Vector3 cameraPosition)
        {
            LightDirection = lightDirection;
            CameraPosition = cameraPosition;
            padding0 = 0;
            padding1 = 0;
        }
    }

    public struct Vector3
    {
        public float X;
        public float Y;
        public float Z;

        public Vector3(float value) : this(value, value, value) { }
        public Vector3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static Vector3 Normalize(Vector3 value)
        {
            return System.Numerics.Vector3.Normalize(value);
        }

        public static implicit operator Vector3(System.Numerics.Vector3 systemV3)
        {
            return new Vector3(systemV3.X, systemV3.Y, systemV3.Z);
        }

        public static implicit operator System.Numerics.Vector3(Vector3 v3)
        {
            return new System.Numerics.Vector3(v3.X, v3.Y, v3.Z);
        }
    }

    public struct MatrixPair
    {
        public Matrix4x4 First;
        public Matrix4x4 Second;

        public MatrixPair(Matrix4x4 first, Matrix4x4 second)
        {
            First = first;
            Second = second;
        }
    }
}
