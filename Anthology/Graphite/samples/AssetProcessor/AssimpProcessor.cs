using AssetPrimitives;
using Silk.NET.Assimp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Veldrid;

using Mesh = Silk.NET.Assimp.Mesh;
using Texture = Veldrid.Texture;

namespace AssetProcessor
{
    public class AssimpProcessor : BinaryAssetProcessor<ProcessedModel>
    {
        private static readonly Assimp _assimp = SampleBase.AssimpHelper.GetApi();

        public unsafe override ProcessedModel ProcessT(Stream stream, string extension)
        {
            byte[] modelBytes;
            using (MemoryStream ms = new MemoryStream())
            {
                stream.CopyTo(ms);
                modelBytes = ms.ToArray();
            }

            Scene* scene;
            fixed (byte* pBuffer = modelBytes)
            {
                scene = _assimp.ImportFileFromMemory(
                    pBuffer,
                    (uint)modelBytes.Length,
                    (uint)(PostProcessSteps.FlipWindingOrder | PostProcessSteps.GenerateNormals | PostProcessSteps.FlipUVs),
                    extension);
            }

            if (scene == null)
            {
                throw new InvalidOperationException("Failed to load model: " + _assimp.GetErrorStringS());
            }

            Matrix4x4 rootNodeInverseTransform;
            Matrix4x4.Invert(scene->MRootNode->MTransformation.ToSystemMatrix(), out rootNodeInverseTransform);

            List<ProcessedMeshPart> parts = new List<ProcessedMeshPart>();
            List<ProcessedAnimation> animations = new List<ProcessedAnimation>();

            HashSet<string> encounteredNames = new HashSet<string>();
            for (uint meshIndex = 0; meshIndex < scene->MNumMeshes; meshIndex++)
            {
                Mesh* mesh = scene->MMeshes[meshIndex];
                string meshName = mesh->MName.AsString;
                if (string.IsNullOrEmpty(meshName))
                {
                    meshName = $"mesh_{meshIndex}";
                }
                int counter = 1;
                while (!encounteredNames.Add(meshName))
                {
                    meshName = mesh->MName.AsString + "_" + counter.ToString();
                    counter += 1;
                }
                uint vertexCount = mesh->MNumVertices;

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

                // Find the first non-null UV channel
                Vector3* texCoordPtr = null;
                for (int ch = 0; ch < 8; ch++)
                {
                    Vector3* tc = mesh->MTextureCoords[ch];
                    if (tc != null)
                    {
                        texCoordPtr = tc;
                        break;
                    }
                }
                bool hasTexCoords = texCoordPtr != null;
                elementDescs.Add(new VertexElementDescription("TexCoords", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2));
                texCoordsOffset = vertexSize;
                vertexSize += 8;

                bool hasBones = mesh->MNumBones > 0;
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
                Vector3 min = vertexCount > 0 ? mesh->MVertices[0] : Vector3.Zero;
                Vector3 max = vertexCount > 0 ? mesh->MVertices[0] : Vector3.Zero;

                for (uint i = 0; i < vertexCount; i++)
                {
                    Vector3 position = mesh->MVertices[i];
                    min = Vector3.Min(min, position);
                    max = Vector3.Max(max, position);

                    builder.WriteVertexElement(
                        (int)i,
                        positionOffset,
                        position);

                    Vector3 normal = mesh->MNormals[i];
                    builder.WriteVertexElement((int)i, normalOffset, normal);

                    if (hasTexCoords)
                    {
                        builder.WriteVertexElement(
                            (int)i,
                            texCoordsOffset,
                            new Vector2(texCoordPtr[i].X, texCoordPtr[i].Y));
                    }
                    else
                    {
                        builder.WriteVertexElement(
                            (int)i,
                            texCoordsOffset,
                            new Vector2());
                    }
                }

                List<int> indices = new List<int>();
                for (uint f = 0; f < mesh->MNumFaces; f++)
                {
                    Face face = mesh->MFaces[f];
                    if (face.MNumIndices == 3)
                    {
                        indices.Add((int)face.MIndices[0]);
                        indices.Add((int)face.MIndices[1]);
                        indices.Add((int)face.MIndices[2]);
                    }
                }

                Dictionary<string, uint> boneIDsByName = new Dictionary<string, uint>();
                Matrix4x4[] boneOffsets = new Matrix4x4[mesh->MNumBones];

                if (hasBones)
                {
                    Dictionary<int, int> assignedBoneWeights = new Dictionary<int, int>();
                    for (uint boneID = 0; boneID < mesh->MNumBones; boneID++)
                    {
                        Bone* bone = mesh->MBones[boneID];
                        string boneName = bone->MName.AsString;
                        int suffix = 1;
                        while (boneIDsByName.ContainsKey(boneName))
                        {
                            boneName = bone->MName.AsString + "_" + suffix.ToString();
                            suffix += 1;
                        }

                        boneIDsByName.Add(boneName, boneID);
                        for (uint w = 0; w < bone->MNumWeights; w++)
                        {
                            VertexWeight weight = bone->MWeights[w];
                            int relativeBoneIndex = GetAndIncrementRelativeBoneIndex(assignedBoneWeights, (int)weight.MVertexId);
                            builder.WriteVertexElement((int)weight.MVertexId, boneIndicesOffset + (relativeBoneIndex * sizeof(uint)), boneID);
                            builder.WriteVertexElement((int)weight.MVertexId, boneWeightOffset + (relativeBoneIndex * sizeof(float)), weight.MWeight);
                        }

                        // Transpose at I/O boundary, then decompose/recompose in System.Numerics convention
                        Matrix4x4 offsetMat = bone->MOffsetMatrix.ToSystemMatrix();
                        Matrix4x4.Decompose(offsetMat, out var scale, out var rot, out var trans);
                        offsetMat = Matrix4x4.CreateScale(scale)
                            * Matrix4x4.CreateFromQuaternion(rot)
                            * Matrix4x4.CreateTranslation(trans);

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
                        System.Buffer.MemoryCopy(int32Ptr, indexDataPtr, indexData.Length, indexData.Length);
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
            Node* rootNode = scene->MRootNode;
            List<ProcessedNode> processedNodes = new List<ProcessedNode>();
            ConvertNode(rootNode, -1, processedNodes);

            ProcessedNodeSet nodes = new ProcessedNodeSet(processedNodes.ToArray(), 0, rootNodeInverseTransform);

            for (uint animIndex = 0; animIndex < scene->MNumAnimations; animIndex++)
            {
                Animation* animation = scene->MAnimations[animIndex];
                Dictionary<string, ProcessedAnimationChannel> channels = new Dictionary<string, ProcessedAnimationChannel>();
                for (uint channelIndex = 0; channelIndex < animation->MNumChannels; channelIndex++)
                {
                    NodeAnim* nac = animation->MChannels[channelIndex];
                    channels[nac->MNodeName.AsString] = ConvertChannel(nac);
                }

                string baseAnimName = animation->MName.AsString;
                if (string.IsNullOrEmpty(baseAnimName))
                {
                    baseAnimName = "anim_" + animIndex;
                }

                string animationName = baseAnimName;

                int nameCounter = 1;
                while (!encounteredNames.Add(animationName))
                {
                    animationName = baseAnimName + "_" + nameCounter.ToString();
                    nameCounter += 1;
                }
            }

            _assimp.ReleaseImport(scene);

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

        private unsafe ProcessedAnimationChannel ConvertChannel(NodeAnim* nac)
        {
            string nodeName = nac->MNodeName.AsString;
            AssetPrimitives.VectorKey[] positions = new AssetPrimitives.VectorKey[nac->MNumPositionKeys];
            for (uint i = 0; i < nac->MNumPositionKeys; i++)
            {
                Silk.NET.Assimp.VectorKey assimpKey = nac->MPositionKeys[i];
                positions[i] = new AssetPrimitives.VectorKey(assimpKey.MTime, assimpKey.MValue);
            }

            AssetPrimitives.VectorKey[] scales = new AssetPrimitives.VectorKey[nac->MNumScalingKeys];
            for (uint i = 0; i < nac->MNumScalingKeys; i++)
            {
                Silk.NET.Assimp.VectorKey assimpKey = nac->MScalingKeys[i];
                scales[i] = new AssetPrimitives.VectorKey(assimpKey.MTime, assimpKey.MValue);
            }

            AssetPrimitives.QuaternionKey[] rotations = new AssetPrimitives.QuaternionKey[nac->MNumRotationKeys];
            for (uint i = 0; i < nac->MNumRotationKeys; i++)
            {
                Silk.NET.Assimp.QuatKey assimpKey = nac->MRotationKeys[i];
                rotations[i] = new AssetPrimitives.QuaternionKey(assimpKey.MTime, assimpKey.MValue.ToSystemQuaternion());
            }

            return new ProcessedAnimationChannel(nodeName, positions, scales, rotations);
        }

        private unsafe int ConvertNode(Node* node, int parentIndex, List<ProcessedNode> processedNodes)
        {
            int currentIndex = processedNodes.Count;
            int[] childIndices = new int[node->MNumChildren];
            // Transpose at I/O boundary: Assimp -> System.Numerics
            var nodeTransform = node->MTransformation.ToSystemMatrix();
            ProcessedNode pn = new ProcessedNode(node->MName.AsString, nodeTransform, parentIndex, childIndices);
            processedNodes.Add(pn);

            for (uint i = 0; i < node->MNumChildren; i++)
            {
                int childIndex = ConvertNode(node->MChildren[i], currentIndex, processedNodes);
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

    internal static class AssimpExtensions
    {
        /// <summary>
        /// Transposes a matrix from Assimp's column-vector convention to System.Numerics' row-vector convention.
        /// </summary>
        public static Matrix4x4 ToSystemMatrix(this Matrix4x4 assimpMatrix)
        {
            return Matrix4x4.Transpose(assimpMatrix);
        }

        public static Quaternion ToSystemQuaternion(this AssimpQuaternion q)
        {
            return new Quaternion(q.X, q.Y, q.Z, q.W);
        }
    }
}
