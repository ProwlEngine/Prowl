using Common;
using SampleBase;
using System;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.SPIRV;
using Veldrid.Utilities;

namespace Offscreen
{
    public class OffscreenApplication : SampleApplication
    {
        private const uint OffscreenWidth = 1024;
        private const uint OffscreenHeight = 1024;

        private CommandList _cl;
        private Framebuffer _offscreenFB;
        private Pipeline _offscreenPipeline;
        private Model _dragonModel;
        private Model _planeModel;
        private Texture _colorMap;
        private TextureView _colorView;
        private Texture _offscreenColor;
        private TextureView _offscreenView;
        private VertexLayoutDescription _vertexLayout;
        private Pipeline _dragonPipeline;
        private Pipeline _mirrorPipeline;
        private Vector3 _dragonPos = new Vector3(0, 1.5f, 0);
        private Vector3 _dragonRotation = new Vector3(0, 0, 0);

        private DeviceBuffer _uniformBuffers_vsShared;
        private DeviceBuffer _uniformBuffers_vsMirror;
        private DeviceBuffer _uniformBuffers_vsOffScreen;
        private ResourceSet _offscreenResourceSet;
        private ResourceSet _dragonResourceSet;
        private ResourceSet _mirrorResourceSet;

        public OffscreenApplication(ApplicationWindow window) : base(window)
        {
            _camera.Position = new Vector3(0, 1, 6f);
        }

