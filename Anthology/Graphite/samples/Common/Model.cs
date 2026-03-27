using Silk.NET.Assimp;
using System;
using System.IO;
using System.Numerics;
using Veldrid;

namespace Common
{
    public unsafe class Model
    {
        private const uint DefaultPostProcessSteps =
            (uint)(PostProcessSteps.FlipWindingOrder | PostProcessSteps.Triangulate | PostProcessSteps.PreTransformVertices
            | PostProcessSteps.CalculateTangentSpace | PostProcessSteps.GenerateSmoothNormals);

        private static readonly Assimp _assimp = SampleBase.AssimpHelper.GetApi();

        public DeviceBuffer VertexBuffer { get; private set; }
        public DeviceBuffer IndexBuffer { get; private set; }
        public IndexFormat IndexFormat { get; private set; } = IndexFormat.UInt32;
        public uint IndexCount { get; private set; }
        public uint VertexCount { get; private set; }

        public Model(
            GraphicsDevice gd,
            ResourceFactory factory,
            string filename,
            VertexElementSemantic[] elementSemantics,
            ModelCreateInfo? createInfo,
            uint flags = DefaultPostProcessSteps)
        {
            using (FileStream fs = System.IO.File.OpenRead(filename))
            {
                string extension = Path.GetExtension(filename);
                Init(gd, factory, fs, extension, elementSemantics, createInfo, flags);
            }
        }

        public Model(
            GraphicsDevice gd,
            ResourceFactory factory,
            Stream stream,
            string extension,
            VertexElementSemantic[] elementSemantics,
            ModelCreateInfo? createInfo,
            uint flags = DefaultPostProcessSteps)
        {
            Init(gd, factory, stream, extension, elementSemantics, createInfo, flags);
        }

