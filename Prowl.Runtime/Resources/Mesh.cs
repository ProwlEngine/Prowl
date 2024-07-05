using Prowl.Runtime.Rendering;
using Prowl.Runtime.Rendering.Primitives;
using System;
using System.Collections.Generic;
using System.IO;
using static VertexFormat;

namespace Prowl.Runtime
{
    public enum IndexFormat : byte
    {
        UInt16 = 0,
        UInt32 = 1
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
                indices = new uint[0];
            }
        }

        /// <summary> The mesh's primitive type </summary>
        public Topology MeshTopology {
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
            get => vertices ?? new System.Numerics.Vector3[0];
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
            get => ReadVertexData(normals ?? new System.Numerics.Vector3[0]);
            set => WriteVertexData(ref normals, value, value.Length);
        }

        public System.Numerics.Vector3[] Tangents {
            get => ReadVertexData(tangents ?? new System.Numerics.Vector3[0]);
            set => WriteVertexData(ref tangents, value, value.Length);
        }

        public Color[] Colors {
            get => ReadVertexData(colors ?? new Color[0]);
            set => WriteVertexData(ref colors, value, value.Length);
        }

        public Color32[] Colors32 {
            get => ReadVertexData(colors32 ?? new Color32[0]);
            set => WriteVertexData(ref colors32, value, value.Length);
        }

        public System.Numerics.Vector2[] UV {
            get => ReadVertexData(uv ?? new System.Numerics.Vector2[0]);
            set => WriteVertexData(ref uv, value, value.Length);
        }

        public System.Numerics.Vector2[] UV2 {
            get => ReadVertexData(uv2 ?? new System.Numerics.Vector2[0]);
            set => WriteVertexData(ref uv2, value, value.Length);
        }

        public uint[] Indices {
            get => ReadVertexData(indices ?? new uint[0]);
            set => WriteVertexData(ref indices, value, value.Length, false);
        }

        public System.Numerics.Vector4[] BoneIndices {
            get => ReadVertexData(boneIndices ?? new System.Numerics.Vector4[0]);
            set => WriteVertexData(ref boneIndices, value, value.Length);
        }

        public System.Numerics.Vector4[] BoneWeights {
            get => ReadVertexData(boneWeights ?? new System.Numerics.Vector4[0]);
            set => WriteVertexData(ref boneWeights, value, value.Length);
        }

        public int VertexCount => vertices?.Length ?? 0;
        public int IndexCount => indices?.Length ?? 0;

        public GraphicsVertexArray? VertexArrayObject => vertexArrayObject;
        public GraphicsBuffer VertexBuffer => vertexBuffer;
        public GraphicsBuffer IndexBuffer => indexBuffer;

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
        Topology meshTopology = Topology.TriangleStrip;

        GraphicsVertexArray? vertexArrayObject;
        GraphicsBuffer vertexBuffer;
        GraphicsBuffer indexBuffer;

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
            if (changed == false && vertexArrayObject != null)
                return;

            changed = false;

            DeleteGPUBuffers();

            if (vertices == null || vertices.Length == 0)
                throw new InvalidOperationException($"Mesh has no vertices");

            if (indices == null || indices.Length == 0)
                throw new InvalidOperationException($"Mesh has no indices");

            switch (meshTopology)
            {
                case Topology.Triangles:
                    if (indices.Length % 3 != 0)
                        throw new InvalidOperationException($"Triangle mesh doesn't have the right amount of indices. Has: {indices.Length}. Should be a multiple of 3");
                    break;
                case Topology.TriangleStrip:
                    if (indices.Length < 3)
                        throw new InvalidOperationException($"Triangle Strip mesh doesn't have the right amount of indices. Has: {indices.Length}. Should have at least 3");
                    break;

                case Topology.Lines:
                    if (indices.Length % 2 != 0)
                        throw new InvalidOperationException($"Line mesh doesn't have the right amount of indices. Has: {indices.Length}. Should be a multiple of 2");
                    break;

                case Topology.LineStrip:
                    if (indices.Length < 2)
                        throw new InvalidOperationException($"Line Strip mesh doesn't have the right amount of indices. Has: {indices.Length}. Should have at least 2");
                    break;
            }

            var layout = GetVertexLayout(this);

            if (layout == null)
            {
                Debug.LogError($"[Mesh] Failed to get vertex layout for this mesh!");
                return;
            }

            var vertexBlob = MakeVertexDataBlob(layout);
            if (vertexBlob == null)
                return;

            vertexBuffer = Graphics.Device.CreateBuffer(BufferType.VertexBuffer, vertexBlob, false);

            if (indexFormat == IndexFormat.UInt16)
            {
                ushort[] data = new ushort[indices.Length];
                for (var i = 0; i < indices.Length; i++)
                {
                    if (indices[i] >= ushort.MaxValue)
                        throw new InvalidOperationException($"[Mesh] Invalid value {indices[i]} for 16-bit indices");
                    data[i] = (ushort)indices[i];
                }
                indexBuffer = Graphics.Device.CreateBuffer(BufferType.ElementsBuffer, data, false);
            }
            else if (indexFormat == IndexFormat.UInt32)
            {
                indexBuffer = Graphics.Device.CreateBuffer(BufferType.ElementsBuffer, indices, false);
            }

            vertexArrayObject = Graphics.Device.CreateVertexArray(layout, vertexBuffer, indexBuffer);

            Debug.Log($"VAO: [ID {vertexArrayObject}] Mesh uploaded successfully to VRAM (GPU)");

            Graphics.Device.BindVertexArray(null);
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
                normals[i] = -Vector3.Normalize(normals[i]);

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
                tangents[i] = Vector3.Normalize(tangents[i]);

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

        public static Mesh CreateCube(Vector3 size)
        {
            Mesh mesh = new Mesh();
            float x = (float)size.x / 2f;
            float y = (float)size.y / 2f;
            float z = (float)size.z / 2f;

            System.Numerics.Vector3[] vertices =
            {
                // Front face
                new(-x, -y, z), new(x, -y, z), new(x, y, z), new(-x, y, z),
                
                // Back face
                new(-x, -y, -z), new(x, -y, -z), new(x, y, -z), new(-x, y, -z),
                
                // Left face
                new(-x, -y, -z), new(-x, y, -z), new(-x, y, z), new(-x, -y, z),
                
                // Right face
                new(x, -y, z), new(x, y, z), new(x, y, -z), new(x, -y, -z),
                
                // Top face
                new(-x, y, z), new(x, y, z), new(x, y, -z), new(-x, y, -z),
                
                // Bottom face
                new(-x, -y, -z), new(x, -y, -z), new(x, -y, z), new(-x, -y, z)
            };

            System.Numerics.Vector2[] uvs =
            {
                // Front face
                new(0, 0), new(1, 0), new(1, 1), new(0, 1),
                // Back face
                new(1, 0), new(0, 0), new(0, 1), new(1, 1),
                // Left face
                new(0, 0), new(1, 0), new(1, 1), new(0, 1),
                // Right face
                new(1, 0), new(1, 1), new(0, 1), new(0, 0),
                // Top face
                new(0, 1), new(1, 1), new(1, 0), new(0, 0),
                // Bottom face
                new(0, 0), new(1, 0), new(1, 1), new(0, 1)
            };

            uint[] indices =
            {
                1, 2, 0, 0, 2, 3,       // Front face
                5, 4, 6, 6, 4, 7,       // Back face
                9, 8, 10, 10, 8, 11,    // Left face
                13, 12, 14, 14, 12, 15, // Right face
                17, 18, 16, 16, 18, 19, // Top face
                21, 22, 20, 20, 22, 23  // Bottom face
            };

            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.indices = indices;

            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();

            return mesh;
        }

        public static Mesh CreateCylinder(float radius, float length, int sliceCount)
        {
#warning TODO: Test, This hasent been tested like at all just assumed it will work
            Mesh mesh = new Mesh();

            List<System.Numerics.Vector3> vertices = new List<System.Numerics.Vector3>();
            List<System.Numerics.Vector2> uvs = new List<System.Numerics.Vector2>();
            List<uint> indices = new List<uint>();

            float halfLength = length / 2.0f;

            // Create the vertices and UVs for the top and bottom circles
            for (int i = 0; i <= sliceCount; i++)
            {
                float angle = 2 * MathF.PI * i / sliceCount;
                float x = radius * MathF.Cos(angle);
                float z = radius * MathF.Sin(angle);

                // Top circle
                vertices.Add(new System.Numerics.Vector3(x, halfLength, z));
                uvs.Add(new System.Numerics.Vector2((float)i / sliceCount, 1));

                // Bottom circle
                vertices.Add(new System.Numerics.Vector3(x, -halfLength, z));
                uvs.Add(new System.Numerics.Vector2((float)i / sliceCount, 0));
            }

            // Add the center vertices for the top and bottom circles
            vertices.Add(new System.Numerics.Vector3(0, halfLength, 0));
            uvs.Add(new System.Numerics.Vector2(0.5f, 1));
            vertices.Add(new System.Numerics.Vector3(0, -halfLength, 0));
            uvs.Add(new System.Numerics.Vector2(0.5f, 0));

            int topCenterIndex = vertices.Count - 2;
            int bottomCenterIndex = vertices.Count - 1;

            // Create the indices for the sides of the cylinder
            for (int i = 0; i < sliceCount; i++)
            {
                int top1 = i * 2;
                int top2 = top1 + 2;
                int bottom1 = top1 + 1;
                int bottom2 = top2 + 1;

                if (i == sliceCount - 1)
                {
                    top2 = 0;
                    bottom2 = 1;
                }

                indices.Add((uint)top1);
                indices.Add((uint)bottom1);
                indices.Add((uint)top2);

                indices.Add((uint)bottom1);
                indices.Add((uint)bottom2);
                indices.Add((uint)top2);
            }

            // Create the indices for the top and bottom circles
            for (int i = 0; i < sliceCount; i++)
            {
                int top1 = i * 2;
                int top2 = (i == sliceCount - 1) ? 0 : top1 + 2;
                int bottom1 = top1 + 1;
                int bottom2 = (i == sliceCount - 1) ? 1 : bottom1 + 2;

                // Top circle
                indices.Add((uint)top1);
                indices.Add((uint)top2);
                indices.Add((uint)topCenterIndex);

                // Bottom circle
                indices.Add((uint)bottom2);
                indices.Add((uint)bottom1);
                indices.Add((uint)bottomCenterIndex);
            }

            mesh.vertices = vertices.ToArray();
            mesh.uv = uvs.ToArray();
            mesh.indices = indices.ToArray();

            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();

            return mesh;
        }

        public static Mesh CreateTriangle(Vector3 a, Vector3 b, Vector3 c)
        {
            Mesh mesh = new Mesh();
            mesh.vertices = new System.Numerics.Vector3[] { a, b, c };
            mesh.indices = new uint[] { 0, 1, 2 };
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            return mesh;
        }

        private void DeleteGPUBuffers()
        {
            vertexArrayObject?.Dispose();
            vertexArrayObject = null;
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

        internal static VertexFormat GetVertexLayout(Mesh mesh)
        {
            List<Element> elements = new();
            elements.Add(new Element(VertexSemantic.Position, VertexType.Float, 3));

            if (mesh.HasUV)
                elements.Add(new Element(VertexSemantic.TexCoord0, VertexType.Float, 2));

            if (mesh.HasUV2)
                elements.Add(new Element(VertexSemantic.TexCoord1, VertexType.Float, 2));

            if (mesh.HasNormals)
                elements.Add(new Element(VertexSemantic.Normal, VertexType.Float, 3, 0, true));

            if (mesh.HasColors || mesh.HasColors32)
                elements.Add(new Element(VertexSemantic.Color, VertexType.Float, 4));

            if (mesh.HasTangents)
                elements.Add(new Element(VertexSemantic.Tangent, VertexType.Float, 3, 0, true));

            if (mesh.HasBoneIndices)
                elements.Add(new Element(VertexSemantic.BoneIndex, VertexType.Float, 4));

            if (mesh.HasBoneWeights)
                elements.Add(new Element(VertexSemantic.BoneWeight, VertexType.Float, 4));

            return new VertexFormat(elements.ToArray());
        }

        internal byte[] MakeVertexDataBlob(VertexFormat layout)
        {
            var buffer = new byte[layout.Size * vertices.Length];

            void Copy(byte[] source, ref int index)
            {
                if (index + source.Length > buffer.Length)
                {
                    throw new InvalidOperationException($"[Mesh] Buffer Overrun while generating vertex data blob: {index} -> {index + source.Length} "
                        + $"is larger than buffer {buffer.Length}");
                }

                System.Buffer.BlockCopy(source, 0, buffer, index, source.Length);

                index += source.Length;
            }

            int index = 0;
            for (int i = 0; i < vertices.Length; i++)
            {
                if (index % layout.Size != 0)
                    throw new InvalidOperationException("[Mesh] Exceeded expected byte count while generating vertex data blob");

                //Copy position
                Copy(BitConverter.GetBytes(vertices[i].X), ref index);
                Copy(BitConverter.GetBytes(vertices[i].Y), ref index);
                Copy(BitConverter.GetBytes(vertices[i].Z), ref index);

                if (HasUV)
                {
                    Copy(BitConverter.GetBytes(uv[i].X), ref index);
                    Copy(BitConverter.GetBytes(uv[i].Y), ref index);
                }

                if (HasUV2)
                {
                    Copy(BitConverter.GetBytes(uv2[i].X), ref index);
                    Copy(BitConverter.GetBytes(uv2[i].Y), ref index);
                }

                //Copy normals
                if (HasNormals)
                {
                    Copy(BitConverter.GetBytes(normals[i].X), ref index);
                    Copy(BitConverter.GetBytes(normals[i].Y), ref index);
                    Copy(BitConverter.GetBytes(normals[i].Z), ref index);
                }

                if (HasColors)
                {
                    Copy(BitConverter.GetBytes(colors[i].r), ref index);
                    Copy(BitConverter.GetBytes(colors[i].g), ref index);
                    Copy(BitConverter.GetBytes(colors[i].b), ref index);
                    Copy(BitConverter.GetBytes(colors[i].a), ref index);
                }
                else if (HasColors32)
                {
                    var c = (Color)colors32[i];

                    Copy(BitConverter.GetBytes(c.r), ref index);
                    Copy(BitConverter.GetBytes(c.g), ref index);
                    Copy(BitConverter.GetBytes(c.b), ref index);
                    Copy(BitConverter.GetBytes(c.a), ref index);
                }

                if (HasTangents)
                {
                    Copy(BitConverter.GetBytes(tangents[i].X), ref index);
                    Copy(BitConverter.GetBytes(tangents[i].Y), ref index);
                    Copy(BitConverter.GetBytes(tangents[i].Z), ref index);
                }

                if (HasBoneIndices)
                {
                    //Copy(new byte[] { boneIndices[i].red, boneIndices[i].green, boneIndices[i].blue, boneIndices[i].alpha }, ref index);
                    Copy(BitConverter.GetBytes(boneIndices[i].X), ref index);
                    Copy(BitConverter.GetBytes(boneIndices[i].Y), ref index);
                    Copy(BitConverter.GetBytes(boneIndices[i].Z), ref index);
                    Copy(BitConverter.GetBytes(boneIndices[i].W), ref index);
                }

                if (HasBoneWeights)
                {
                    Copy(BitConverter.GetBytes(boneWeights[i].X), ref index);
                    Copy(BitConverter.GetBytes(boneWeights[i].Y), ref index);
                    Copy(BitConverter.GetBytes(boneWeights[i].Z), ref index);
                    Copy(BitConverter.GetBytes(boneWeights[i].W), ref index);
                }
            }

            return buffer;
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
                        writer.Write(color.red);
                        writer.Write(color.green);
                        writer.Write(color.blue);
                        writer.Write(color.alpha);
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
                meshTopology = (Topology)reader.ReadByte();

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

public class VertexFormat
{
    public Element[] Elements;
    public int Size;

    public VertexFormat() { }

    public VertexFormat(Element[] elements)
    {
        ArgumentNullException.ThrowIfNull(elements);

        if (elements.Length == 0) throw new($"The argument '{nameof(elements)}' is null!");

        Elements = elements;

        foreach (var element in Elements)
        {
            element.Offset = (short)Size;
            int s = 0;
            if ((int)element.Type > 5122) s = 4 * element.Count; //Greater than short then its either a Float or Int
            else if ((int)element.Type > 5121) s = 2 * element.Count; //Greater than byte then its a Short
            else s = 1 * element.Count; //Byte or Unsigned Byte
            Size += s;
            element.Stride = (short)s;
        }
    }

    public class Element
    {
        public uint Semantic;
        public VertexType Type;
        public byte Count;
        public short Offset; // Automatically assigned in VertexFormats constructor
        public short Stride; // Automatically assigned in VertexFormats constructor
        public short Divisor;
        public bool Normalized;
        public Element() { }
        public Element(VertexSemantic semantic, VertexType type, byte count, short divisor = 0, bool normalized = false)
        {
            Semantic = (uint)semantic;
            Type = type;
            Count = count;
            Divisor = divisor;
            Normalized = normalized;
        }
        public Element(uint semantic, VertexType type, byte count, short divisor = 0, bool normalized = false)
        {
            Semantic = semantic;
            Type = type;
            Count = count;
            Divisor = divisor;
            Normalized = normalized;
        }
    }

    public enum VertexSemantic { Position, TexCoord0, TexCoord1, Normal, Color, Tangent, BoneIndex, BoneWeight }

    public enum VertexType { Byte = 5120, UnsignedByte = 5121, Short = 5122, Int = 5124, Float = 5126, }
}