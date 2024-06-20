using System;
using System.Collections.Generic;
using System.IO;
using Veldrid;
using System.Linq;
using System.Runtime.InteropServices;

namespace Prowl.Runtime
{  
    public enum MeshResource
    {
        Position,
        UV0,
        UV1,
        Normals,
        Tangents,
        Colors,
        BoneIndices,
        BoneWeights,
        Custom
    }

    public class Mesh : EngineObject, ISerializable
    {
        /// <summary> Whether this mesh is readable by the CPU </summary>
        public readonly bool isReadable = true;

        /// <summary> Whether this mesh is writable </summary>
        public readonly bool isWritable = true;

        /// <summary> The bounds of the mesh </summary>
        public Bounds bounds { get; internal set; }

        /// <summary> The format of the indices for this mesh </summary>
        public IndexFormat IndexFormat {
            get => indexFormat;
            set {
                if (isWritable == false) return;
                changed = true;
                indexFormat = value;
                indices = [];
            }
        }

        /// <summary> The mesh's primitive type </summary>
        public PrimitiveTopology MeshTopology {
            get => meshTopology;
            set {
                if (isWritable == false) return;
                changed = true;
                meshTopology = value;
            }
        }

        /// <summary>
        /// Sets or gets the current vertices.
        /// Getting depends on isReadable.
        /// Note: When setting, if the vertex count is different than previous, it'll reset all other vertex data fields.
        /// </summary>
        public System.Numerics.Vector3[] Vertices {
            get => vertices ?? [];
            set {
                if (isWritable == false)
                    return;
                var needsReset = vertices == null || vertices.Length != value.Length;
                vertices = value;
                changed = true;
                if (needsReset)
                {
                    normals = null;
                    tangents = null;
                    colors = null;
                    colors32 = null;
                    uv = null;
                    uv2 = null;
                    indices = null;
                }
            }
        }

        public System.Numerics.Vector3[] Normals {
            get => ReadVertexData(normals ?? []);
            set => WriteVertexData(ref normals, value, value.Length);
        }

        public System.Numerics.Vector3[] Tangents {
            get => ReadVertexData(tangents ?? []);
            set => WriteVertexData(ref tangents, value, value.Length);
        }

        public Color[] Colors {
            get => ReadVertexData(colors ?? []);
            set => WriteVertexData(ref colors, value, value.Length);
        }

        public Color32[] Colors32 {
            get => ReadVertexData(colors32 ?? []);
            set => WriteVertexData(ref colors32, value, value.Length);
        }

        public System.Numerics.Vector2[] UV {
            get => ReadVertexData(uv ?? []);
            set => WriteVertexData(ref uv, value, value.Length);
        }

        public System.Numerics.Vector2[] UV2 {
            get => ReadVertexData(uv2 ?? []);
            set => WriteVertexData(ref uv2, value, value.Length);
        }

        public uint[] Indices {
            get => ReadVertexData(indices ?? []);
            set => WriteVertexData(ref indices, value, value.Length, false);
        }

        public System.Numerics.Vector4[] BoneIndices {
            get => ReadVertexData(boneIndices ?? []);
            set => WriteVertexData(ref boneIndices, value, value.Length);
        }

        public System.Numerics.Vector4[] BoneWeights {
            get => ReadVertexData(boneWeights ?? []);
            set => WriteVertexData(ref boneWeights, value, value.Length);
        }

        public int VertexCount => vertices?.Length ?? 0;
        public int IndexCount => indices?.Length ?? 0;

        public DeviceBuffer VertexBuffer => vertexBuffer;
        public DeviceBuffer IndexBuffer => indexBuffer;

        public bool HasNormals => (normals?.Length ?? 0) > 0;
        public bool HasTangents => (tangents?.Length ?? 0) > 0;
        public bool HasColors => (colors?.Length ?? 0) > 0;
        public bool HasColors32 => (colors32?.Length ?? 0) > 0;
        public bool HasUV => (uv?.Length ?? 0) > 0;
        public bool HasUV2 => (uv2?.Length ?? 0) > 0;

