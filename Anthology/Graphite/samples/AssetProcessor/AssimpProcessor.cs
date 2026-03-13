using AssetPrimitives;
using Assimp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Veldrid;
using aiMatrix4x4 = Assimp.Matrix4x4;

namespace AssetProcessor
{
    public class AssimpProcessor : BinaryAssetProcessor<ProcessedModel>
    {
        public unsafe override ProcessedModel ProcessT(Stream stream, string extension)
        {
            AssimpContext ac = new AssimpContext();
            Scene scene = ac.ImportFileFromStream(
                stream, 
                PostProcessSteps.FlipWindingOrder | PostProcessSteps.GenerateNormals | PostProcessSteps.FlipUVs,
                extension);
            aiMatrix4x4 rootNodeInverseTransform = scene.RootNode.Transform;
            rootNodeInverseTransform.Inverse();

            List<ProcessedMeshPart> parts = new List<ProcessedMeshPart>();
            List<ProcessedAnimation> animations = new List<ProcessedAnimation>();

            HashSet<string> encounteredNames = new HashSet<string>();
            for (int meshIndex = 0; meshIndex < scene.MeshCount; meshIndex++)
            {
                Mesh mesh = scene.Meshes[meshIndex];
                string meshName = mesh.Name;
                if (string.IsNullOrEmpty(meshName))
                {
                    meshName = $"mesh_{meshIndex}";
                }
                int counter = 1;
                while (!encounteredNames.Add(meshName))
                {
                    meshName = mesh.Name + "_" + counter.ToString();
                    counter += 1;
                }
                int vertexCount = mesh.VertexCount;

                int positionOffset = 0;
                int normalOffset = 12;
                int texCoordsOffset = -1;
                int boneWeightOffset = -1;
                int boneIndicesOffset = -1;

                List<VertexElementDescription> elementDescs = new List<VertexElementDescription>();
                elementDescs.Add(new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3));
                elementDescs.Add(new VertexElementDescription("Normal", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3));
                normalOffset = 12;

                int vertexSize = 24;

                bool hasTexCoords = mesh.HasTextureCoords(0);
                elementDescs.Add(new VertexElementDescription("TexCoords", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2));
                texCoordsOffset = vertexSize;
                vertexSize += 8;

                bool hasBones = mesh.HasBones;
                if (hasBones)
                {
                    elementDescs.Add(new VertexElementDescription("BoneWeights", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4));
                    elementDescs.Add(new VertexElementDescription("BoneIndices", VertexElementSemantic.TextureCoordinate, VertexElementFormat.UInt4));

                    boneWeightOffset = vertexSize;
                    vertexSize += 16;

                    boneIndicesOffset = vertexSize;
                    vertexSize += 16;
                }

                byte[] vertexData = new byte[vertexCount * vertexSize];
                VertexDataBuilder builder = new VertexDataBuilder(vertexData, vertexSize);
                Vector3 min = vertexCount > 0 ? mesh.Vertices[0].ToSystemVector3() : Vector3.Zero;
                Vector3 max = vertexCount > 0 ? mesh.Vertices[0].ToSystemVector3() : Vector3.Zero;

                for (int i = 0; i < vertexCount; i++)
                {
                    Vector3 position = mesh.Vertices[i].ToSystemVector3();
                    min = Vector3.Min(min, position);
                    max = Vector3.Max(max, position);

                    builder.WriteVertexElement(
                        i,
                        positionOffset,
                        position);

                    Vector3 normal = mesh.Normals[i].ToSystemVector3();
                    builder.WriteVertexElement(i, normalOffset, normal);

                    if (mesh.HasTextureCoords(0))
                    {
                        builder.WriteVertexElement(
                            i,
                            texCoordsOffset,
                            new Vector2(mesh.TextureCoordinateChannels[0][i].X, mesh.TextureCoordinateChannels[0][i].Y));
                    }
                    else
                    {
                        builder.WriteVertexElement(
                            i,
                            texCoordsOffset,
                            new Vector2());
                    }
                }

                List<int> indices = new List<int>();
                foreach (Face face in mesh.Faces)
                {
                    if (face.IndexCount == 3)
                    {
                        indices.Add(face.Indices[0]);
                        indices.Add(face.Indices[1]);
                        indices.Add(face.Indices[2]);
                    }
                }

                Dictionary<string, uint> boneIDsByName = new Dictionary<string, uint>();
                System.Numerics.Matrix4x4[] boneOffsets = new System.Numerics.Matrix4x4[mesh.BoneCount];

                if (hasBones)
                {
                    Dictionary<int, int> assignedBoneWeights = new Dictionary<int, int>();
                    for (uint boneID = 0; boneID < mesh.BoneCount; boneID++)
                    {
                        Bone bone = mesh.Bones[(int)boneID];
                        string boneName = bone.Name;
                        int suffix = 1;
                        while (boneIDsByName.ContainsKey(boneName))
                        {
                            boneName = bone.Name + "_" + suffix.ToString();
                            suffix += 1;
                        }

                        boneIDsByName.Add(boneName, boneID);
                        foreach (VertexWeight weight in bone.VertexWeights)
                        {
                            int relativeBoneIndex = GetAndIncrementRelativeBoneIndex(assignedBoneWeights, weight.VertexID);
                            builder.WriteVertexElement(weight.VertexID, boneIndicesOffset + (relativeBoneIndex * sizeof(uint)), boneID);
                            builder.WriteVertexElement(weight.VertexID, boneWeightOffset + (relativeBoneIndex * sizeof(float)), weight.Weight);
                        }

                        System.Numerics.Matrix4x4 offsetMat = bone.OffsetMatrix.ToSystemMatrixTransposed();
                        System.Numerics.Matrix4x4.Decompose(offsetMat, out var scale, out var rot, out var trans);
                        offsetMat = System.Numerics.Matrix4x4.CreateScale(scale)
                            * System.Numerics.Matrix4x4.CreateFromQuaternion(rot)
                            * System.Numerics.Matrix4x4.CreateTranslation(trans);

                        boneOffsets[boneID] = offsetMat;
                    }
                }
                builder.FreeGCHandle();

                uint indexCount = (uint)indices.Count;

                int[] int32Indices = indices.ToArray();
                byte[] indexData = new byte[indices.Count * sizeof(uint)];
                fixed (byte* indexDataPtr = indexData)
                {
                    fixed (int* int32Ptr = int32Indices)
                    {
                        Buffer.MemoryCopy(int32Ptr, indexDataPtr, indexData.Length, indexData.Length);
                    }
                }

                ProcessedMeshPart part = new ProcessedMeshPart(
                    vertexData,
                    elementDescs.ToArray(),
                    indexData,
                    IndexFormat.UInt32,
                    (uint)indices.Count,
                    boneIDsByName,
                    boneOffsets);
                parts.Add(part);
            }

