using SampleBase;
using System;
using System.IO;
using NeoVeldrid;
using System.Numerics;
using Silk.NET.Assimp;

using Camera = SampleBase.Camera;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using NeoVeldrid.SPIRV;

namespace AnimatedMesh
{
    public unsafe class AnimatedMesh : SampleApplication
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

        private static readonly Assimp _assimp = SampleBase.AssimpHelper.GetApi();

        private Animation* _animation;
        private Dictionary<string, uint> _boneIDsByName = new Dictionary<string, uint>();
        private double _previousAnimSeconds = 0;
        private Scene* _scene;
        private Silk.NET.Assimp.Mesh* _firstMesh;
        private BoneAnimInfo _boneAnimInfo = BoneAnimInfo.New();
        private Matrix4x4 _rootNodeInverseTransform;
        private Matrix4x4[] _boneTransformations;
        private float _animationTimeScale = 1f;

        public AnimatedMesh(ApplicationWindow window) : base(window) { }

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
                new ResourceLayoutElementDescription("ProjectionBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex),
                new ResourceLayoutElementDescription("ViewBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex),
                new ResourceLayoutElementDescription("WorldBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex),
                new ResourceLayoutElementDescription("BonesBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex),
                new ResourceLayoutElementDescription("SurfaceTex", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("SurfaceSampler", ResourceKind.Sampler, ShaderStages.Fragment)));

            NeoVeldrid.Texture texture;
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

            byte[] modelBytes;
            using (Stream modelStream = OpenEmbeddedAssetStream("goblin.dae"))
            using (MemoryStream ms = new MemoryStream())
            {
                modelStream.CopyTo(ms);
                modelBytes = ms.ToArray();
            }

            fixed (byte* pBuffer = modelBytes)
            {
                _scene = _assimp.ImportFileFromMemory(pBuffer, (uint)modelBytes.Length, 0, ".dae");
            }

            if (_scene == null || _scene->MRootNode == null)
            {
                throw new InvalidOperationException("Failed to load model: " + _assimp.GetErrorStringS());
            }

            // Transpose from Assimp convention to System.Numerics at the I/O boundary
            Matrix4x4.Invert(_scene->MRootNode->MTransformation.ToSystemMatrix(), out _rootNodeInverseTransform);

            _firstMesh = _scene->MMeshes[0];
            AnimatedVertex[] vertices = new AnimatedVertex[_firstMesh->MNumVertices];
            // Find the first non-null UV channel (Assimp 6.x may place COLLADA UVs in channel 1+)
            Vector3* texCoords = null;
            for (int ch = 0; ch < 8; ch++)
            {
                Vector3* tc = _firstMesh->MTextureCoords[ch];
                if (tc != null)
                {
                    texCoords = tc;
                    break;
                }
            }
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i].Position = _firstMesh->MVertices[i];
                if (texCoords != null)
                {
                    vertices[i].UV = new Vector2(texCoords[i].X, texCoords[i].Y);
                }
            }

            _animation = _scene->MAnimations[0];

            List<int> indices = new List<int>();
            for (uint f = 0; f < _firstMesh->MNumFaces; f++)
            {
                Face face = _firstMesh->MFaces[f];
                if (face.MNumIndices == 3)
                {
                    indices.Add((int)face.MIndices[0]);
                    indices.Add((int)face.MIndices[1]);
                    indices.Add((int)face.MIndices[2]);
                }
            }

            for (uint boneID = 0; boneID < _firstMesh->MNumBones; boneID++)
            {
                Bone* bone = _firstMesh->MBones[boneID];
                _boneIDsByName.Add(bone->MName.AsString, boneID);
                for (uint w = 0; w < bone->MNumWeights; w++)
                {
                    VertexWeight weight = bone->MWeights[w];
                    vertices[weight.MVertexId].AddBone(boneID, weight.MWeight);
                }
            }
            _boneTransformations = new Matrix4x4[_firstMesh->MNumBones];

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
            double tps = _animation->MTicksPerSecond != 0 ? _animation->MTicksPerSecond : 25;
            double durationTicks = _animation->MDuration;
            double newTicks = _previousAnimSeconds + (deltaSeconds * _animationTimeScale * tps);
            newTicks = newTicks % durationTicks;
            _previousAnimSeconds = newTicks;

            double ticks = newTicks;

            UpdateChannel(ticks, _scene->MRootNode, Matrix4x4.Identity);

            // Already in System.Numerics convention - no transpose needed
            for (int i = 0; i < _boneTransformations.Length; i++)
            {
                _boneAnimInfo.BonesTransformations[i] = _boneTransformations[i];
            }

            GraphicsDevice.UpdateBuffer(_bonesBuffer, 0, _boneAnimInfo.GetBlittable());
        }