        public bool HasBoneIndices => (boneIndices?.Length ?? 0) > 0;
        public bool HasBoneWeights => (boneWeights?.Length ?? 0) > 0;

        public System.Numerics.Matrix4x4[]? bindPoses;

        bool changed = true;
        System.Numerics.Vector3[]? vertices;
        System.Numerics.Vector3[]? normals;
        System.Numerics.Vector3[]? tangents;
        Color[]? colors;
        Color32[]? colors32;
        System.Numerics.Vector2[]? uv;
        System.Numerics.Vector2[]? uv2;
        uint[]? indices;
        System.Numerics.Vector4[]? boneIndices;
        System.Numerics.Vector4[]? boneWeights;

        IndexFormat indexFormat = IndexFormat.UInt16;
        PrimitiveTopology meshTopology = PrimitiveTopology.TriangleList;


        DeviceBuffer vertexBuffer;
        DeviceBuffer indexBuffer;        
        int[] bufferOffsets;

        public int UVStart => bufferOffsets[0];
        public int UV2Start => bufferOffsets[1];
        public int NormalsStart => bufferOffsets[2];
        public int ColorsStart => bufferOffsets[3]; 
        public int TangentsStart => bufferOffsets[4];
        public int BoneIndexStart => bufferOffsets[5];
        public int BoneWeightStart => bufferOffsets[6];
        public int BufferLength => bufferOffsets[7];

        public Mesh() { }

        public void Clear()
        {
            vertices = null;
            normals = null;
            colors = null;
            colors32 = null;
            uv = null;
            uv2 = null;
            indices = null;
            tangents = null;
            boneIndices = null;
            boneWeights = null;

            changed = true;

            DeleteGPUBuffers();
        }

        public void Upload()
        {
            if (changed == false)
                return;

            changed = false;

            DeleteGPUBuffers();

            if (vertices == null || vertices.Length == 0)
                throw new InvalidOperationException($"Mesh has no vertices");

            if (indices == null || indices.Length == 0)
                throw new InvalidOperationException($"Mesh has no indices");

            switch (meshTopology)
            {
                case PrimitiveTopology.TriangleList:
                    if (indices.Length % 3 != 0)
                        throw new InvalidOperationException($"Triangle List mesh doesn't have the right amount of indices. Has: {indices.Length}. Should be a multiple of 3");
                    break;
                case PrimitiveTopology.TriangleStrip:
                    if (indices.Length < 3)
                        throw new InvalidOperationException($"Triangle Strip mesh doesn't have the right amount of indices. Has: {indices.Length}. Should have at least 3");
                    break;

                case PrimitiveTopology.LineList:
                    if (indices.Length % 2 != 0)
                        throw new InvalidOperationException($"Line List mesh doesn't have the right amount of indices. Has: {indices.Length}. Should be a multiple of 2");
                    break;

                case PrimitiveTopology.LineStrip:
                    if (indices.Length < 2)
                        throw new InvalidOperationException($"Line Strip mesh doesn't have the right amount of indices. Has: {indices.Length}. Should have at least 2");
                    break;
            }

            bufferOffsets = CalcOffsets();

            // Vertex buffer upload
            vertexBuffer = Graphics.ResourceFactory.CreateBuffer(new BufferDescription((uint)BufferLength, BufferUsage.VertexBuffer));

            Graphics.Device.UpdateBuffer(vertexBuffer, 0, vertices);
            
            if (HasUV)
                Graphics.Device.UpdateBuffer(vertexBuffer, (uint)UVStart, uv);

            if (HasUV2)
                Graphics.Device.UpdateBuffer(vertexBuffer, (uint)UV2Start, uv2);

            if (HasNormals)
                Graphics.Device.UpdateBuffer(vertexBuffer, (uint)NormalsStart, normals);

            if (HasColors)
                Graphics.Device.UpdateBuffer(vertexBuffer, (uint)ColorsStart, colors);
            else if (HasColors32)
                Graphics.Device.UpdateBuffer(vertexBuffer, (uint)ColorsStart, colors32.Select(x => (Color)x).ToArray());

            if (HasTangents)
                Graphics.Device.UpdateBuffer(vertexBuffer, (uint)TangentsStart, tangents);

            if (HasBoneIndices)
                Graphics.Device.UpdateBuffer(vertexBuffer, (uint)BoneIndexStart, boneIndices);

            if (HasBoneWeights)
                Graphics.Device.UpdateBuffer(vertexBuffer, (uint)BoneWeightStart, boneWeights);


            // Index buffer upload
            uint indexByteSize = (uint)(indexFormat == IndexFormat.UInt16 ? sizeof(ushort) : sizeof(uint));
            indexBuffer = Graphics.ResourceFactory.CreateBuffer(new BufferDescription((uint)indices.Length * indexByteSize, BufferUsage.IndexBuffer));

            if (indexFormat == IndexFormat.UInt16)
            {
                ushort[] data = new ushort[indices.Length];
                for (var i = 0; i < indices.Length; i++)
                {
                    if (indices[i] >= ushort.MaxValue)
                        throw new InvalidOperationException($"[Mesh] Invalid value {indices[i]} for 16-bit indices");
                    data[i] = (ushort)indices[i];
                }
                
                Graphics.Device.UpdateBuffer(indexBuffer, 0, data);
            }
            else if (indexFormat == IndexFormat.UInt32)
            {
                Graphics.Device.UpdateBuffer(indexBuffer, 0, indices);
            }
        }

