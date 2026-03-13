using SampleBase;
using System;
using System.IO;
using Veldrid;
using System.Numerics;
using Assimp;

using aiMatrix4x4 = Assimp.Matrix4x4;
using Matrix4x4 = System.Numerics.Matrix4x4;
using Quaternion = System.Numerics.Quaternion;
using aiQuaternion = Assimp.Quaternion;
using Camera = SampleBase.Camera;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using Veldrid.SPIRV;

namespace AnimatedMesh
{
    public class AnimatedMesh : SampleApplication
    {
        private DeviceBuffer _projectionBuffer;
        private DeviceBuffer _viewBuffer;
        private DeviceBuffer _worldBuffer;
        private DeviceBuffer _vertexBuffer;
        private DeviceBuffer _indexBuffer;
        private uint _indexCount;
        private DeviceBuffer _bonesBuffer;
        private TextureView _texView;
        private ResourceSet _rs;
        private CommandList _cl;
        private Pipeline _pipeline;

        private Animation _animation;
        private Dictionary<string, uint> _boneIDsByName = new Dictionary<string, uint>();
        private double _previousAnimSeconds = 0;
        private Scene _scene;
        private Mesh _firstMesh;
        private BoneAnimInfo _boneAnimInfo = BoneAnimInfo.New();
        private aiMatrix4x4 _rootNodeInverseTransform;
        private aiMatrix4x4[] _boneTransformations;
        private float _animationTimeScale = 1f;

        public AnimatedMesh(ApplicationWindow window) : base(window) { }

        private static string GetAssetPath(string relativeAssetPath)
        {
            return Path.Combine(AppContext.BaseDirectory, "assets", relativeAssetPath);
        }

