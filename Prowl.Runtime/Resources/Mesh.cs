using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using static Prowl.Runtime.Mesh.VertexFormat;

namespace Prowl.Runtime
{
    public sealed class Mesh : EngineObject, ISerializable
    {

        public VertexFormat format;

        public int vertexCount => vertices.Length;
        public int triangleCount => triangles.Length / 3;

        // Vertex attributes data
        public Vertex[] vertices;
        public ushort[] triangles;

        // The array of bone paths under a root
        // The index of a path is the Bone Index
        public string[] boneNames;
        public (Vector3, Quaternion, Vector3)[] boneOffsets;

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
        private int uploadedVBOSize = 0;
        public uint ibo { get; private set; }
        private int uploadedIBOSize = 0;

        public unsafe void Upload()
        {
            if (vao > 0) return; // Already loaded in, You have to Unload first!
            ArgumentNullException.ThrowIfNull(format);

            ArgumentNullException.ThrowIfNull(vertices);
            if (vertices.Length == 0) throw new($"The mesh argument '{nameof(vertices)}' is empty!");

            vao = Graphics.GL.GenVertexArray();
            Graphics.GL.BindVertexArray(vao);
            Graphics.CheckGL();

            vbo  = Graphics.GL.GenBuffer();
            Graphics.GL.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
            Graphics.GL.BufferData(BufferTargetARB.ArrayBuffer, new ReadOnlySpan<Vertex>(vertices), BufferUsageARB.StaticDraw);
            uploadedVBOSize = vertices.Length;

            Graphics.CheckGL();
            format.Bind();

            uploadedIBOSize = triangles?.Length ?? 0;
            if (triangles != null) {
                ibo = Graphics.GL.GenBuffer();
                Graphics.GL.BindBuffer(BufferTargetARB.ElementArrayBuffer, ibo);
                Graphics.GL.BufferData(BufferTargetARB.ElementArrayBuffer, new ReadOnlySpan<ushort>(triangles), BufferUsageARB.StaticDraw);
            }

            Debug.Log($"VAO: [ID {vao}] Mesh uploaded successfully to VRAM (GPU)");

            Graphics.GL.BindVertexArray(0);
            Graphics.GL.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
        }

        // Update the data inside the VBO and IBO if it exist if not it just unloads and reuploads
        public void Update()
        {
            if (vao <= 0) {
                Upload();
                return;
            }

            Graphics.GL.BindVertexArray(vao);
            Graphics.GL.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
            if(uploadedVBOSize != vertices.Length) {
                // Vertex count changed then reallocate it
                Graphics.GL.BufferData(BufferTargetARB.ArrayBuffer, new ReadOnlySpan<Vertex>(vertices), BufferUsageARB.StaticDraw);
            } else {
                // Vertex count didnt change so just update it
                Graphics.GL.BufferSubData(BufferTargetARB.ArrayBuffer, IntPtr.Zero, new ReadOnlySpan<Vertex>(vertices));
            }
            uploadedVBOSize = vertices.Length;

            if ((uploadedIBOSize > 0 && triangles == null) || (uploadedIBOSize != triangles.Length)) {
                uploadedIBOSize = triangles?.Length ?? 0;


                // if indices has been deleted
                if (triangles == null) {
                    Graphics.GL.DeleteBuffer(ibo);
                    ibo = 0;
                } else {
                    // If we dont have an indices buffer create one
                    if(ibo == 0) {
                        ibo = Graphics.GL.GenBuffer();
                    }

                    // if indices has been changed
                    Graphics.GL.BindBuffer(BufferTargetARB.ElementArrayBuffer, ibo);
                    if (uploadedIBOSize != vertices.Length) {
                        // Size changed so reallocate
                        Graphics.GL.BufferData(BufferTargetARB.ElementArrayBuffer, new ReadOnlySpan<ushort>(triangles), BufferUsageARB.StaticDraw);
                    } else {
                        // Size didnt change so just update
                        Graphics.GL.BufferSubData(BufferTargetARB.ElementArrayBuffer, IntPtr.Zero, new ReadOnlySpan<ushort>(triangles));
                    }
                }
            }

            Graphics.GL.BindVertexArray(0);
            Graphics.GL.BindBuffer(BufferTargetARB.ArrayBuffer, 0);

        }