        public void RecalculateBounds()
        {
            if (vertices == null)
                throw new ArgumentNullException();

            var empty = true;
            var minVec = System.Numerics.Vector3.One * 99999f;
            var maxVec = System.Numerics.Vector3.One * -99999f;
            foreach (var ptVector in vertices)
            {
                minVec.X = (minVec.X < ptVector.X) ? minVec.X : ptVector.X;
                minVec.Y = (minVec.Y < ptVector.Y) ? minVec.Y : ptVector.Y;
                minVec.Z = (minVec.Z < ptVector.Z) ? minVec.Z : ptVector.Z;

                maxVec.X = (maxVec.X > ptVector.X) ? maxVec.X : ptVector.X;
                maxVec.Y = (maxVec.Y > ptVector.Y) ? maxVec.Y : ptVector.Y;
                maxVec.Z = (maxVec.Z > ptVector.Z) ? maxVec.Z : ptVector.Z;

                empty = false;
            }
            if (empty)
                throw new ArgumentException();

            bounds = new Bounds(minVec, maxVec);
        }

        public void RecalculateNormals()
        {
            if (vertices == null || vertices.Length < 3) return;
            if (indices == null || indices.Length < 3) return;

            var normals = new System.Numerics.Vector3[vertices.Length];

            for (int i = 0; i < indices.Length; i += 3)
            {
                uint ai = indices[i];
                uint bi = indices[i + 1];
                uint ci = indices[i + 2];

                System.Numerics.Vector3 n = System.Numerics.Vector3.Normalize(System.Numerics.Vector3.Cross(
                    vertices[bi] - vertices[ai],
                    vertices[ci] - vertices[ai]
                ));

                normals[ai] += n;
                normals[bi] += n;
                normals[ci] += n;
            }

            for (int i = 0; i < vertices.Length; i++)
                normals[i] = -System.Numerics.Vector3.Normalize(normals[i]);

            Normals = normals;
        }

