// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;

using Veldrid;

using Vector2F = System.Numerics.Vector2;
using Vector3F = System.Numerics.Vector3;
using Vector4F = System.Numerics.Vector4;
using Matrix4x4F = System.Numerics.Matrix4x4;

namespace Prowl.Runtime;

public enum MeshResource
{
    Position = 0,
    UV0 = 1,
    UV1 = 2,
    Normals = 3,
    Tangents = 4,
    Colors = 5,
    BoneIndices = 6,
    BoneWeights = 7
}

public class Mesh : EngineObject, ISerializable, IGeometryDrawData
{
    /// <summary> Whether this mesh is readable by the CPU </summary>
    public readonly bool isReadable = true;

    /// <summary> Whether this mesh is writable </summary>
    public readonly bool isWritable = true;

    /// <summary> The bounds of the mesh </summary>
    public Bounds bounds { get; internal set; }

    /// <summary> The format of the indices for this mesh </summary>
    public IndexFormat IndexFormat
    {
        get => indexFormat;
        set
        {
            if (isWritable == false || indexFormat == value) return;

            changed = true;
            indexFormat = value;

            if (value == IndexFormat.UInt16)
                indices32 = [];
            else
                indices16 = [];
        }
    }

    /// <summary> The mesh's primitive type </summary>
    public PrimitiveTopology Topology
    {
        get => topology;
        set
        {
            if (isWritable == false) return;
            changed = true;
            topology = value;
        }
    }