        protected override void CreateResources(ResourceFactory factory)
        {
            _vertexLayout = new VertexLayoutDescription(
                new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
                new VertexElementDescription("UV", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
                new VertexElementDescription("Color", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
                new VertexElementDescription("Normal", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3));

            using (Stream planeModelStream = OpenEmbeddedAssetStream("plane2.dae"))
            {
                _planeModel = new Model(
                    GraphicsDevice,
                    factory,
                    planeModelStream,
                    "dae",
                    new[] { VertexElementSemantic.Position, VertexElementSemantic.TextureCoordinate, VertexElementSemantic.Color, VertexElementSemantic.Normal },
                    new Model.ModelCreateInfo(new Vector3(0.5f, 0.5f, 0.5f), Vector2.One, Vector3.Zero));
            }

            using (Stream dragonModelStream = OpenEmbeddedAssetStream("chinesedragon.dae"))
            {
                _dragonModel = new Model(
                    GraphicsDevice,
                    factory,
                    dragonModelStream,
                    "dae",
                    new[] { VertexElementSemantic.Position, VertexElementSemantic.TextureCoordinate, VertexElementSemantic.Color, VertexElementSemantic.Normal },
                    new Model.ModelCreateInfo(new Vector3(0.3f, -0.3f, 0.3f), Vector2.One, Vector3.Zero));
            }

            using (Stream colorMapStream = OpenEmbeddedAssetStream("darkmetal_bc3_unorm.ktx"))
            {
                _colorMap = KtxFile.LoadTexture(
                    GraphicsDevice,
                    factory,
                    colorMapStream,
                    PixelFormat.BC3_UNorm);
            }
            _colorView = factory.CreateTextureView(_colorMap);

            _offscreenColor = factory.CreateTexture(TextureDescription.Texture2D(
                OffscreenWidth, OffscreenHeight, 1, 1,
                 PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.RenderTarget | TextureUsage.Sampled));
            _offscreenView = factory.CreateTextureView(_offscreenColor);
            Texture offscreenDepth = factory.CreateTexture(TextureDescription.Texture2D(
                OffscreenWidth, OffscreenHeight, 1, 1, PixelFormat.R16_UNorm, TextureUsage.DepthStencil));
            _offscreenFB = factory.CreateFramebuffer(new FramebufferDescription(offscreenDepth, _offscreenColor));

            ShaderSetDescription phongShaders = new ShaderSetDescription(
                new[] { _vertexLayout },
                factory.CreateFromSpirv(
                    new ShaderDescription(ShaderStages.Vertex, ReadEmbeddedAssetBytes("Phong-vertex.glsl"), "main"),
                    new ShaderDescription(ShaderStages.Fragment, ReadEmbeddedAssetBytes("Phong-fragment.glsl"), "main")));

            ResourceLayout phongLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("UBO", ResourceKind.UniformBuffer, ShaderStages.Vertex)));

            GraphicsPipelineDescription pd = new GraphicsPipelineDescription(
                BlendStateDescription.SingleOverrideBlend,
                DepthStencilStateDescription.DepthOnlyLessEqual,
                new RasterizerStateDescription(FaceCullMode.Front, PolygonFillMode.Solid, FrontFace.Clockwise, true, false),
                PrimitiveTopology.TriangleList,
                phongShaders,
                phongLayout,
                _offscreenFB.OutputDescription);
            _offscreenPipeline = factory.CreateGraphicsPipeline(pd);

            pd.Outputs = GraphicsDevice.SwapchainFramebuffer.OutputDescription;
            pd.RasterizerState = RasterizerStateDescription.Default;
            _dragonPipeline = factory.CreateGraphicsPipeline(pd);

            ResourceLayout mirrorLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("UBO", ResourceKind.UniformBuffer, ShaderStages.Vertex),
                new ResourceLayoutElementDescription("ReflectionMap", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("ReflectionMapSampler", ResourceKind.Sampler, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("ColorMap", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("ColorMapSampler", ResourceKind.Sampler, ShaderStages.Fragment)));

            ShaderSetDescription mirrorShaders = new ShaderSetDescription(
                new[] { _vertexLayout },
                factory.CreateFromSpirv(
                    new ShaderDescription(ShaderStages.Vertex, ReadEmbeddedAssetBytes("Mirror-vertex.glsl"), "main"),
                    new ShaderDescription(ShaderStages.Fragment, ReadEmbeddedAssetBytes("Mirror-fragment.glsl"), "main")));

            GraphicsPipelineDescription mirrorPD = new GraphicsPipelineDescription(
                BlendStateDescription.SingleOverrideBlend,
                DepthStencilStateDescription.DepthOnlyLessEqual,
                new RasterizerStateDescription(FaceCullMode.None, PolygonFillMode.Solid, FrontFace.Clockwise, true, false),
                PrimitiveTopology.TriangleList,
                mirrorShaders,
                mirrorLayout,
                GraphicsDevice.SwapchainFramebuffer.OutputDescription);
            _mirrorPipeline = factory.CreateGraphicsPipeline(ref mirrorPD);

            _uniformBuffers_vsShared = factory.CreateBuffer(new BufferDescription(208, BufferUsage.UniformBuffer | BufferUsage.Dynamic));
            _uniformBuffers_vsMirror = factory.CreateBuffer(new BufferDescription(208, BufferUsage.UniformBuffer | BufferUsage.Dynamic));
            _uniformBuffers_vsOffScreen = factory.CreateBuffer(new BufferDescription(208, BufferUsage.UniformBuffer | BufferUsage.Dynamic));

            _offscreenResourceSet = factory.CreateResourceSet(new ResourceSetDescription(phongLayout, _uniformBuffers_vsOffScreen));
            _dragonResourceSet = factory.CreateResourceSet(new ResourceSetDescription(phongLayout, _uniformBuffers_vsShared));
            _mirrorResourceSet = factory.CreateResourceSet(new ResourceSetDescription(mirrorLayout,
                _uniformBuffers_vsMirror,
                _offscreenView,
                GraphicsDevice.LinearSampler,
                _colorView,
                GraphicsDevice.Aniso4xSampler));

            _cl = factory.CreateCommandList();
        }

        public struct UniformInfo
        {
            public Matrix4x4 Projection;
            public Matrix4x4 View;
            public Matrix4x4 Model;
            public Vector4 LightPos;
        }

        protected override void Draw(float deltaSeconds)
        {
            _dragonRotation.Y += deltaSeconds * 10f;

            UpdateUniformBuffers();
            UpdateUniformBufferOffscreen();

            _cl.Begin();
            DrawOffscreen();
            DrawMain();
            _cl.End();
            GraphicsDevice.SubmitCommands(_cl);
            GraphicsDevice.SwapBuffers();
        }

        private void DrawOffscreen()
        {
            _cl.SetFramebuffer(_offscreenFB);
            _cl.SetFullViewports();
            _cl.ClearColorTarget(0, RgbaFloat.Black);
            _cl.ClearDepthStencil(1f);

            _cl.SetPipeline(_offscreenPipeline);
            _cl.SetGraphicsResourceSet(0, _offscreenResourceSet);
            _cl.SetVertexBuffer(0, _dragonModel.VertexBuffer);
            _cl.SetIndexBuffer(_dragonModel.IndexBuffer, IndexFormat.UInt32);
            _cl.DrawIndexed(_dragonModel.IndexCount, 1, 0, 0, 0);
        }

        private void DrawMain()
        {
            _cl.SetFramebuffer(GraphicsDevice.SwapchainFramebuffer);
            _cl.SetFullViewports();
            _cl.ClearColorTarget(0, RgbaFloat.Black);
            _cl.ClearDepthStencil(1f);

            _cl.SetPipeline(_mirrorPipeline);
            _cl.SetGraphicsResourceSet(0, _mirrorResourceSet);
            _cl.SetVertexBuffer(0, _planeModel.VertexBuffer);
            _cl.SetIndexBuffer(_planeModel.IndexBuffer, IndexFormat.UInt32);
            _cl.DrawIndexed(_planeModel.IndexCount, 1, 0, 0, 0);

            _cl.SetPipeline(_dragonPipeline);
            _cl.SetGraphicsResourceSet(0, _dragonResourceSet);
            _cl.SetVertexBuffer(0, _dragonModel.VertexBuffer);
            _cl.SetIndexBuffer(_dragonModel.IndexBuffer, IndexFormat.UInt32);
            _cl.DrawIndexed(_dragonModel.IndexCount, 1, 0, 0, 0);
        }

        public static float DegreesToRadians(float degrees)
        {
            return degrees * (float)Math.PI / 180f;
        }

        private void UpdateUniformBuffers()
        {
            UniformInfo ui = new UniformInfo { LightPos = new Vector4(0, 0, 0, 1) };

            ui.Projection = Matrix4x4.CreatePerspectiveFieldOfView(DegreesToRadians(60.0f), Window.Width / (float)Window.Height, 0.1f, 256.0f);

            ui.View = Matrix4x4.CreateLookAt(_camera.Position, _camera.Position + _camera.Forward, Vector3.UnitY);

            ui.Model = Matrix4x4.CreateRotationX(DegreesToRadians(_dragonRotation.X));
            ui.Model = Matrix4x4.CreateRotationY(DegreesToRadians(_dragonRotation.Y)) * ui.Model;
            ui.Model = Matrix4x4.CreateTranslation(_dragonPos) * ui.Model;

            GraphicsDevice.UpdateBuffer(_uniformBuffers_vsShared, 0, ref ui);

            // Mirror
            ui.Model = Matrix4x4.Identity;
            GraphicsDevice.UpdateBuffer(_uniformBuffers_vsMirror, 0, ref ui);
        }

        private void UpdateUniformBufferOffscreen()
        {
            UniformInfo ui = new UniformInfo { LightPos = new Vector4(0, 0, 0, 1) };

            ui.Projection = Matrix4x4.CreatePerspectiveFieldOfView(DegreesToRadians(60.0f), Window.Width / (float)Window.Height, 0.1f, 256.0f);

            ui.View = Matrix4x4.CreateLookAt(_camera.Position, _camera.Position + _camera.Forward, Vector3.UnitY);

            ui.Model = Matrix4x4.CreateRotationX(DegreesToRadians(_dragonRotation.X));
            ui.Model = Matrix4x4.CreateRotationY(DegreesToRadians(_dragonRotation.Y)) * ui.Model;
            ui.Model = Matrix4x4.CreateScale(new Vector3(1, -1, 1)) * ui.Model;
            ui.Model = Matrix4x4.CreateTranslation(_dragonPos) * ui.Model;

            GraphicsDevice.UpdateBuffer(_uniformBuffers_vsOffScreen, 0, ref ui);
        }
    }
}