        public void RecalculateTangents()
        {
            if (vertices == null || vertices.Length < 3) return;
            if (indices == null || indices.Length < 3) return;
            if (uv == null) return;

            var tangents = new System.Numerics.Vector3[vertices.Length];

            for (int i = 0; i < indices.Length; i += 3)
            {
                uint ai = indices[i];
                uint bi = indices[i + 1];
                uint ci = indices[i + 2];

                System.Numerics.Vector3 edge1 = vertices[bi] - vertices[ai];
                System.Numerics.Vector3 edge2 = vertices[ci] - vertices[ai];

                System.Numerics.Vector2 deltaUV1 = uv[bi] - uv[ai];
                System.Numerics.Vector2 deltaUV2 = uv[ci] - uv[ai];

                float f = 1.0f / (deltaUV1.X * deltaUV2.Y - deltaUV2.X * deltaUV1.Y);

                System.Numerics.Vector3 tangent;
                tangent.X = f * (deltaUV2.Y * edge1.X - deltaUV1.Y * edge2.X);
                tangent.Y = f * (deltaUV2.Y * edge1.Y - deltaUV1.Y * edge2.Y);
                tangent.Z = f * (deltaUV2.Y * edge1.Z - deltaUV1.Y * edge2.Z);

                tangents[ai] += tangent;
                tangents[bi] += tangent;
                tangents[ci] += tangent;
            }

            for (int i = 0; i < vertices.Length; i++)
                tangents[i] = System.Numerics.Vector3.Normalize(tangents[i]);

            Tangents = tangents;
        }

        public override void OnDispose() => DeleteGPUBuffers();

        private static Mesh fullScreenQuad;
        public static Mesh GetFullscreenQuad()
        {
            if (fullScreenQuad != null) return fullScreenQuad;
            Mesh mesh = new Mesh();
            mesh.vertices = new System.Numerics.Vector3[4];
            mesh.vertices[0] = new System.Numerics.Vector3(-1, -1, 0);
            mesh.vertices[1] = new System.Numerics.Vector3(1, -1, 0);
            mesh.vertices[2] = new System.Numerics.Vector3(-1, 1, 0);
            mesh.vertices[3] = new System.Numerics.Vector3(1, 1, 0);

            mesh.uv = new System.Numerics.Vector2[4];
            mesh.uv[0] = new System.Numerics.Vector2(0, 0);
            mesh.uv[1] = new System.Numerics.Vector2(1, 0);
            mesh.uv[2] = new System.Numerics.Vector2(0, 1);
            mesh.uv[3] = new System.Numerics.Vector2(1, 1);

            mesh.indices = [0, 2, 1, 2, 3, 1];

            fullScreenQuad = mesh;
            return mesh;
        }

        public static Mesh CreateSphere(float radius, int rings, int slices)
        {
            Mesh mesh = new Mesh();

            List<System.Numerics.Vector3> vertices = new List<System.Numerics.Vector3>();
            List<System.Numerics.Vector2> uvs = new List<System.Numerics.Vector2>();
            List<uint> indices = new List<uint>();

            for (int i = 0; i <= rings; i++)
            {
                float v = 1 - (float)i / rings;
                float phi = v * MathF.PI;

                for (int j = 0; j <= slices; j++)
                {
                    float u = (float)j / slices;
                    float theta = u * MathF.PI * 2;

                    float x = MathF.Sin(phi) * MathF.Cos(theta);
                    float y = MathF.Cos(phi);
                    float z = MathF.Sin(phi) * MathF.Sin(theta);

                    vertices.Add(new System.Numerics.Vector3(x, y, z) * radius);
                    uvs.Add(new System.Numerics.Vector2(u, v));
                }
            }

            for (int i = 0; i < rings; i++)
            {
                for (int j = 0; j < slices; j++)
                {
                    uint a = (uint)(i * (slices + 1) + j);
                    uint b = (uint)(a + slices + 1);

                    indices.Add(a);
                    indices.Add(b);
                    indices.Add(a + 1);

                    indices.Add(b);
                    indices.Add(b + 1);
                    indices.Add(a + 1);
                }
            }

            mesh.vertices = vertices.ToArray();
            mesh.uv = uvs.ToArray();
            mesh.indices = indices.ToArray();

            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();

            return mesh;
        }