            // Nodes
            Node rootNode = scene.RootNode;
            List<ProcessedNode> processedNodes = new List<ProcessedNode>();
            ConvertNode(rootNode, -1, processedNodes);

            ProcessedNodeSet nodes = new ProcessedNodeSet(processedNodes.ToArray(), 0, rootNodeInverseTransform.ToSystemMatrixTransposed());

            for (int animIndex = 0; animIndex < scene.AnimationCount; animIndex++)
            {
                Animation animation = scene.Animations[animIndex];
                Dictionary<string, ProcessedAnimationChannel> channels = new Dictionary<string, ProcessedAnimationChannel>();
                for (int channelIndex = 0; channelIndex < animation.NodeAnimationChannelCount; channelIndex++)
                {
                    NodeAnimationChannel nac = animation.NodeAnimationChannels[channelIndex];
                    channels[nac.NodeName] = ConvertChannel(nac);
                }

                string baseAnimName = animation.Name;
                if (string.IsNullOrEmpty(baseAnimName))
                {
                    baseAnimName = "anim_" + animIndex;
                }

                string animationName = baseAnimName;


                int counter = 1;
                while (!encounteredNames.Add(animationName))
                {
                    animationName = baseAnimName + "_" + counter.ToString();
                    counter += 1;
                }
            }

            return new ProcessedModel()
            {
                MeshParts = parts.ToArray(),
                Animations = animations.ToArray(),
                Nodes = nodes
            };
        }

