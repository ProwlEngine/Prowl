using Raylib_cs;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Prowl.Runtime
{
    public sealed class Mesh : EngineObject, ISerializable
    {
        public int vertexCount => vertices.Length / 3;
        public int triangleCount => triangles.Length / 3;

        // Vertex attributes data
        public float[] vertices;       // Vertex position (XYZ - 3 components per vertex) (shader-location = 0)
        public float[] texcoords;      // Vertex texture coordinates (UV - 2 components per vertex) (shader-location = 1)
        public float[] texcoords2;     // Vertex texture second coordinates (UV - 2 components per vertex) (shader-location = 5)
        public float[] normals;        // Vertex normals (XYZ - 3 components per vertex) (shader-location = 2)
        public float[] tangents;       // Vertex tangents (XYZW - 4 components per vertex) (shader-location = 4)
        public byte[] colors;          // Vertex colors (RGBA - 4 components per vertex) (shader-location = 3)

        public ushort[] triangles;        // Vertex indices (in case vertex data comes indexed)

        // Animation vertex data
        public byte[] boneIds;         // Vertex bone ids, max 255 bone ids, up to 4 bones influence by vertex (skinning)
        public float[] boneWeights;    // Vertex bone weight, up to 4 bones influence by vertex (skinning)

        // OpenGL identifiers
        internal uint vaoId;              // OpenGL Vertex Array Object id
        internal uint[] vboId;        // OpenGL Vertex Buffer Objects id (default vertex data)

        public unsafe void Upload(bool dynamic)
        {
            if (vaoId > 0)
            {
                // Check if mesh has already been loaded in GPU
                Debug.LogError($"VAO: [ID {vaoId}] Trying to re-load an already loaded mesh");
                return;
            }

            vboId = new uint[7];

            vaoId = 0;        // Vertex Array Object
            vboId[0] = 0;     // Vertex buffer: positions
            vboId[1] = 0;     // Vertex buffer: texcoords
            vboId[2] = 0;     // Vertex buffer: normals
            vboId[3] = 0;     // Vertex buffer: colors
            vboId[4] = 0;     // Vertex buffer: tangents
            vboId[5] = 0;     // Vertex buffer: texcoords2
            vboId[6] = 0;     // Vertex buffer: indices

            vaoId = Rlgl.rlLoadVertexArray();
            Rlgl.rlEnableVertexArray(vaoId);

            // NOTE: Vertex attributes must be uploaded considering default locations points and available vertex data

            int RL_FLOAT = 0x1406;
            int RL_UNSIGNED_BYTE = 0x1401;

            if (vertices == null) throw new Exception("Vertices cannot be null");

            // Enable vertex attributes: position (shader-location = 0)
            fixed (float* vptr = vertices)
                vboId[0] = Rlgl.rlLoadVertexBuffer(vptr, vertexCount * 3 * sizeof(float), dynamic);
            Rlgl.rlSetVertexAttribute(0, 3, RL_FLOAT, 0, 0, null);
            Rlgl.rlEnableVertexAttribute(0);

            if (texcoords != null)
            {
                // Enable vertex attributes: texcoords (shader-location = 1)
                fixed (float* tptr = texcoords)
                    vboId[1] = Rlgl.rlLoadVertexBuffer(tptr, vertexCount * 2 * sizeof(float), dynamic);
                Rlgl.rlSetVertexAttribute(1, 2, RL_FLOAT, 0, 0, null);
                Rlgl.rlEnableVertexAttribute(1);
            }
            else
            {
                // Default vertex attribute: texcoords
                // WARNING: Default value provided to shader if location available
                float[] value = { 1.0f, 1.0f };
                fixed (float* tptr = value)
                    Rlgl.rlSetVertexAttributeDefault(1, tptr, (int)ShaderAttributeDataType.SHADER_ATTRIB_VEC2, 2);
                Rlgl.rlDisableVertexAttribute(1);
            }

            // WARNING: When setting default vertex attribute values, the values for each generic vertex attribute
            // is part of current state, and it is maintained even if a different program object is used

            if (normals != null)
            {
                // Enable vertex attributes: normals (shader-location = 2)
                fixed (float* nptr = normals)
                    vboId[2] = Rlgl.rlLoadVertexBuffer(nptr, vertexCount * 3 * sizeof(float), dynamic);
                Rlgl.rlSetVertexAttribute(2, 3, RL_FLOAT, 1, 0, null);
                Rlgl.rlEnableVertexAttribute(2);
            }
            else
            {
                // Default vertex attribute: normal
                // WARNING: Default value provided to shader if location available
                float[] value = { 1.0f, 1.0f, 1.0f };
                fixed (float* nptr = value)
                    Rlgl.rlSetVertexAttributeDefault(2, nptr, (int)ShaderAttributeDataType.SHADER_ATTRIB_VEC3, 3);
                Rlgl.rlDisableVertexAttribute(2);
            }

            if (colors != null)
            {
                // Enable vertex attribute: color (shader-location = 3)
                fixed (byte* cptr = colors)
                    vboId[3] = Rlgl.rlLoadVertexBuffer(cptr, vertexCount * 4 * sizeof(byte), dynamic);
                Rlgl.rlSetVertexAttribute(3, 4, RL_UNSIGNED_BYTE, 0, 0, null);
                Rlgl.rlEnableVertexAttribute(3);
            }
            else
            {
                // Default vertex attribute: color
                // WARNING: Default value provided to shader if location available
                float[] value = { 1.0f, 1.0f, 1.0f, 1.0f };    // WHITE
                fixed (float* cptr = value)
                    Rlgl.rlSetVertexAttributeDefault(3, cptr, (int)ShaderAttributeDataType.SHADER_ATTRIB_VEC4, 4);
                Rlgl.rlDisableVertexAttribute(3);
            }

            if (tangents != null)
            {
                // Enable vertex attribute: tangent (shader-location = 4)
                fixed (float* taptr = tangents)
                    vboId[4] = Rlgl.rlLoadVertexBuffer(taptr, vertexCount * 4 * sizeof(float), dynamic);
                Rlgl.rlSetVertexAttribute(4, 4, RL_FLOAT, 0, 0, null);
                Rlgl.rlEnableVertexAttribute(4);
            }
            else
            {
                // Default vertex attribute: tangent
                // WARNING: Default value provided to shader if location available
                float[] value = { 0.0f, 0.0f, 0.0f, 0.0f };
                fixed (float* taptr = value)
                    Rlgl.rlSetVertexAttributeDefault(4, taptr, (int)ShaderAttributeDataType.SHADER_ATTRIB_VEC4, 4);
                Rlgl.rlDisableVertexAttribute(4);
            }

            if (texcoords2 != null)
            {
                // Enable vertex attribute: texcoord2 (shader-location = 5)
                fixed (float* t2ptr = texcoords2)
                    vboId[5] = Rlgl.rlLoadVertexBuffer(t2ptr, vertexCount * 2 * sizeof(float), dynamic);
                Rlgl.rlSetVertexAttribute(5, 2, RL_FLOAT, 0, 0, null);
                Rlgl.rlEnableVertexAttribute(5);
            }
            else
            {
                // Default vertex attribute: texcoord2
                // WARNING: Default value provided to shader if location available
                float[] value = { 0.0f, 0.0f };
                fixed (float* t2ptr = value)
                    Rlgl.rlSetVertexAttributeDefault(5, t2ptr, (int)ShaderAttributeDataType.SHADER_ATTRIB_VEC2, 2);
                Rlgl.rlDisableVertexAttribute(5);
            }

            if (triangles != null)
            {
                fixed (ushort* trptr = triangles)
                    vboId[6] = Rlgl.rlLoadVertexBufferElement(trptr, triangleCount * 3 * sizeof(ushort), dynamic);
            }

            if (vaoId > 0) Debug.Log($"VAO: [ID {vaoId}] Mesh uploaded successfully to VRAM (GPU)");
            else Debug.Log("VBO: Mesh uploaded successfully to VRAM (GPU)");

            Rlgl.rlDisableVertexArray();
        }

        // Unload from memory (RAM and VRAM)
        void Unload()
        {
            // Unload rlgl vboId data
            Rlgl.rlUnloadVertexArray(vaoId);
            vaoId = 0;

            if (vboId != null) for (int i = 0; i < vboId.Length; i++) Rlgl.rlUnloadVertexBuffer(vboId[i]);
            vboId = null;

            vertices = null;
            texcoords = null;
            normals = null;
            colors = null;
            tangents = null;
            texcoords2 = null;
            triangles = null;

            boneWeights = null;
            boneIds = null;
        }

        // Unload on Garbage Collection
        ~Mesh()
        {
            Unload();
        }

        #region Create Primitives

        public static Mesh CreateSphere(float radius, int rings, int slices)
        {
            Mesh mesh = new();

            int vertexCount = (rings + 1) * (slices + 1);
            int triangleCount = rings * slices * 2;

            mesh.vertices = new float[vertexCount * 3];
            mesh.normals = new float[vertexCount * 3];
            mesh.texcoords = new float[vertexCount * 2];
            mesh.triangles = new ushort[triangleCount * 3];

            int vertexIndex = 0;
            int texIndex = 0;
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

                    mesh.vertices[vertexIndex] = radius * x;
                    mesh.vertices[vertexIndex + 1] = radius * y;
                    mesh.vertices[vertexIndex + 2] = radius * z;

                    mesh.normals[vertexIndex] = x;
                    mesh.normals[vertexIndex + 1] = y;
                    mesh.normals[vertexIndex + 2] = z;

                    mesh.texcoords[texIndex] = (float)j / slices;
                    mesh.texcoords[texIndex + 1] = 1.0f - (float)i / rings;

                    vertexIndex += 3;
                    texIndex += 2;
                }
            }

            // Generate triangles
            for (int i = 0; i < rings; i++)
            {
                for (int j = 0; j < slices; j++)
                {
                    int nextRing = (i + 1) * (slices + 1);
                    int nextSlice = j + 1;

                    mesh.triangles[triangleIndex] = (ushort)(i * (slices + 1) + j);
                    mesh.triangles[triangleIndex + 1] = (ushort)(nextRing + j);
                    mesh.triangles[triangleIndex + 2] = (ushort)(nextRing + nextSlice);

                    mesh.triangles[triangleIndex + 3] = (ushort)(i * (slices + 1) + j);
                    mesh.triangles[triangleIndex + 4] = (ushort)(nextRing + nextSlice);
                    mesh.triangles[triangleIndex + 5] = (ushort)(i * (slices + 1) + nextSlice);

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

            // Vertices
            float[] vertices = new float[]
            {
                -1f, -1f, 0f, // Bottom Left
                1f, -1f, 0f,  // Bottom Right
                -1f, 1f, 0f,   // Top Left
                1f, 1f, 0f     // Top Right
            };

            // UVs
            float[] uvs = new float[]
            {
                0f, 0f,
                1f, 0f,
                0f, 1f,
                1f, 1f
            };

            // Triangles
            ushort[] triangles = new ushort[] { 0, 2, 1, 2, 3, 1 };

            mesh.vertices = vertices;
            mesh.texcoords = uvs;
            mesh.triangles = triangles;

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
                SerializeArray(writer, vertices);
                SerializeArray(writer, texcoords);
                SerializeArray(writer, texcoords2);
                SerializeArray(writer, normals);
                SerializeArray(writer, tangents);
                SerializeArray(writer, colors);
                SerializeArray(writer, triangles);
                SerializeArray(writer, boneIds);
                SerializeArray(writer, boneWeights);

                compoundTag.Add("Data", new ByteArrayTag(memoryStream.ToArray()));
            }
            return compoundTag;
        }

        public void Deserialize(CompoundTag value, TagSerializer.SerializationContext ctx)
        {
            using (MemoryStream memoryStream = new MemoryStream(value["Data"].ByteArrayValue))
            using (BinaryReader reader = new BinaryReader(memoryStream))
            {
                vertices = DeserializeArray<float>(reader);
                texcoords = DeserializeArray<float>(reader);
                texcoords2 = DeserializeArray<float>(reader);
                normals = DeserializeArray<float>(reader);
                tangents = DeserializeArray<float>(reader);
                colors = DeserializeArray<byte>(reader);
                triangles = DeserializeArray<ushort>(reader);
                boneIds = DeserializeArray<byte>(reader);
                boneWeights = DeserializeArray<float>(reader);
            }
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
    }
}