        private void DeleteGPUBuffers()
        {
            vertexBuffer?.Dispose();
            vertexBuffer = null;
            indexBuffer?.Dispose();
            indexBuffer = null;
        }

        private T ReadVertexData<T>(T value)
        {
            if (isReadable == false)
                throw new InvalidOperationException("Mesh is not readable");
            return value;
        }

        private void WriteVertexData<T>(ref T target, T value, int length, bool mustMatchLength = true)
        {
            if (isWritable == false)
                throw new InvalidOperationException("Mesh is not writable");
            if ((value == null || length == 0 || length != (vertices?.Length ?? 0)) && mustMatchLength)
                throw new ArgumentException("Array length should match vertices length");
            changed = true;
            target = value;
        }

        internal int[] CalcOffsets()
        {
            int floatSize = sizeof(float);

            int vec2Size = floatSize * 2;
            int vec3Size = floatSize * 3;
            int vec4Size = floatSize * 4;

            int[] offsets = new int[8];

            int vertLen = vertices.Length;

            offsets[0] = vertLen * vec3Size; // Where vertices end
            offsets[1] = offsets[0] + (HasUV ? vertLen * vec2Size : 0); // Where UV0 ends
            offsets[2] = offsets[1] + (HasUV2 ? vertLen * vec2Size : 0); // Where UV1 ends
            offsets[3] = offsets[2] + (HasNormals ? vertLen * vec3Size : 0); // Where Normals end
            offsets[4] = offsets[3] + (HasColors ? vertLen * vec4Size : 0); // Where Colors end
            offsets[5] = offsets[4] + (HasTangents ? vertLen * vec3Size : 0); // Where Tangents end
            offsets[6] = offsets[5] + (HasBoneIndices ? vertLen * vec4Size : 0); // Where bone indices end
            offsets[7] = offsets[6] + (HasBoneWeights ? vertLen * vec4Size : 0); // Where bone weights end

            return offsets;
        }

        public static VertexLayoutDescription GetLayoutForResource(MeshResource resource, VertexLayoutDescription layout)
        {
            return resource switch 
            {
                MeshResource.Position => new VertexLayoutDescription(new VertexElementDescription("POSITION", VertexElementFormat.Float3, VertexElementSemantic.Position)),
                MeshResource.UV0 => new VertexLayoutDescription(new VertexElementDescription("TEXCOORD0", VertexElementFormat.Float2, VertexElementSemantic.TextureCoordinate)),
                MeshResource.UV1 => new VertexLayoutDescription(new VertexElementDescription("TEXCOORD1", VertexElementFormat.Float2, VertexElementSemantic.TextureCoordinate)),
                MeshResource.Normals => new VertexLayoutDescription(new VertexElementDescription("NORMAL", VertexElementFormat.Float3, VertexElementSemantic.Normal)),
                MeshResource.Tangents => new VertexLayoutDescription(new VertexElementDescription("TANGENT", VertexElementFormat.Float3, VertexElementSemantic.Normal)),
                MeshResource.Colors => new VertexLayoutDescription(new VertexElementDescription("COLOR", VertexElementFormat.Float4, VertexElementSemantic.Color)),
                MeshResource.BoneIndices => new VertexLayoutDescription(new VertexElementDescription("BONEINDEX", VertexElementFormat.Float4, VertexElementSemantic.Position)),
                MeshResource.BoneWeights => new VertexLayoutDescription(new VertexElementDescription("BONEWEIGHT", VertexElementFormat.Float4, VertexElementSemantic.Color)),
                MeshResource.Custom => layout,
            };
        }


