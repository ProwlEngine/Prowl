using Raylib_cs;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using static Prowl.Runtime.Mesh.VertexFormat;

namespace Prowl.Runtime
{
    public sealed class Mesh : EngineObject, ISerializable
    {

        public VertexFormat format;

        public int vertexCount => vertices.Length;
        public int triangleCount => indices.Length / 3;

        // Vertex attributes data
        public Vertex[] vertices;
        public ushort[] indices;

        public Matrix4x4[] bindposes;

        public Mesh() 
        {
            // Default Format
            format = new([
                new Element(VertexSemantic.Position, VertexType.Float, 3),
                new Element(VertexSemantic.TexCoord, VertexType.Float, 2),
                new Element(VertexSemantic.Normal, VertexType.Float, 3, 0, true),
                new Element(VertexSemantic.Color, VertexType.Float, 3),
                new Element(VertexSemantic.Tangent, VertexType.Float, 3),
                new Element(VertexSemantic.BoneIndex, VertexType.UnsignedByte, 4),
                new Element(VertexSemantic.BoneWeight, VertexType.Float, 4)
            ]);
        }

        public uint vao { get; private set; }
        public uint vbo { get; private set; }
        public uint ibo { get; private set; }

        public unsafe void Upload()
        {
            if (vao > 0) return; // Already loaded in, You have to Unload first!
            ArgumentNullException.ThrowIfNull(format);

            ArgumentNullException.ThrowIfNull(vertices);
            if (vertices.Length == 0) throw new($"The mesh argument '{nameof(vertices)}' is empty!");
            ArgumentNullException.ThrowIfNull(indices);

            vao = Rlgl.rlLoadVertexArray();
            Rlgl.rlEnableVertexArray(vao);

            fixed (Vertex* vptr = vertices)
                vbo = Rlgl.rlLoadVertexBuffer(vptr, vertexCount * sizeof(Vertex), false);

            for (var i = 0; i < format.Elements.Length; i++)
            {
                var element = format.Elements[i];
                var index = (int)element.Semantic;
                Rlgl.rlEnableVertexAttribute((uint)index);
                //Rlgl.rlSetVertexAttribute((uint)index, element.Count, (int)element.Type, element.Normalized, element.Offset, null);
                int offset = (int)element.Offset;
                Rlgl.rlSetVertexAttribute((uint)index, element.Count, (int)element.Type, element.Normalized, format.Size, (void*)offset);
                //Rlgl.rlSetVertexAttributeDivisor((uint)index, element.Divisor);
            }

            fixed (ushort* iptr = indices)
                ibo = Rlgl.rlLoadVertexBufferElement(iptr, indices.Length * sizeof(ushort), false);

            Debug.Log($"VAO: [ID {vao}] Mesh uploaded successfully to VRAM (GPU)");

            Rlgl.rlDisableVertexBuffer();
            Rlgl.rlDisableVertexArray();
        }

        // Unload from memory (RAM and VRAM)
        void Unload()
        {
            // Unload rlgl vboId data
            Rlgl.rlUnloadVertexArray(vao);
            vao = 0;
            Rlgl.rlUnloadVertexBuffer(vbo);
        }

        public override void OnDispose()
        {
            Unload();
        }

        #region Create Primitives