    /// <summary>
    /// Sets or gets the current vertices.
    /// Getting depends on isReadable.
    /// Note: When setting, if the vertex count is different than previous, it'll reset all other vertex data fields.
    /// </summary>
    public Vector3F[] Vertices
    {
        get => vertices ?? [];
        set
        {
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
                uv = null;
                uv2 = null;
                indices32 = null;
            }
        }
    }

    public Vector3F[] Normals
    {
        get => ReadVertexData(normals ?? []);
        set => WriteVertexData(ref normals, value, value.Length);
    }

    public Vector3F[] Tangents
    {
        get => ReadVertexData(tangents ?? []);
        set => WriteVertexData(ref tangents, value, value.Length);
    }

    public Color32[] Colors
    {
        get => ReadVertexData(colors ?? []);
        set => WriteVertexData(ref colors, value, value.Length);
    }

    public Vector2F[] UV
    {
        get => ReadVertexData(uv ?? []);
        set => WriteVertexData(ref uv, value, value.Length);
    }

    public Vector2F[] UV2
    {
        get => ReadVertexData(uv2 ?? []);
        set => WriteVertexData(ref uv2, value, value.Length);
    }

    public uint[] Indices32
    {
        get => ReadVertexData(indices32 ?? []);
        set => WriteVertexData(ref indices32, value, value.Length, false);
    }

    public ushort[] Indices16
    {
        get => ReadVertexData(indices16 ?? []);
        set => WriteVertexData(ref indices16, value, value.Length, false);
    }

    public Vector4F[] BoneIndices
    {
        get => ReadVertexData(boneIndices ?? []);
        set => WriteVertexData(ref boneIndices, value, value.Length);
    }

    public Vector4F[] BoneWeights
    {
        get => ReadVertexData(boneWeights ?? []);
        set => WriteVertexData(ref boneWeights, value, value.Length);
    }

    public int VertexCount => vertices?.Length ?? 0;
    public int IndexCount => IndexFormat == IndexFormat.UInt16 ? indices16.Length : indices32.Length;

    public DeviceBuffer VertexBuffer => vertexBuffer;
    public DeviceBuffer IndexBuffer => indexBuffer;

    public bool HasNormals => (normals?.Length ?? 0) > 0;
    public bool HasTangents => (tangents?.Length ?? 0) > 0;
    public bool HasColors => (colors?.Length ?? 0) > 0;
    public bool HasUV => (uv?.Length ?? 0) > 0;
    public bool HasUV2 => (uv2?.Length ?? 0) > 0;

    public bool HasBoneIndices => (boneIndices?.Length ?? 0) > 0;
    public bool HasBoneWeights => (boneWeights?.Length ?? 0) > 0;

    public Matrix4x4F[]? bindPoses;

    bool changed = true;
    Vector3F[]? vertices;
    Vector3F[]? normals;
    Vector3F[]? tangents;
    Color32[]? colors;
    Vector2F[]? uv;
    Vector2F[]? uv2;

    uint[]? indices32;
    ushort[]? indices16;

    Vector4F[]? boneIndices;
    Vector4F[]? boneWeights;

    IndexFormat indexFormat = IndexFormat.UInt16;
    PrimitiveTopology topology = PrimitiveTopology.TriangleList;


    DeviceBuffer vertexBuffer;
    DeviceBuffer indexBuffer;

    public int UVStart { get; private set; }
    public int UV2Start { get; private set; }
    public int NormalsStart { get; private set; }
    public int TangentsStart { get; private set; }
    public int ColorsStart { get; private set; }
    public int BufferLength { get; private set; }


    public static readonly Dictionary<string, VertexElementFormat> MeshSemantics = new()
    {
        { "POSITION0", VertexElementFormat.Float3 },
        { "TEXCOORD0", VertexElementFormat.Float2 },
        { "TEXCOORD1", VertexElementFormat.Float2 },
        { "NORMAL0", VertexElementFormat.Float3 },
        { "TANGENT0", VertexElementFormat.Float3 },
        { "COLOR0", VertexElementFormat.Byte4_Norm }
    };

    public Mesh() { }

    public void Clear()
    {
        vertices = null;
        normals = null;
        colors = null;
        uv = null;
        uv2 = null;
        indices16 = null;
        indices32 = null;
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

        if (indexFormat == IndexFormat.UInt16)
        {
            if (indices16 == null || indices16.Length == 0)
                throw new InvalidOperationException($"Mesh has no indices");
        }
        else if (indices32 == null || indices32.Length == 0)
        {
            throw new InvalidOperationException($"Mesh has no indices");
        }

        int indexLength = indexFormat == IndexFormat.UInt16 ? indices16.Length : indices32.Length;
        switch (topology)
        {
            case PrimitiveTopology.TriangleList:
                if (indexLength % 3 != 0)
                    throw new InvalidOperationException($"Triangle List mesh doesn't have the right amount of indices. Has: {indexLength}. Should be a multiple of 3");
                break;
            case PrimitiveTopology.TriangleStrip:
                if (indexLength < 3)
                    throw new InvalidOperationException($"Triangle Strip mesh doesn't have the right amount of indices. Has: {indexLength}. Should have at least 3");
                break;

            case PrimitiveTopology.LineList:
                if (indexLength % 2 != 0)
                    throw new InvalidOperationException($"Line List mesh doesn't have the right amount of indices. Has: {indexLength}. Should be a multiple of 2");
                break;

            case PrimitiveTopology.LineStrip:
                if (indexLength < 2)
                    throw new InvalidOperationException($"Line Strip mesh doesn't have the right amount of indices. Has: {indexLength}. Should have at least 2");
                break;
        }

        RecalculateBufferOffsets();

        // Vertex buffer upload
        vertexBuffer = Graphics.Factory.CreateBuffer(new BufferDescription((uint)BufferLength, BufferUsage.VertexBuffer));

        CommandList list = Graphics.GetCommandList();

        list.UpdateBuffer(vertexBuffer, 0, vertices);

        if (HasUV)
            list.UpdateBuffer(vertexBuffer, (uint)UVStart, uv);

        if (HasUV2)
            list.UpdateBuffer(vertexBuffer, (uint)UV2Start, uv2);

        if (HasNormals)
            list.UpdateBuffer(vertexBuffer, (uint)NormalsStart, normals);

        if (HasColors)
            list.UpdateBuffer(vertexBuffer, (uint)ColorsStart, colors);

        if (HasTangents)
            list.UpdateBuffer(vertexBuffer, (uint)TangentsStart, tangents);

        if (indexFormat == IndexFormat.UInt16)
        {
            indexBuffer = Graphics.Factory.CreateBuffer(new BufferDescription((uint)indices16.Length * sizeof(ushort), BufferUsage.IndexBuffer));
            list.UpdateBuffer(indexBuffer, 0, indices16);
        }
        else if (indexFormat == IndexFormat.UInt32)
        {
            indexBuffer = Graphics.Factory.CreateBuffer(new BufferDescription((uint)indices32.Length * sizeof(uint), BufferUsage.IndexBuffer));
            list.UpdateBuffer(indexBuffer, 0, indices32);
        }

        Graphics.SubmitCommandList(list, false);
    }

    public void SetDrawData(CommandList commandList, ShaderPipeline pipeline)
    {
        Upload();

        commandList.SetIndexBuffer(IndexBuffer, IndexFormat);

        pipeline.BindVertexBuffer(commandList, "POSITION0", VertexBuffer, 0);
        pipeline.BindVertexBuffer(commandList, "TEXCOORD0", VertexBuffer, (uint)UVStart);
        pipeline.BindVertexBuffer(commandList, "TEXCOORD1", VertexBuffer, (uint)UV2Start);
        pipeline.BindVertexBuffer(commandList, "NORMAL0", VertexBuffer, (uint)NormalsStart);
        pipeline.BindVertexBuffer(commandList, "TANGENT0", VertexBuffer, (uint)TangentsStart);
        pipeline.BindVertexBuffer(commandList, "COLOR0", VertexBuffer, (uint)ColorsStart);
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

    public void RecalculateBounds()
    {
        ArgumentNullException.ThrowIfNull(vertices);

        if (vertices.Length < 1)
            throw new ArgumentException();

        var minVec = Vector3F.One * 99999f;
        var maxVec = Vector3F.One * -99999f;
        foreach (var ptVector in vertices)
        {
            minVec.X = (minVec.X < ptVector.X) ? minVec.X : ptVector.X;
            minVec.Y = (minVec.Y < ptVector.Y) ? minVec.Y : ptVector.Y;
            minVec.Z = (minVec.Z < ptVector.Z) ? minVec.Z : ptVector.Z;

            maxVec.X = (maxVec.X > ptVector.X) ? maxVec.X : ptVector.X;
            maxVec.Y = (maxVec.Y > ptVector.Y) ? maxVec.Y : ptVector.Y;
            maxVec.Z = (maxVec.Z > ptVector.Z) ? maxVec.Z : ptVector.Z;
        }

        bounds = new Bounds(minVec, maxVec);
    }

    public void RecalculateNormals()
    {
        if (vertices == null || vertices.Length < 3) return;
        if (indices32 == null || indices32.Length < 3) return;

        var normals = new Vector3F[vertices.Length];

        for (int i = 0; i < indices32.Length; i += 3)
        {
            uint ai = indices32[i];
            uint bi = indices32[i + 1];
            uint ci = indices32[i + 2];

            Vector3F n = Vector3F.Normalize(Vector3F.Cross(
                vertices[bi] - vertices[ai],
                vertices[ci] - vertices[ai]
            ));

            normals[ai] += n;
            normals[bi] += n;
            normals[ci] += n;
        }

        for (int i = 0; i < vertices.Length; i++)
            normals[i] = -Vector3F.Normalize(normals[i]);

        Normals = normals;
    }

    public void RecalculateTangents()
    {
        if (vertices == null || vertices.Length < 3) return;
        if (indices32 == null || indices32.Length < 3) return;
        if (uv == null) return;

        var tangents = new Vector3F[vertices.Length];

        for (int i = 0; i < indices32.Length; i += 3)
        {
            uint ai = indices32[i];
            uint bi = indices32[i + 1];
            uint ci = indices32[i + 2];

            Vector3F edge1 = vertices[bi] - vertices[ai];
            Vector3F edge2 = vertices[ci] - vertices[ai];

            Vector2F deltaUV1 = uv[bi] - uv[ai];
            Vector2F deltaUV2 = uv[ci] - uv[ai];

            float f = 1.0f / (deltaUV1.X * deltaUV2.Y - deltaUV2.X * deltaUV1.Y);

            Vector3F tangent;
            tangent.X = f * (deltaUV2.Y * edge1.X - deltaUV1.Y * edge2.X);
            tangent.Y = f * (deltaUV2.Y * edge1.Y - deltaUV1.Y * edge2.Y);
            tangent.Z = f * (deltaUV2.Y * edge1.Z - deltaUV1.Y * edge2.Z);

            tangents[ai] += tangent;
            tangents[bi] += tangent;
            tangents[ci] += tangent;
        }

        for (int i = 0; i < vertices.Length; i++)
            tangents[i] = Vector3F.Normalize(tangents[i]);

        Tangents = tangents;
    }

    internal void RecalculateBufferOffsets()
    {
        const int floatSize = sizeof(float);

        const int vec2Size = floatSize * 2;
        const int vec3Size = floatSize * 3;
        const int byte4Size = 4;

        int vertLen = vertices.Length;

        UVStart = vertLen * vec3Size;                                                 // Where vertices end
        UV2Start = UVStart + (HasUV ? vertLen * vec2Size : 0);                        // Where UV0 ends
        NormalsStart = UV2Start + (HasUV2 ? vertLen * vec2Size : 0);                  // Where UV1 ends
        TangentsStart = NormalsStart + (HasNormals ? vertLen * vec3Size : 0);         // Where Normals end
        ColorsStart = TangentsStart + (HasTangents ? vertLen * vec3Size : 0);         // Where Tangents end
        BufferLength = ColorsStart + (HasColors ? vertLen * byte4Size : 0);   // Where bone weights end
    }

    public override void OnDispose() => DeleteGPUBuffers();

    private void DeleteGPUBuffers()
    {
        vertexBuffer?.Dispose();
        vertexBuffer = null;
        indexBuffer?.Dispose();
        indexBuffer = null;
    }

    public static Mesh CreateQuad(Vector2 scale)
    {
        Mesh mesh = new Mesh();

        float x = (float)scale.x;
        float y = (float)scale.y;

        mesh.Vertices = [
            new Vector3F(-x, -y, 0),
            new Vector3F(x, -y, 0),
            new Vector3F(-x, y, 0),
            new Vector3F(x, y, 0),
        ];

        mesh.UV = [
            new Vector2F(0, 0),
            new Vector2F(1, 0),
            new Vector2F(0, 1),
            new Vector2F(1, 1),
        ];

        mesh.IndexFormat = IndexFormat.UInt16;
        mesh.Indices16 = [0, 2, 1, 2, 3, 1];

        return mesh;
    }

    public static Mesh CreateSphere(float radius, int rings, int slices)
    {
        Mesh mesh = new Mesh();

        List<Vector3F> vertices = [];
        List<Vector2F> uvs = [];
        List<ushort> indices = [];

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

                vertices.Add(new Vector3F(x, y, z) * radius);
                uvs.Add(new Vector2F(u, v));
            }
        }

        for (int i = 0; i < rings; i++)
        {
            for (int j = 0; j < slices; j++)
            {
                ushort a = (ushort)(i * (slices + 1) + j);
                ushort b = (ushort)(a + slices + 1);

                indices.Add(a);
                indices.Add(b);
                indices.Add((ushort)(a + 1));

                indices.Add(b);
                indices.Add((ushort)(b + 1));
                indices.Add((ushort)(a + 1));
            }
        }

        mesh.Vertices = vertices.ToArray();
        mesh.UV = uvs.ToArray();
        mesh.IndexFormat = IndexFormat.UInt16;
        mesh.Indices16 = indices.ToArray();

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

        Vector3F[] vertices =
        [
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
        ];

        Vector2F[] uvs =
        [
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
        ];

        ushort[] indices =
        [
            1, 2, 0, 0, 2, 3,       // Front face
            5, 4, 6, 6, 4, 7,       // Back face
            9, 8, 10, 10, 8, 11,    // Left face
            13, 12, 14, 14, 12, 15, // Right face
            17, 18, 16, 16, 18, 19, // Top face
            21, 22, 20, 20, 22, 23  // Bottom face
        ];

        mesh.Vertices = vertices;
        mesh.UV = uvs;
        mesh.IndexFormat = IndexFormat.UInt16;
        mesh.Indices16 = indices;

        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();

        return mesh;
    }

    public static Mesh CreateCylinder(float radius, float length, int sliceCount)
    {
#warning TODO: Test, This hasent been tested like at all just assumed it will work
        Mesh mesh = new Mesh();

        List<Vector3F> vertices = [];
        List<Vector2F> uvs = [];
        List<ushort> indices = [];

        float halfLength = length / 2.0f;

        // Create the vertices and UVs for the top and bottom circles
        for (int i = 0; i <= sliceCount; i++)
        {
            float angle = 2 * MathF.PI * i / sliceCount;
            float x = radius * MathF.Cos(angle);
            float z = radius * MathF.Sin(angle);

            // Top circle
            vertices.Add(new Vector3F(x, halfLength, z));
            uvs.Add(new Vector2F((float)i / sliceCount, 1));

            // Bottom circle
            vertices.Add(new Vector3F(x, -halfLength, z));
            uvs.Add(new Vector2F((float)i / sliceCount, 0));
        }

        // Add the center vertices for the top and bottom circles
        vertices.Add(new Vector3F(0, halfLength, 0));
        uvs.Add(new Vector2F(0.5f, 1));
        vertices.Add(new Vector3F(0, -halfLength, 0));
        uvs.Add(new Vector2F(0.5f, 0));

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

            indices.Add((ushort)top1);
            indices.Add((ushort)bottom1);
            indices.Add((ushort)top2);

            indices.Add((ushort)bottom1);
            indices.Add((ushort)bottom2);
            indices.Add((ushort)top2);
        }

        // Create the indices for the top and bottom circles
        for (int i = 0; i < sliceCount; i++)
        {
            int top1 = i * 2;
            int top2 = (i == sliceCount - 1) ? 0 : top1 + 2;
            int bottom1 = top1 + 1;
            int bottom2 = (i == sliceCount - 1) ? 1 : bottom1 + 2;

            // Top circle
            indices.Add((ushort)top1);
            indices.Add((ushort)top2);
            indices.Add((ushort)topCenterIndex);

            // Bottom circle
            indices.Add((ushort)bottom2);
            indices.Add((ushort)bottom1);
            indices.Add((ushort)bottomCenterIndex);
        }

        mesh.Vertices = vertices.ToArray();
        mesh.UV = uvs.ToArray();
        mesh.IndexFormat = IndexFormat.UInt16;
        mesh.Indices16 = indices.ToArray();

        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();

        return mesh;
    }

    public static Mesh CreateTriangle(Vector3 a, Vector3 b, Vector3 c)
    {
        Mesh mesh = new Mesh();
        mesh.Vertices = [a, b, c];
        mesh.IndexFormat = IndexFormat.UInt16;
        mesh.Indices16 = [0, 1, 2];
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        return mesh;
    }

    public SerializedProperty Serialize(Serializer.SerializationContext ctx)
    {
        var compoundTag = SerializedProperty.NewCompound();

        using (MemoryStream memoryStream = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(memoryStream))
        {
            writer.Write((byte)indexFormat);
            writer.Write((byte)topology);

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

            writer.Write(indices32?.Length ?? 0);
            if (indices32 != null)
            {
                foreach (var index in indices32)
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
            topology = (PrimitiveTopology)reader.ReadByte();

            var vertexCount = reader.ReadInt32();
            vertices = new Vector3F[vertexCount];
            for (int i = 0; i < vertexCount; i++)
                vertices[i] = new Vector3F(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

            var normalCount = reader.ReadInt32();
            if (normalCount > 0)
            {
                normals = new Vector3F[normalCount];
                for (int i = 0; i < normalCount; i++)
                    normals[i] = new Vector3F(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            }

            var tangentCount = reader.ReadInt32();
            if (tangentCount > 0)
            {
                tangents = new Vector3F[tangentCount];
                for (int i = 0; i < tangentCount; i++)
                    tangents[i] = new Vector3F(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            }

            var colorCount = reader.ReadInt32();
            if (colorCount > 0)
            {
                colors = new Color32[colorCount];
                for (int i = 0; i < colorCount; i++)
                    colors[i] = new Color32(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte());
            }

            var uvCount = reader.ReadInt32();
            if (uvCount > 0)
            {
                uv = new Vector2F[uvCount];
                for (int i = 0; i < uvCount; i++)
                    uv[i] = new Vector2F(reader.ReadSingle(), reader.ReadSingle());
            }

            var uv2Count = reader.ReadInt32();
            if (uv2Count > 0)
            {
                uv2 = new Vector2F[uv2Count];
                for (int i = 0; i < uv2Count; i++)
                    uv2[i] = new Vector2F(reader.ReadSingle(), reader.ReadSingle());
            }

            var indexCount = reader.ReadInt32();
            if (indexCount > 0)
            {
                indices32 = new uint[indexCount];
                for (int i = 0; i < indexCount; i++)
                    indices32[i] = reader.ReadUInt32();
            }

            var boneIndexCount = reader.ReadInt32();
            if (boneIndexCount > 0)
            {
                boneIndices = new Vector4F[boneIndexCount];
                for (int i = 0; i < boneIndexCount; i++)
                {
                    //boneIndices[i] = new Color32(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte());
                    boneIndices[i] = new Vector4F(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                }
            }

            var boneWeightCount = reader.ReadInt32();
            if (boneWeightCount > 0)
            {
                boneWeights = new Vector4F[boneWeightCount];
                for (int i = 0; i < boneWeightCount; i++)
                    boneWeights[i] = new Vector4F(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            }

            var bindPosesCount = reader.ReadInt32();
            if (bindPosesCount > 0)
            {
                bindPoses = new Matrix4x4F[bindPosesCount];
                for (int i = 0; i < bindPosesCount; i++)
                {
                    bindPoses[i] = new Matrix4x4F()
                    {
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