        public SerializedProperty Serialize(Serializer.SerializationContext ctx)
        {
            var compoundTag = SerializedProperty.NewCompound();

            using (MemoryStream memoryStream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(memoryStream))
            {
                writer.Write((byte)indexFormat);
                writer.Write((byte)meshTopology);

                writer.Write(vertices.Length);
                foreach (var vertex in vertices)
                {
                    writer.Write(vertex.X);
                    writer.Write(vertex.Y);
                    writer.Write(vertex.Z);
                }

                writer.Write(normals?.Length ?? 0);
                if (normals != null)
                {
                    foreach (var normal in normals)
                    {
                        writer.Write(normal.X);
                        writer.Write(normal.Y);
                        writer.Write(normal.Z);
                    }
                }

                writer.Write(tangents?.Length ?? 0);
                if (tangents != null)
                {
                    foreach (var tangent in tangents)
                    {
                        writer.Write(tangent.X);
                        writer.Write(tangent.Y);
                        writer.Write(tangent.Z);
                    }
                }

                writer.Write(colors?.Length ?? 0);
                if (colors != null)
                {
                    foreach (var color in colors)
                    {
                        writer.Write(color.r);
                        writer.Write(color.g);
                        writer.Write(color.b);
                        writer.Write(color.a);
                    }
                }

                writer.Write(colors32?.Length ?? 0);
                if (colors32 != null)
                {
                    foreach (var color in colors32)
                    {
                        writer.Write(color.r);
                        writer.Write(color.g);
                        writer.Write(color.b);
                        writer.Write(color.a);
                    }
                }

                writer.Write(uv?.Length ?? 0);
                if (uv != null)
                {
                    foreach (var uv in uv)
                    {
                        writer.Write(uv.X);
                        writer.Write(uv.Y);
                    }
                }

                writer.Write(uv2?.Length ?? 0);
                if (uv2 != null)
                {
                    foreach (var uv in uv2)
                    {
                        writer.Write(uv.X);
                        writer.Write(uv.Y);
                    }
                }

                writer.Write(indices?.Length ?? 0);
                if (indices != null)
                {
                    foreach (var index in indices)
                        writer.Write(index);
                }

                writer.Write(boneIndices?.Length ?? 0);
                if (boneIndices != null)
                {
                    foreach (var boneIndex in boneIndices)
                    {
                        //writer.Write(boneIndex.red);
                        //writer.Write(boneIndex.green);
                        //writer.Write(boneIndex.blue);
                        //writer.Write(boneIndex.alpha);
                        writer.Write(boneIndex.X);
                        writer.Write(boneIndex.Y);
                        writer.Write(boneIndex.Z);
                        writer.Write(boneIndex.W);
                    }
                }

                writer.Write(boneWeights?.Length ?? 0);
                if (boneWeights != null)
                {
                    foreach (var boneWeight in boneWeights)
                    {
                        writer.Write(boneWeight.X);
                        writer.Write(boneWeight.Y);
                        writer.Write(boneWeight.Z);
                        writer.Write(boneWeight.W);
                    }
                }

                writer.Write(bindPoses?.Length ?? 0);
                if (bindPoses != null)
                {
                    foreach (var bindPose in bindPoses)
                    {
                        writer.Write(bindPose.M11);
                        writer.Write(bindPose.M12);
                        writer.Write(bindPose.M13);
                        writer.Write(bindPose.M14);

                        writer.Write(bindPose.M21);
                        writer.Write(bindPose.M22);
                        writer.Write(bindPose.M23);
                        writer.Write(bindPose.M24);

                        writer.Write(bindPose.M31);
                        writer.Write(bindPose.M32);
                        writer.Write(bindPose.M33);
                        writer.Write(bindPose.M34);

                        writer.Write(bindPose.M41);
                        writer.Write(bindPose.M42);
                        writer.Write(bindPose.M43);
                        writer.Write(bindPose.M44);
                    }
                }


                compoundTag.Add("MeshData", new SerializedProperty(memoryStream.ToArray()));
            }

            return compoundTag;
        }