        // Unload from memory (RAM and VRAM)
        public void Unload()
        {
            if (vao <= 0) return; // nothing to unload

            // Unload rlgl vboId data
            Graphics.GL.DeleteVertexArray(vao);
            vao = 0;
            Graphics.GL.DeleteBuffer(vbo);
        }

        public override void OnDispose()
        {
            Unload();
        }

        #region Create Primitives

        public struct CubeFace
        {
            public bool enabled;
            public Vector2[] texCoords;
        }

        /// <summary>
        /// 24 vertex cube with per face control
        /// </summary>
        /// <param name="size">Size of the cube</param>
        /// <param name="faces">0=(Z+) 1=(Z-) 2=(Y+) 3=(Y-) 4=(X+) 5=(X-)</param>
        public static Mesh CreateCube(Vector3 size, CubeFace[] faces)
        {
            if (faces.Length != 6) throw new($"The argument '{nameof(faces)}' must have 6 elements!");

            Mesh mesh = new();

            List<Vertex> vertices = new();
            List<ushort> indices = new();

            // Front Face (Z+) - 0
            if (faces[0].enabled) {
                vertices.Add(new Vertex { Position = new Vector3(-size.x, -size.y, size.z), TexCoord = faces[0].texCoords[0] });
                vertices.Add(new Vertex { Position = new Vector3(size.x, -size.y, size.z), TexCoord = faces[0].texCoords[1] });
                vertices.Add(new Vertex { Position = new Vector3(size.x, size.y, size.z), TexCoord = faces[0].texCoords[2] });
                vertices.Add(new Vertex { Position = new Vector3(-size.x, size.y, size.z), TexCoord = faces[0].texCoords[3] });
                indices.AddRange(new ushort[] { 0, 1, 2, 0, 2, 3 });
            }
            // Back Face (Z-) - 1
            if (faces[1].enabled) {
                vertices.Add(new Vertex { Position = new Vector3(size.x, -size.y, -size.z), TexCoord = faces[1].texCoords[0] });
                vertices.Add(new Vertex { Position = new Vector3(-size.x, -size.y, -size.z), TexCoord = faces[1].texCoords[1] });
                vertices.Add(new Vertex { Position = new Vector3(-size.x, size.y, -size.z), TexCoord = faces[1].texCoords[2] });
                vertices.Add(new Vertex { Position = new Vector3(size.x, size.y, -size.z), TexCoord = faces[1].texCoords[3] });
                indices.AddRange(new ushort[] { 4, 5, 6, 4, 6, 7 });
            }
            // Top Face (Y+) - 2
            if (faces[2].enabled) {
                vertices.Add(new Vertex { Position = new Vector3(-size.x, size.y, -size.z), TexCoord = faces[2].texCoords[0] });
                vertices.Add(new Vertex { Position = new Vector3(size.x, size.y, -size.z), TexCoord = faces[2].texCoords[1] });
                vertices.Add(new Vertex { Position = new Vector3(size.x, size.y, size.z), TexCoord = faces[2].texCoords[2] });
                vertices.Add(new Vertex { Position = new Vector3(-size.x, size.y, size.z), TexCoord = faces[2].texCoords[3] });
                indices.AddRange(new ushort[] { 8, 9, 10, 8, 10, 11 });
            }
            // Bottom Face (Y-) - 3
            if (faces[3].enabled) {
                vertices.Add(new Vertex { Position = new Vector3(size.x, -size.y, -size.z), TexCoord = faces[3].texCoords[0] });
                vertices.Add(new Vertex { Position = new Vector3(-size.x, -size.y, -size.z), TexCoord = faces[3].texCoords[1] });
                vertices.Add(new Vertex { Position = new Vector3(-size.x, -size.y, size.z), TexCoord = faces[3].texCoords[2] });
                vertices.Add(new Vertex { Position = new Vector3(size.x, -size.y, size.z), TexCoord = faces[3].texCoords[3] });
                indices.AddRange(new ushort[] { 12, 13, 14, 12, 14, 15 });
            }
            // Right Face (X+) - 4
            if (faces[4].enabled) {
                vertices.Add(new Vertex { Position = new Vector3(size.x, -size.y, size.z), TexCoord = faces[4].texCoords[0] });
                vertices.Add(new Vertex { Position = new Vector3(size.x, -size.y, -size.z), TexCoord = faces[4].texCoords[1] });
                vertices.Add(new Vertex { Position = new Vector3(size.x, size.y, -size.z), TexCoord = faces[4].texCoords[2] });
                vertices.Add(new Vertex { Position = new Vector3(size.x, size.y, size.z), TexCoord = faces[4].texCoords[3] });
                indices.AddRange(new ushort[] { 16, 17, 18, 16, 18, 19 });
            }
            // Left Face (X-) - 5
            if (faces[5].enabled) {
                vertices.Add(new Vertex { Position = new Vector3(-size.x, -size.y, -size.z), TexCoord = faces[5].texCoords[0] });
                vertices.Add(new Vertex { Position = new Vector3(-size.x, -size.y, size.z), TexCoord = faces[5].texCoords[1] });
                vertices.Add(new Vertex { Position = new Vector3(-size.x, size.y, size.z), TexCoord = faces[5].texCoords[2] });
                vertices.Add(new Vertex { Position = new Vector3(-size.x, size.y, -size.z), TexCoord = faces[5].texCoords[3] });
                indices.AddRange(new ushort[] { 20, 21, 22, 20, 22, 23 });
            }

            mesh.vertices = [.. vertices];
            mesh.triangles = [.. indices];
            return mesh;
        }