        public static Mesh CreateSphere(float radius, int rings, int slices)
        {
            Mesh mesh = new();

            int vertexCount = (rings + 1) * (slices + 1);
            int triangleCount = rings * slices * 2;

            mesh.vertices = new Vertex[vertexCount];
            mesh.indices = new ushort[triangleCount * 3];

            int vertexIndex = 0;
            int triangleIndex = 0;

            // Generate vertices and normals
            for (int i = 0; i <= rings; i++)
            {
                float theta = (float)i / rings * (float)Math.PI;
                for (int j = 0; j <= slices; j++)
                {
                    float phi = (float)j / slices * 2.0f * (float)Math.PI;

                    float x = (float)(Math.Sin(theta) * Math.Cos(phi));
                    float y = (float)Math.Cos(theta);
                    float z = (float)(Math.Sin(theta) * Math.Sin(phi));

                    Vertex v = new()
                    {
                        Position = new Vector3(x, y, z) * radius,
                        Normal = new Vector3(x, y, z),
                        TexCoord = new Vector2((float)j / slices, (float)i / rings)
                    };
                    mesh.vertices[vertexIndex++] = v;
                }
            }

            // Generate triangles
            ushort sliceCount = (ushort)(slices + 1);
            for (ushort i = 0; i < rings; i++)
            {
                for (ushort j = 0; j < slices; j++)
                {
                    ushort nextRing = (ushort)((i + 1) * sliceCount);
                    ushort nextSlice = (ushort)(j + 1);

                    mesh.indices[triangleIndex] = (ushort)(i * sliceCount + j);
                    mesh.indices[triangleIndex + 1] = (ushort)(nextRing + j);
                    mesh.indices[triangleIndex + 2] = (ushort)(nextRing + nextSlice);

                    mesh.indices[triangleIndex + 3] = (ushort)(i * sliceCount + j);
                    mesh.indices[triangleIndex + 4] = (ushort)(nextRing + nextSlice);
                    mesh.indices[triangleIndex + 5] = (ushort)(i * sliceCount + nextSlice);

                    triangleIndex += 6;
                }
            }

            return mesh;
        }

        private static Mesh fullScreenQuad;

        public static Mesh GetFullscreenQuad()
        {
            if (fullScreenQuad != null) return fullScreenQuad;

            Mesh mesh = new Mesh();

            mesh.vertices = 
            [
                new Vertex { Position = new Vector3(-1, -1, 0), TexCoord = new Vector2(0, 0) },
                new Vertex { Position = new Vector3(1, -1, 0), TexCoord = new Vector2(1, 0) },
                new Vertex { Position = new Vector3(-1, 1, 0), TexCoord = new Vector2(0, 1) },
                new Vertex { Position = new Vector3(1, 1, 0), TexCoord = new Vector2(1, 1) }
            ];

            mesh.indices = [0, 2, 1, 2, 3, 1];

            fullScreenQuad = mesh;
            return mesh;
        }

        #endregion

        public CompoundTag Serialize(TagSerializer.SerializationContext ctx)
        {
            CompoundTag compoundTag = new CompoundTag();
            // Serialize to byte[]
            using (MemoryStream memoryStream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(memoryStream))
            {
                //SerializeArray(writer, vertices);
                writer.Write(vertices.Length);
                foreach (var vertex in vertices)
                {
                    writer.Write(vertex.Position.X);
                    writer.Write(vertex.Position.Y);
                    writer.Write(vertex.Position.Z);

                    writer.Write(vertex.TexCoord.X);
                    writer.Write(vertex.TexCoord.Y);

                    writer.Write(vertex.Normal.X);
                    writer.Write(vertex.Normal.Y);
                    writer.Write(vertex.Normal.Z);

                    writer.Write(vertex.Color.X);
                    writer.Write(vertex.Color.Y);
                    writer.Write(vertex.Color.Z);

                    writer.Write(vertex.Tangent.X);
                    writer.Write(vertex.Tangent.Y);
                    writer.Write(vertex.Tangent.Z);

                    writer.Write(vertex.BoneIndex0);
                    writer.Write(vertex.BoneIndex1);
                    writer.Write(vertex.BoneIndex2);
                    writer.Write(vertex.BoneIndex3);

                    writer.Write(vertex.Weight0);
                    writer.Write(vertex.Weight1);
                    writer.Write(vertex.Weight2);
                    writer.Write(vertex.Weight3);
                }

                SerializeArray(writer, indices);

                compoundTag.Add("Data", new ByteArrayTag(memoryStream.ToArray()));
            }
            compoundTag.Add("Format", TagSerializer.Serialize(format, ctx));
            return compoundTag;
        }