        public void Deserialize(SerializedProperty value, Serializer.SerializationContext ctx)
        {
            using (MemoryStream memoryStream = new MemoryStream(value["MeshData"].ByteArrayValue))
            using (BinaryReader reader = new BinaryReader(memoryStream))
            {
                indexFormat = (IndexFormat)reader.ReadByte();
                meshTopology = (PrimitiveTopology)reader.ReadByte();

                var vertexCount = reader.ReadInt32();
                vertices = new System.Numerics.Vector3[vertexCount];
                for (int i = 0; i < vertexCount; i++)
                    vertices[i] = new System.Numerics.Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

                var normalCount = reader.ReadInt32();
                if (normalCount > 0)
                {
                    normals = new System.Numerics.Vector3[normalCount];
                    for (int i = 0; i < normalCount; i++)
                        normals[i] = new System.Numerics.Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                }

                var tangentCount = reader.ReadInt32();
                if (tangentCount > 0)
                {
                    tangents = new System.Numerics.Vector3[tangentCount];
                    for (int i = 0; i < tangentCount; i++)
                        tangents[i] = new System.Numerics.Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                }

                var colorCount = reader.ReadInt32();
                if (colorCount > 0)
                {
                    colors = new Color[colorCount];
                    for (int i = 0; i < colorCount; i++)
                        colors[i] = new Color(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                }

                var color32Count = reader.ReadInt32();
                if (color32Count > 0)
                {
                    colors32 = new Color32[color32Count];
                    for (int i = 0; i < color32Count; i++)
                        colors32[i] = new Color32(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte());
                }

                var uvCount = reader.ReadInt32();
                if (uvCount > 0)
                {
                    uv = new System.Numerics.Vector2[uvCount];
                    for (int i = 0; i < uvCount; i++)
                        uv[i] = new System.Numerics.Vector2(reader.ReadSingle(), reader.ReadSingle());
                }

                var uv2Count = reader.ReadInt32();
                if (uv2Count > 0)
                {
                    uv2 = new System.Numerics.Vector2[uv2Count];
                    for (int i = 0; i < uv2Count; i++)
                        uv2[i] = new System.Numerics.Vector2(reader.ReadSingle(), reader.ReadSingle());
                }

                var indexCount = reader.ReadInt32();
                if (indexCount > 0)
                {
                    indices = new uint[indexCount];
                    for (int i = 0; i < indexCount; i++)
                        indices[i] = reader.ReadUInt32();
                }

                var boneIndexCount = reader.ReadInt32();
                if (boneIndexCount > 0)
                {
                    boneIndices = new System.Numerics.Vector4[boneIndexCount];
                    for(int i = 0; i < boneIndexCount; i++)
                    {
                        //boneIndices[i] = new Color32(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte());
                        boneIndices[i] = new System.Numerics.Vector4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    }
                }

                var boneWeightCount = reader.ReadInt32();
                if (boneWeightCount > 0)
                {
                    boneWeights = new System.Numerics.Vector4[boneWeightCount];
                    for (int i = 0; i < boneWeightCount; i++)
                        boneWeights[i] = new System.Numerics.Vector4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                }

                var bindPosesCount = reader.ReadInt32();
                if (bindPosesCount > 0)
                {
                    bindPoses = new System.Numerics.Matrix4x4[bindPosesCount];
                    for (int i = 0; i < bindPosesCount; i++)
                    {
                        bindPoses[i] = new System.Numerics.Matrix4x4() {
                            M11 = reader.ReadSingle(),
                            M12 = reader.ReadSingle(),
                            M13 = reader.ReadSingle(),
                            M14 = reader.ReadSingle(),

                            M21 = reader.ReadSingle(),
                            M22 = reader.ReadSingle(),
                            M23 = reader.ReadSingle(),
                            M24 = reader.ReadSingle(),

                            M31 = reader.ReadSingle(),
                            M32 = reader.ReadSingle(),
                            M33 = reader.ReadSingle(),
                            M34 = reader.ReadSingle(),

                            M41 = reader.ReadSingle(),
                            M42 = reader.ReadSingle(),
                            M43 = reader.ReadSingle(),
                            M44 = reader.ReadSingle()
                        };
                    }
                }

                changed = true;
            }
        }
    }
}