        public static Mesh CreateSphere(float radius, int rings, int slices)
        {
            Mesh mesh = new();

            int vertexCount = (rings + 1) * (slices + 1);
            int triangleCount = rings * slices * 2;

            mesh.vertices = new Vertex[vertexCount];
            mesh.triangles = new ushort[triangleCount * 3];

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

                    mesh.triangles[triangleIndex] = (ushort)(i * sliceCount + j);
                    mesh.triangles[triangleIndex + 1] = (ushort)(nextRing + j);
                    mesh.triangles[triangleIndex + 2] = (ushort)(nextRing + nextSlice);

                    mesh.triangles[triangleIndex + 3] = (ushort)(i * sliceCount + j);
                    mesh.triangles[triangleIndex + 4] = (ushort)(nextRing + nextSlice);
                    mesh.triangles[triangleIndex + 5] = (ushort)(i * sliceCount + nextSlice);

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

            mesh.triangles = [0, 2, 1, 2, 3, 1];

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

                // Serialize bone names
                writer.Write(boneNames?.Length ?? 0);
                if (boneNames != null)
                    for (int i = 0; i < boneNames.Length; i++)
                        writer.Write(boneNames[i]);

                // Serialize bone offsets
                writer.Write(boneOffsets?.Length ?? 0);
                if (boneOffsets != null)
                    for (int i = 0; i < boneOffsets.Length; i++)
                    {
                        writer.Write((float)boneOffsets[i].Item1.x);
                        writer.Write((float)boneOffsets[i].Item1.y);
                        writer.Write((float)boneOffsets[i].Item1.z);
                        writer.Write((float)boneOffsets[i].Item2.x);
                        writer.Write((float)boneOffsets[i].Item2.y);
                        writer.Write((float)boneOffsets[i].Item2.z);
                        writer.Write((float)boneOffsets[i].Item2.w);
                        writer.Write((float)boneOffsets[i].Item3.x);
                        writer.Write((float)boneOffsets[i].Item3.y);
                        writer.Write((float)boneOffsets[i].Item3.z);
                    }

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

                SerializeArray(writer, triangles);

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
                int boneCount = reader.ReadInt32();
                if (boneCount > 0)
                {
                    boneNames = new string[boneCount];
                    for (int i = 0; i < boneCount; i++)
                        boneNames[i] = reader.ReadString();
                }

                int boneOffsetCount = reader.ReadInt32();
                if (boneOffsetCount > 0)
                {
                    boneOffsets = new (Vector3, Quaternion, Vector3)[boneOffsetCount];
                    for (int i = 0; i < boneOffsetCount; i++)
                    {
                        Vector3 v = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                        Quaternion q = new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                        Vector3 s = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                        boneOffsets[i] = (v, q, s);
                    }
                }


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
                triangles = DeserializeArray<ushort> (reader);

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
            System.Buffer.BlockCopy(array, 0, bytes, 0, bytes.Length);
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

            System.Buffer.BlockCopy(bytes, 0, array, 0, bytes.Length);

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

            public enum VertexSemantic { Position, TexCoord, Normal, Color, Tangent, BoneIndex, BoneWeight }

            public enum VertexType { Byte = 5120, UnsignedByte = 5121, Short = 5122, Int = 5124, Float = 5126, }

            public void Bind()
            {
                for (var i = 0; i < Elements.Length; i++) {
                    var element = Elements[i];
                    var index = element.Semantic;
                    Graphics.GL.EnableVertexAttribArray(index);
                    int offset = (int)element.Offset;
                    unsafe {
                        if(element.Type == VertexType.Float)
                            Graphics.GL.VertexAttribPointer(index, element.Count, (GLEnum)element.Type, element.Normalized, (uint)Size, (void*)offset);
                        else
                            Graphics.GL.VertexAttribIPointer(index, element.Count, (GLEnum)element.Type, (uint)Size, (void*)offset);
                    }
                }
            }
        }
    }