        public void Deserialize(CompoundTag value, TagSerializer.SerializationContext ctx)
        {
            using (MemoryStream memoryStream = new MemoryStream(value["Data"].ByteArrayValue))
            using (BinaryReader reader = new BinaryReader(memoryStream))
            {
                //vertices = DeserializeArray<Vertex>(reader);
                int vertexCount = reader.ReadInt32();
                vertices = new Vertex[vertexCount];
                for (int i = 0; i < vertexCount; i++)
                {
                    vertices[i] = new Vertex
                    {
                        Position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                        TexCoord = new Vector2(reader.ReadSingle(), reader.ReadSingle()),
                        Normal = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                        Color = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                        Tangent = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                        BoneIndex0 = reader.ReadByte(),
                        BoneIndex1 = reader.ReadByte(),
                        BoneIndex2 = reader.ReadByte(),
                        BoneIndex3 = reader.ReadByte(),
                        Weight0 = reader.ReadSingle(),
                        Weight1 = reader.ReadSingle(),
                        Weight2 = reader.ReadSingle(),
                        Weight3 = reader.ReadSingle()
                    };
                }
                indices = DeserializeArray<ushort> (reader);
            }
            format = TagSerializer.Deserialize<VertexFormat>(value["Format"], ctx);
        }

        // Helper method to serialize an array
        private static void SerializeArray<T>(BinaryWriter writer, T[] array) where T : struct
        {
            if (array == null)
            {
                writer.Write(false);
                return;
            }
            writer.Write(true);
            int length = array.Length;
            writer.Write(length);
            int elementSize = Marshal.SizeOf<T>();
            byte[] bytes = new byte[length * elementSize];
            Buffer.BlockCopy(array, 0, bytes, 0, bytes.Length);
            writer.Write(bytes);
        }

        // Helper method to deserialize an array
        private static T[] DeserializeArray<T>(BinaryReader reader) where T : struct
        {
            bool isNotNull = reader.ReadBoolean();
            if (!isNotNull) return null;
            int length = reader.ReadInt32();
            int elementSize = Marshal.SizeOf<T>();

            byte[] bytes = reader.ReadBytes(length * elementSize);
            T[] array = new T[length];

            Buffer.BlockCopy(bytes, 0, array, 0, bytes.Length);

            return array;
        }

        public struct Vertex
        {
            public System.Numerics.Vector3 Position;
            public System.Numerics.Vector2 TexCoord;
            public System.Numerics.Vector3 Normal;
            public System.Numerics.Vector3 Color;
            public System.Numerics.Vector3 Tangent;
            public byte BoneIndex0, BoneIndex1, BoneIndex2, BoneIndex3;
            public float Weight0, Weight1, Weight2, Weight3;
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
                    if ((int)element.Type > 5122)       s = 4 * element.Count; //Greater than short then its either a Float or Int
                    else if ((int)element.Type > 5121)  s = 2 * element.Count; //Greater than byte then its a Short
                    else                                s = 1 * element.Count; //Byte or Unsigned Byte
                    Size += s;
                    element.Stride = (short)s;
                }
            }

            public class Element
            {

                public VertexSemantic Semantic;
                public VertexType Type;
                public byte Count;
                public short Offset; // Automatically assigned in VertexFormats constructor
                public short Stride; // Automatically assigned in VertexFormats constructor
                public short Divisor;
                public bool Normalized;
                public Element() { }
                public Element(VertexSemantic semantic, VertexType type, byte count, short divisor = 0, bool normalized = false)
                {
                    Semantic = semantic;
                    Type = type;
                    Count = count;
                    Divisor = divisor;
                    Normalized = normalized;
                }
            }

            public enum VertexSemantic { Position, TexCoord, Normal, Color, Tangent, BoneIndex, BoneWeight }

            public enum VertexType { Byte = 5120, UnsignedByte = 5121, Short = 5122, Int = 5124, Float = 5126, }
        }
    }
}