        private void UpdateChannel(double time, Node* node, Matrix4x4 parentTransform)
        {
            // Transpose at the I/O boundary: Assimp -> System.Numerics
            Matrix4x4 nodeTransformation = node->MTransformation.ToSystemMatrix();

            if (GetChannel(node, out NodeAnim* channel))
            {
                Matrix4x4 scale = InterpolateScale(time, channel);
                Matrix4x4 rotation = InterpolateRotation(time, channel);
                Matrix4x4 translation = InterpolateTranslation(time, channel);

                // Same order as original AssimpNet code.
                // AssimpNet's reversed operator* and the transpose cancel each other out.
                nodeTransformation = scale * rotation * translation;
            }

            if (_boneIDsByName.TryGetValue(node->MName.AsString, out uint boneID))
            {
                Matrix4x4 offsetMatrix = _firstMesh->MBones[(int)boneID]->MOffsetMatrix.ToSystemMatrix();
                Matrix4x4 m = offsetMatrix
                    * nodeTransformation
                    * parentTransform
                    * _rootNodeInverseTransform;
                _boneTransformations[boneID] = m;
            }

            for (uint i = 0; i < node->MNumChildren; i++)
            {
                UpdateChannel(time, node->MChildren[i], nodeTransformation * parentTransform);
            }
        }

        private Matrix4x4 InterpolateTranslation(double time, NodeAnim* channel)
        {
            Vector3 position;

            if (channel->MNumPositionKeys == 1)
            {
                position = channel->MPositionKeys[0].MValue;
            }
            else
            {
                uint frameIndex = 0;
                for (uint i = 0; i < channel->MNumPositionKeys - 1; i++)
                {
                    if (time < (float)channel->MPositionKeys[i + 1].MTime)
                    {
                        frameIndex = i;
                        break;
                    }
                }

                VectorKey currentFrame = channel->MPositionKeys[frameIndex];
                VectorKey nextFrame = channel->MPositionKeys[(frameIndex + 1) % channel->MNumPositionKeys];

                double delta = (time - (float)currentFrame.MTime) / (float)(nextFrame.MTime - currentFrame.MTime);

                Vector3 start = currentFrame.MValue;
                Vector3 end = nextFrame.MValue;
                position = start + (float)delta * (end - start);
            }

            return Matrix4x4.CreateTranslation(position);
        }

        private Matrix4x4 InterpolateRotation(double time, NodeAnim* channel)
        {
            Quaternion rotation;

            if (channel->MNumRotationKeys == 1)
            {
                rotation = channel->MRotationKeys[0].MValue.ToSystemQuaternion();
            }
            else
            {
                uint frameIndex = 0;
                for (uint i = 0; i < channel->MNumRotationKeys - 1; i++)
                {
                    if (time < (float)channel->MRotationKeys[i + 1].MTime)
                    {
                        frameIndex = i;
                        break;
                    }
                }

                QuatKey currentFrame = channel->MRotationKeys[frameIndex];
                QuatKey nextFrame = channel->MRotationKeys[(frameIndex + 1) % channel->MNumRotationKeys];

                double delta = (time - (float)currentFrame.MTime) / (float)(nextFrame.MTime - currentFrame.MTime);

                Quaternion start = currentFrame.MValue.ToSystemQuaternion();
                Quaternion end = nextFrame.MValue.ToSystemQuaternion();
                rotation = Quaternion.Slerp(start, end, (float)delta);
                rotation = Quaternion.Normalize(rotation);
            }

            return Matrix4x4.CreateFromQuaternion(rotation);
        }

        private Matrix4x4 InterpolateScale(double time, NodeAnim* channel)
        {
            Vector3 scale;

            if (channel->MNumScalingKeys == 1)
            {
                scale = channel->MScalingKeys[0].MValue;
            }
            else
            {
                uint frameIndex = 0;
                for (uint i = 0; i < channel->MNumScalingKeys - 1; i++)
                {
                    if (time < (float)channel->MScalingKeys[i + 1].MTime)
                    {
                        frameIndex = i;
                        break;
                    }
                }

                VectorKey currentFrame = channel->MScalingKeys[frameIndex];
                VectorKey nextFrame = channel->MScalingKeys[(frameIndex + 1) % channel->MNumScalingKeys];

                double delta = (time - (float)currentFrame.MTime) / (float)(nextFrame.MTime - currentFrame.MTime);

                Vector3 start = currentFrame.MValue;
                Vector3 end = nextFrame.MValue;
                scale = start + (float)delta * (end - start);
            }

            return Matrix4x4.CreateScale(scale);
        }

        private bool GetChannel(Node* node, out NodeAnim* channel)
        {
            string nodeName = node->MName.AsString;
            for (uint i = 0; i < _animation->MNumChannels; i++)
            {
                NodeAnim* c = _animation->MChannels[i];
                if (c->MNodeName.AsString == nodeName)
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