        private int GetAndIncrementRelativeBoneIndex(Dictionary<int, int> assignedBoneWeights, int vertexID)
        {
            int currentCount = 0;
            assignedBoneWeights.TryGetValue(vertexID, out currentCount);
            assignedBoneWeights[vertexID] = currentCount + 1;
            return currentCount;
        }

        private ProcessedAnimationChannel ConvertChannel(NodeAnimationChannel nac)
        {
            string nodeName = nac.NodeName;
            AssetPrimitives.VectorKey[] positions = new AssetPrimitives.VectorKey[nac.PositionKeyCount];
            for (int i = 0; i < nac.PositionKeyCount; i++)
            {
                Assimp.VectorKey assimpKey = nac.PositionKeys[i];
                positions[i] = new AssetPrimitives.VectorKey(assimpKey.Time, assimpKey.Value.ToSystemVector3());
            }

            AssetPrimitives.VectorKey[] scales = new AssetPrimitives.VectorKey[nac.ScalingKeyCount];
            for (int i = 0; i < nac.ScalingKeyCount; i++)
            {
                Assimp.VectorKey assimpKey = nac.ScalingKeys[i];
                scales[i] = new AssetPrimitives.VectorKey(assimpKey.Time, assimpKey.Value.ToSystemVector3());
            }

            AssetPrimitives.QuaternionKey[] rotations = new AssetPrimitives.QuaternionKey[nac.RotationKeyCount];
            for (int i = 0; i < nac.RotationKeyCount; i++)
            {
                Assimp.QuaternionKey assimpKey = nac.RotationKeys[i];
                rotations[i] = new AssetPrimitives.QuaternionKey(assimpKey.Time, assimpKey.Value.ToSystemQuaternion());
            }

            return new ProcessedAnimationChannel(nodeName, positions, scales, rotations);
        }

        private int ConvertNode(Node node, int parentIndex, List<ProcessedNode> processedNodes)
        {
            int currentIndex = processedNodes.Count;
            int[] childIndices = new int[node.ChildCount];
            var nodeTransform = node.Transform.ToSystemMatrixTransposed();
            ProcessedNode pn = new ProcessedNode(node.Name, nodeTransform, parentIndex, childIndices);
            processedNodes.Add(pn);

            for (int i = 0; i < childIndices.Length; i++)
            {
                int childIndex = ConvertNode(node.Children[i], currentIndex, processedNodes);
                childIndices[i] = childIndex;
            }

            return currentIndex;
        }

        private unsafe struct VertexDataBuilder
        {
            private readonly GCHandle _gch;
            private readonly unsafe byte* _dataPtr;
            private readonly int _vertexSize;

            public VertexDataBuilder(byte[] data, int vertexSize)
            {
                _gch = GCHandle.Alloc(data, GCHandleType.Pinned);
                _dataPtr = (byte*)_gch.AddrOfPinnedObject();
                _vertexSize = vertexSize;
            }

            public void WriteVertexElement<T>(int vertex, int elementOffset, ref T data)
            {
                byte* dst = _dataPtr + (_vertexSize * vertex) + elementOffset;
                Unsafe.Copy(dst, ref data);
            }

            public void WriteVertexElement<T>(int vertex, int elementOffset, T data)
            {
                byte* dst = _dataPtr + (_vertexSize * vertex) + elementOffset;
                Unsafe.Copy(dst, ref data);
            }

            public void FreeGCHandle()
            {
                _gch.Free();
            }
        }
    }

    public static class AssimpExtensions
    {
        public static unsafe System.Numerics.Matrix4x4 ToSystemMatrixTransposed(this aiMatrix4x4 mat)
        {
            return System.Numerics.Matrix4x4.Transpose(Unsafe.Read<System.Numerics.Matrix4x4>(&mat));
        }

        public static System.Numerics.Quaternion ToSystemQuaternion(this Assimp.Quaternion quat)
        {
            return new System.Numerics.Quaternion(quat.X, quat.Y, quat.Z, quat.W);
        }

        public static Vector3 ToSystemVector3(this Assimp.Vector3D v3)
        {
            return new Vector3(v3.X, v3.Y, v3.Z);
        }
    }
}