        protected override void CreateResources(ResourceFactory factory)
        {
            _projectionBuffer = factory.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer | BufferUsage.Dynamic));
            _viewBuffer = factory.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer | BufferUsage.Dynamic));
            _worldBuffer = factory.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer | BufferUsage.Dynamic));
            Matrix4x4 worldMatrix =
                Matrix4x4.CreateTranslation(0, 15000, -5000)
                * Matrix4x4.CreateRotationX(3 * (float)Math.PI / 2)
                * Matrix4x4.CreateScale(0.05f);
            GraphicsDevice.UpdateBuffer(_worldBuffer, 0, ref worldMatrix);

            ResourceLayout layout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("Projection", ResourceKind.UniformBuffer, ShaderStages.Vertex),
                new ResourceLayoutElementDescription("View", ResourceKind.UniformBuffer, ShaderStages.Vertex),
                new ResourceLayoutElementDescription("World", ResourceKind.UniformBuffer, ShaderStages.Vertex),
                new ResourceLayoutElementDescription("Bones", ResourceKind.UniformBuffer, ShaderStages.Vertex),
                new ResourceLayoutElementDescription("SurfaceTex", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("SurfaceSampler", ResourceKind.Sampler, ShaderStages.Fragment)));

            Texture texture;
            using (Stream ktxStream = OpenEmbeddedAssetStream("goblin_bc3_unorm.ktx"))
            {
                texture = KtxFile.LoadTexture(
                    GraphicsDevice,
                    factory,
                    ktxStream,
                    PixelFormat.BC3_UNorm);
            }
            _texView = ResourceFactory.CreateTextureView(texture);

            VertexLayoutDescription vertexLayouts = new VertexLayoutDescription(
                new[]
                {
                    new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
                    new VertexElementDescription("UV", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
                    new VertexElementDescription("BoneWeights", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4),
                    new VertexElementDescription("BoneIndices", VertexElementSemantic.TextureCoordinate, VertexElementFormat.UInt4),
                });

            GraphicsPipelineDescription gpd = new GraphicsPipelineDescription(
                BlendStateDescription.SingleOverrideBlend,
                DepthStencilStateDescription.DepthOnlyLessEqual,
                new RasterizerStateDescription(FaceCullMode.Back, PolygonFillMode.Solid, FrontFace.CounterClockwise, true, false),
                PrimitiveTopology.TriangleList,
                new ShaderSetDescription(
                    new[] { vertexLayouts },
                    factory.CreateFromSpirv(
                        new ShaderDescription(ShaderStages.Vertex, Encoding.UTF8.GetBytes(VertexCode), "main"),
                        new ShaderDescription(ShaderStages.Fragment, Encoding.UTF8.GetBytes(FragmentCode), "main"))),
                layout,
                GraphicsDevice.SwapchainFramebuffer.OutputDescription);
            _pipeline = factory.CreateGraphicsPipeline(ref gpd);

            AssimpContext ac = new AssimpContext();
            using (Stream modelStream = OpenEmbeddedAssetStream("goblin.dae"))
            {
                _scene = ac.ImportFileFromStream(modelStream, "dae");
            }
            _rootNodeInverseTransform = _scene.RootNode.Transform;
            _rootNodeInverseTransform.Inverse();

            _firstMesh = _scene.Meshes[0];
            AnimatedVertex[] vertices = new AnimatedVertex[_firstMesh.VertexCount];
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i].Position = new Vector3(_firstMesh.Vertices[i].X, _firstMesh.Vertices[i].Y, _firstMesh.Vertices[i].Z);
                vertices[i].UV = new Vector2(_firstMesh.TextureCoordinateChannels[0][i].X, _firstMesh.TextureCoordinateChannels[0][i].Y);
            }

            _animation = _scene.Animations[0];

            List<int> indices = new List<int>();
            foreach (Face face in _firstMesh.Faces)
            {
                if (face.IndexCount == 3)
                {
                    indices.Add(face.Indices[0]);
                    indices.Add(face.Indices[1]);
                    indices.Add(face.Indices[2]);
                }
            }

            for (uint boneID = 0; boneID < _firstMesh.BoneCount; boneID++)
            {
                Bone bone = _firstMesh.Bones[(int)boneID];
                _boneIDsByName.Add(bone.Name, boneID);
                foreach (VertexWeight weight in bone.VertexWeights)
                {
                    vertices[weight.VertexID].AddBone(boneID, weight.Weight);
                }
            }
            Array.Resize(ref _boneTransformations, _firstMesh.BoneCount);

            _bonesBuffer = ResourceFactory.CreateBuffer(new BufferDescription(
                64 * 64, BufferUsage.UniformBuffer | BufferUsage.Dynamic));

            _rs = factory.CreateResourceSet(new ResourceSetDescription(layout,
                _projectionBuffer, _viewBuffer, _worldBuffer, _bonesBuffer, _texView, GraphicsDevice.Aniso4xSampler));

            _indexCount = (uint)indices.Count;

            _vertexBuffer = ResourceFactory.CreateBuffer(new BufferDescription(
                (uint)(vertices.Length * Unsafe.SizeOf<AnimatedVertex>()), BufferUsage.VertexBuffer));
            GraphicsDevice.UpdateBuffer(_vertexBuffer, 0, vertices);

            _indexBuffer = ResourceFactory.CreateBuffer(new BufferDescription(
                _indexCount * 4, BufferUsage.IndexBuffer));
            GraphicsDevice.UpdateBuffer(_indexBuffer, 0, indices.ToArray());

            _cl = factory.CreateCommandList();
            _camera.Position = new Vector3(110, -87, -532);
            _camera.Yaw = 0.45f;
            _camera.Pitch = -0.55f;
            _camera.MoveSpeed = 1000f;
            _camera.FarDistance = 100000;
        }

        protected override void Draw(float deltaSeconds)
        {
            UpdateAnimation(deltaSeconds);
            UpdateUniforms();
            _cl.Begin();
            _cl.SetFramebuffer(GraphicsDevice.SwapchainFramebuffer);
            _cl.ClearColorTarget(0, RgbaFloat.Black);
            _cl.ClearDepthStencil(1f);
            _cl.SetPipeline(_pipeline);
            _cl.SetGraphicsResourceSet(0, _rs);
            _cl.SetVertexBuffer(0, _vertexBuffer);
            _cl.SetIndexBuffer(_indexBuffer, IndexFormat.UInt32);
            _cl.DrawIndexed(_indexCount);
            _cl.End();
            GraphicsDevice.SubmitCommands(_cl);
            GraphicsDevice.SwapBuffers();
        }

        private void UpdateAnimation(float deltaSeconds)
        {
            double totalSeconds = _animation.DurationInTicks * _animation.TicksPerSecond;
            double newSeconds = _previousAnimSeconds + (deltaSeconds * _animationTimeScale);
            newSeconds = newSeconds % totalSeconds;
            _previousAnimSeconds = newSeconds;

            double ticks = newSeconds * _animation.TicksPerSecond;

            UpdateChannel(ticks, _scene.RootNode, aiMatrix4x4.Identity);

            for (int i = 0; i < _boneTransformations.Length; i++)
            {
                _boneAnimInfo.BonesTransformations[i] = _boneTransformations[i].ToSystemMatrixTransposed();
            }

            GraphicsDevice.UpdateBuffer(_bonesBuffer, 0, _boneAnimInfo.GetBlittable());
        }

        private void UpdateChannel(double time, Node node, aiMatrix4x4 parentTransform)
        {
            aiMatrix4x4 nodeTransformation = node.Transform;

            if (GetChannel(node, out NodeAnimationChannel channel))
            {
                aiMatrix4x4 scale = InterpolateScale(time, channel);
                aiMatrix4x4 rotation = InterpolateRotation(time, channel);
                aiMatrix4x4 translation = InterpolateTranslation(time, channel);

                nodeTransformation = scale * rotation * translation;
            }

            if (_boneIDsByName.TryGetValue(node.Name, out uint boneID))
            {
                aiMatrix4x4 m = _firstMesh.Bones[(int)boneID].OffsetMatrix
                    * nodeTransformation
                    * parentTransform
                    * _rootNodeInverseTransform;
                _boneTransformations[boneID] = m;
            }

            foreach (Node childNode in node.Children)
            {
                UpdateChannel(time, childNode, nodeTransformation * parentTransform);
            }
        }

        private aiMatrix4x4 InterpolateTranslation(double time, NodeAnimationChannel channel)
        {
            Vector3D position;

            if (channel.PositionKeyCount == 1)
            {
                position = channel.PositionKeys[0].Value;
            }
            else
            {
                uint frameIndex = 0;
                for (uint i = 0; i < channel.PositionKeyCount - 1; i++)
                {
                    if (time < (float)channel.PositionKeys[(int)(i + 1)].Time)
                    {
                        frameIndex = i;
                        break;
                    }
                }

                VectorKey currentFrame = channel.PositionKeys[(int)frameIndex];
                VectorKey nextFrame = channel.PositionKeys[(int)((frameIndex + 1) % channel.PositionKeyCount)];

                double delta = (time - (float)currentFrame.Time) / (float)(nextFrame.Time - currentFrame.Time);

                Vector3D start = currentFrame.Value;
                Vector3D end = nextFrame.Value;
                position = (start + (float)delta * (end - start));
            }

            return aiMatrix4x4.FromTranslation(position);
        }

        private aiMatrix4x4 InterpolateRotation(double time, NodeAnimationChannel channel)
        {
            aiQuaternion rotation;

            if (channel.RotationKeyCount == 1)
            {
                rotation = channel.RotationKeys[0].Value;
            }
            else
            {
                uint frameIndex = 0;
                for (uint i = 0; i < channel.RotationKeyCount - 1; i++)
                {
                    if (time < (float)channel.RotationKeys[(int)(i + 1)].Time)
                    {
                        frameIndex = i;
                        break;
                    }
                }

                QuaternionKey currentFrame = channel.RotationKeys[(int)frameIndex];
                QuaternionKey nextFrame = channel.RotationKeys[(int)((frameIndex + 1) % channel.RotationKeyCount)];

                double delta = (time - (float)currentFrame.Time) / (float)(nextFrame.Time - currentFrame.Time);

                aiQuaternion start = currentFrame.Value;
                aiQuaternion end = nextFrame.Value;
                rotation = aiQuaternion.Slerp(start, end, (float)delta);
                rotation.Normalize();
            }

            return rotation.GetMatrix();
        }

        private aiMatrix4x4 InterpolateScale(double time, NodeAnimationChannel channel)
        {
            Vector3D scale;

            if (channel.ScalingKeyCount == 1)
            {
                scale = channel.ScalingKeys[0].Value;
            }
            else
            {
                uint frameIndex = 0;
                for (uint i = 0; i < channel.ScalingKeyCount - 1; i++)
                {
                    if (time < (float)channel.ScalingKeys[(int)(i + 1)].Time)
                    {
                        frameIndex = i;
                        break;
                    }
                }

                VectorKey currentFrame = channel.ScalingKeys[(int)frameIndex];
                VectorKey nextFrame = channel.ScalingKeys[(int)((frameIndex + 1) % channel.ScalingKeyCount)];

                double delta = (time - (float)currentFrame.Time) / (float)(nextFrame.Time - currentFrame.Time);

                Vector3D start = currentFrame.Value;
                Vector3D end = nextFrame.Value;

                scale = (start + (float)delta * (end - start));
            }

            return aiMatrix4x4.FromScaling(scale);
        }

        private bool GetChannel(Node node, out NodeAnimationChannel channel)
        {
            foreach (NodeAnimationChannel c in _animation.NodeAnimationChannels)
            {
                if (c.NodeName == node.Name)
                {
                    channel = c;
                    return true;
                }
            }

            channel = null;
            return false;
        }

        protected override void OnKeyDown(KeyEvent keyEvent)
        {
            if (keyEvent.Key == Key.KeypadPlus)
            {
                _animationTimeScale = Math.Min(3, _animationTimeScale + 0.25f);
            }
            if (keyEvent.Key == Key.KeypadMinus)
            {
                _animationTimeScale = Math.Max(0, _animationTimeScale - 0.25f);
            }
        }

        private void UpdateUniforms()
        {
            GraphicsDevice.UpdateBuffer(_projectionBuffer, 0, _camera.ProjectionMatrix);
            GraphicsDevice.UpdateBuffer(_viewBuffer, 0, _camera.ViewMatrix);
        }

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