        private void Init(
            GraphicsDevice gd,
            ResourceFactory factory,
            Stream stream,
            string extension,
            VertexElementSemantic[] elementSemantics,
            ModelCreateInfo? createInfo,
            uint flags = DefaultPostProcessSteps)
        {
            byte[] modelBytes;
            using (MemoryStream ms = new MemoryStream())
            {
                stream.CopyTo(ms);
                modelBytes = ms.ToArray();
            }

            Scene* pScene;
            fixed (byte* pBuffer = modelBytes)
            {
                pScene = _assimp.ImportFileFromMemory(pBuffer, (uint)modelBytes.Length, flags, (byte*)null);
            }

            if (pScene == null)
            {
                throw new InvalidOperationException("Failed to load model: " + _assimp.GetErrorStringS());
            }

            parts.Clear();
            parts.Count = pScene->MNumMeshes;

            Vector3 scale = new Vector3(1.0f);
            Vector2 uvscale = new Vector2(1.0f);
            Vector3 center = new Vector3(0.0f);
            if (createInfo != null)
            {
                scale = createInfo.Value.Scale;
                uvscale = createInfo.Value.UVScale;
                center = createInfo.Value.Center;
            }

            RawList<float> vertices = new RawList<float>();
            RawList<uint> indices = new RawList<uint>();

            VertexCount = 0;
            IndexCount = 0;

            for (uint i = 0; i < pScene->MNumMeshes; i++)
            {
                var paiMesh = pScene->MMeshes[i];

                parts[i] = new ModelPart();
                parts[i].vertexBase = VertexCount;
                parts[i].indexBase = IndexCount;

                VertexCount += paiMesh->MNumVertices;

                var pMaterial = pScene->MMaterials[paiMesh->MMaterialIndex];
                Vector4 diffuseColor = Vector4.One;
                _assimp.GetMaterialColor(pMaterial, Assimp.MaterialColorDiffuseBase, 0, 0, ref diffuseColor);

                Vector3 Zero3D = new Vector3(0.0f, 0.0f, 0.0f);

                // Find the first non-null UV channel (Assimp 6.x preserves COLLADA set= index)
                Vector3* texCoordPtr = null;
                for (int ch = 0; ch < 8; ch++)
                {
                    Vector3* tc = paiMesh->MTextureCoords[ch];
                    if (tc != null) { texCoordPtr = tc; break; }
                }
                bool hasTexCoords = texCoordPtr != null;
                bool hasTangents = paiMesh->MTangents != null;

                for (uint j = 0; j < paiMesh->MNumVertices; j++)
                {
                    Vector3 pPos = paiMesh->MVertices[j];
                    Vector3 pNormal = paiMesh->MNormals[j];
                    Vector3 pTexCoord = hasTexCoords ? texCoordPtr[j] : Zero3D;
                    Vector3 pTangent = hasTangents ? paiMesh->MTangents[j] : Zero3D;
                    Vector3 pBiTangent = hasTangents ? paiMesh->MBitangents[j] : Zero3D;

                    foreach (VertexElementSemantic component in elementSemantics)
                    {
                        switch (component)
                        {
                            case VertexElementSemantic.Position:
                                vertices.Add(pPos.X * scale.X + center.X);
                                vertices.Add(-pPos.Y * scale.Y + center.Y);
                                vertices.Add(pPos.Z * scale.Z + center.Z);
                                break;
                            case VertexElementSemantic.Normal:
                                vertices.Add(pNormal.X);
                                vertices.Add(-pNormal.Y);
                                vertices.Add(pNormal.Z);
                                break;
                            case VertexElementSemantic.TextureCoordinate:
                                vertices.Add(pTexCoord.X * uvscale.X);
                                vertices.Add(pTexCoord.Y * uvscale.Y);
                                break;
                            case VertexElementSemantic.Color:
                                vertices.Add(diffuseColor.X);
                                vertices.Add(diffuseColor.Y);
                                vertices.Add(diffuseColor.Z);
                                break;
                            default: throw new System.NotImplementedException();
                        };
                    }

                    dim.Max.X = Math.Max(pPos.X, dim.Max.X);
                    dim.Max.Y = Math.Max(pPos.Y, dim.Max.Y);
                    dim.Max.Z = Math.Max(pPos.Z, dim.Max.Z);

                    dim.Min.X = Math.Min(pPos.X, dim.Min.X);
                    dim.Min.Y = Math.Min(pPos.Y, dim.Min.Y);
                    dim.Min.Z = Math.Min(pPos.Z, dim.Min.Z);
                }

                dim.Size = dim.Max - dim.Min;

                parts[i].vertexCount = paiMesh->MNumVertices;

                uint indexBase = indices.Count;
                for (uint j = 0; j < paiMesh->MNumFaces; j++)
                {
                    Face face = paiMesh->MFaces[j];
                    if (face.MNumIndices != 3)
                        continue;
                    indices.Add(indexBase + face.MIndices[0]);
                    indices.Add(indexBase + face.MIndices[1]);
                    indices.Add(indexBase + face.MIndices[2]);
                    parts[i].indexCount += 3;
                    IndexCount += 3;
                }
            }

            _assimp.ReleaseImport(pScene);

            uint vBufferSize = (vertices.Count) * sizeof(float);
            uint iBufferSize = (indices.Count) * sizeof(uint);

            VertexBuffer = factory.CreateBuffer(new BufferDescription(vBufferSize, BufferUsage.VertexBuffer));
            IndexBuffer = factory.CreateBuffer(new BufferDescription(iBufferSize, BufferUsage.IndexBuffer));

            gd.UpdateBuffer(VertexBuffer, 0, ref vertices[0], vBufferSize);
            gd.UpdateBuffer(IndexBuffer, 0, ref indices[0], iBufferSize);
        }

        public struct ModelPart
        {
            public uint vertexBase;
            public uint vertexCount;
            public uint indexBase;
            public uint indexCount;
        }

        RawList<ModelPart> parts = new RawList<ModelPart>();

        public struct Dimension
        {
            public Vector3 Min;
            public Vector3 Max;
            public Vector3 Size;
            public Dimension(Vector3 min, Vector3 max) { Min = min; Max = max; Size = new Vector3(); }
        }

        public Dimension dim = new Dimension(new Vector3(float.MaxValue), new Vector3(float.MinValue));

        public struct ModelCreateInfo
        {
            public Vector3 Center;
            public Vector3 Scale;
            public Vector2 UVScale;

            public ModelCreateInfo(Vector3 scale, Vector2 uvScale, Vector3 center)
            {
                Center = center;
                Scale = scale;
                UVScale = uvScale;
            }

            public ModelCreateInfo(float scale, float uvScale, float center)
            {
                Center = new Vector3(center);
                Scale = new Vector3(scale);
                UVScale = new Vector2(uvScale);
            }
        }
    }
}