    //public interface IInstantiatable
    //{
    //    void Instantiate();
    //}
    //
    //public sealed class Model : EngineObject, IInstantiatable
    //{
    //    public Mesh[] meshes;
    //    public Material[] materials;
    //    public ModelNode rootNode;        // actual model root node - base node of the model - from here we can locate any node in the chain        
    //
    //    public void Instantiate()
    //    {
    //    }
    //
    //    public class ModelNode
    //    {
    //        public string name;
    //        public ModelNode parent;
    //        public List<ModelNode> children = new List<ModelNode>();      
    //
    //        public Matrix local;
    //        public Matrix combined;
    //    }
    //}
    //
    //public sealed class SkinModel : EngineObject, IInstantiatable
    //{
    //    public Mesh[] meshes;
    //    public Material[] materials;
    //    public ModelNode rootNode;
    //
    //    public List<AnimationClip> animations = new List<AnimationClip>();
    //
    //    public void Instantiate()
    //    {
    //    }
    //
    //    public class ModelNode
    //    {
    //        public string name;
    //        public ModelNode parent;
    //        public List<ModelNode> children = new List<ModelNode>();      
    //
    //        // Each mesh has a list of shader-matrices - this keeps track of which meshes these bones apply to (and the bone index)
    //        public List<ModelBone> uniqueMeshBones = new List<ModelBone>();
    //
    //        public Matrix local;
    //        public Matrix combined;
    //    }
    //
    //    public class ModelBone
    //    {
    //        public string name;
    //        public int meshIndex = -1;
    //        public int boneIndex = -1;
    //        public int numWeightedVerts = 0;
    //        public Matrix offset;
    //    }
    //}
    //
    ///// <summary> A Animation clip with Tracks for each Bone/Node, It stores one entire animation. </summary>
    //public class AnimationClip : EngineObject
    //{
    //    public double DurationInTicks;
    //    public double DurationInSeconds;
    //    public double DurationInSecondsAdded;
    //    public double TicksPerSecond;
    //
    //    public bool HasMeshAnims;
    //    public bool HasNodeAnims;
    //    public List<AnimTrack> animatedNodes;
    //}
    //
    ///// <summary> The Position/Rotation/Scale data for a single node in the model hierarchy for the entire animation clip </summary>
    //public class AnimTrack
    //{
    //    public string nodePath = ""; // The path to the node in the model hierarchy
    //
    //    // Frames for this track/node
    //    public List<double> positionTime = new List<double>();
    //    public List<double> scaleTime = new List<double>();
    //    public List<double> rotationTime = new List<double>();
    //    public List<Vector3> position = new List<Vector3>();
    //    public List<Vector3> scale = new List<Vector3>();
    //    public List<Quaternion> rotation = new List<Quaternion>();
    //}
}