layout(set = 0, binding = 2) uniform WorldBuffer
{
    mat4 World;
};

layout(set = 0, binding = 3) uniform BonesBuffer
{
    mat4 BonesTransformations[64];
};

layout(location = 0) in vec3 Position;
layout(location = 1) in vec2 UV;
layout(location = 2) in vec4 BoneWeights;
layout(location = 3) in uvec4 BoneIndices;
layout(location = 0) out vec2 fsin_uv;

void main()
{
    mat4 boneTransformation = BonesTransformations[BoneIndices.x]  * BoneWeights.x;
    boneTransformation += BonesTransformations[BoneIndices.y]  * BoneWeights.y;
    boneTransformation += BonesTransformations[BoneIndices.z]  * BoneWeights.z;
    boneTransformation += BonesTransformations[BoneIndices.w]  * BoneWeights.w;
    gl_Position = Projection * View * World * boneTransformation * vec4(Position, 1);
    fsin_uv = UV;
}";

        private const string FragmentCode = @"
#version 450

layout(set = 0, binding = 4) uniform texture2D SurfaceTex;
layout(set = 0, binding = 5) uniform sampler SurfaceSampler;

layout(location = 0) in vec2 fsin_uv;
layout(location = 0) out vec4 fsout_color;

void main()
{
    fsout_color = texture(sampler2D(SurfaceTex, SurfaceSampler), fsin_uv);
}";
    }
}
