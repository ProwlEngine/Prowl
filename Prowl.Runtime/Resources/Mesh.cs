// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;

using Veldrid;

using Prowl.Runtime.Rendering;

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
        get => _indexFormat;
        set
        {
            if (isWritable == false || _indexFormat == value) return;

            _changed = true;
            _indexFormat = value;

            if (value == IndexFormat.UInt16)
                _indices32 = [];
            else
                _indices16 = [];
        }
    }

    /// <summary> The mesh's primitive type </summary>
    public PrimitiveTopology Topology
    {
        get => _topology;
        set
        {
            if (isWritable == false) return;
            _changed = true;
            _topology = value;
        }
    }

    /// <summary>
    /// Sets or gets the current vertices.
    /// Getting depends on isReadable.
    /// Note: When setting, if the vertex count is different than previous, it'll reset all other vertex data fields.
    /// </summary>
    public Vector3F[] Vertices
    {
        get => _vertices ?? [];
        set
        {
            if (isWritable == false)
                return;
            bool needsReset = _vertices == null || _vertices.Length != value.Length;
            _vertices = value;
            _changed = true;
            if (needsReset)
            {
                _normals = null;
                _tangents = null;
                _colors = null;
                _uv = null;
                _uv2 = null;
                _indices32 = null;
            }
        }
    }

    public Vector3F[] Normals
    {
        get => ReadVertexData(_normals ?? []);
        set => WriteVertexData(ref _normals, value, value.Length);
    }

    public Vector3F[] Tangents
    {
        get => ReadVertexData(_tangents ?? []);
        set => WriteVertexData(ref _tangents, value, value.Length);
    }

    public Color32[] Colors
    {
        get => ReadVertexData(_colors ?? []);
        set => WriteVertexData(ref _colors, value, value.Length);
    }

    public Vector2F[] UV
    {
        get => ReadVertexData(_uv ?? []);
        set => WriteVertexData(ref _uv, value, value.Length);
    }

    public Vector2F[] UV2
    {
        get => ReadVertexData(_uv2 ?? []);
        set => WriteVertexData(ref _uv2, value, value.Length);
    }

    public uint[] Indices32
    {
        get => ReadVertexData(_indices32 ?? []);
        set => WriteVertexData(ref _indices32, value, value.Length, false);
    }

    public ushort[] Indices16
    {
        get => ReadVertexData(_indices16 ?? []);
        set => WriteVertexData(ref _indices16, value, value.Length, false);
    }

    public Vector4F[] BoneIndices
    {
        get => ReadVertexData(_boneIndices ?? []);
        set => WriteVertexData(ref _boneIndices, value, value.Length);
    }

    public Vector4F[] BoneWeights
    {
        get => ReadVertexData(_boneWeights ?? []);
        set => WriteVertexData(ref _boneWeights, value, value.Length);
    }

    public int VertexCount => _vertices?.Length ?? 0;
    public int IndexCount => IndexFormat == IndexFormat.UInt16 ? _indices16.Length : _indices32.Length;

    public DeviceBuffer VertexBuffer => _vertexBuffer;
    public DeviceBuffer IndexBuffer => _indexBuffer;

    public bool HasNormals => (_normals?.Length ?? 0) > 0;
    public bool HasTangents => (_tangents?.Length ?? 0) > 0;
    public bool HasColors => (_colors?.Length ?? 0) > 0;
    public bool HasUV => (_uv?.Length ?? 0) > 0;
    public bool HasUV2 => (_uv2?.Length ?? 0) > 0;

    public bool HasBoneIndices => (_boneIndices?.Length ?? 0) > 0;
    public bool HasBoneWeights => (_boneWeights?.Length ?? 0) > 0;

    public Matrix4x4F[]? bindPoses;

    private bool _changed;
    private Vector3F[]? _vertices;
    private Vector3F[]? _normals;
    private Vector3F[]? _tangents;
    private Color32[]? _colors;
    private Vector2F[]? _uv;
    private Vector2F[]? _uv2;

    private uint[]? _indices32;
    private ushort[]? _indices16;

    private Vector4F[]? _boneIndices;
    private Vector4F[]? _boneWeights;

    private IndexFormat _indexFormat = IndexFormat.UInt16;
    private PrimitiveTopology _topology = PrimitiveTopology.TriangleList;


    private DeviceBuffer _vertexBuffer;
    private DeviceBuffer _indexBuffer;

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

    public Mesh()
    {
        _changed = true;
    }

    public void Clear()
    {
        _vertices = null;
        _normals = null;
        _colors = null;
        _uv = null;
        _uv2 = null;
        _indices16 = null;
        _indices32 = null;
        _tangents = null;
        _boneIndices = null;
        _boneWeights = null;

        _changed = true;

        DeleteGPUBuffers();
    }

    public void Upload()
    {
        if (_changed == false && _indexBuffer != null && _vertexBuffer != null)
            return;

        _changed = false;

        DeleteGPUBuffers();

        if (_vertices == null || _vertices.Length == 0)
            throw new InvalidOperationException($"Mesh has no vertices");

        if (_indexFormat == IndexFormat.UInt16)
        {
            if (_indices16 == null || _indices16.Length == 0)
                throw new InvalidOperationException($"Mesh has no indices");
        }
        else if (_indices32 == null || _indices32.Length == 0)
        {
            throw new InvalidOperationException($"Mesh has no indices");
        }

        int indexLength = _indexFormat == IndexFormat.UInt16 ? _indices16.Length : _indices32.Length;
        switch (_topology)
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
        _vertexBuffer = Graphics.Factory.CreateBuffer(new BufferDescription((uint)BufferLength, BufferUsage.VertexBuffer));

        Graphics.Device.UpdateBuffer(_vertexBuffer, 0, _vertices);

        if (HasUV)
            Graphics.Device.UpdateBuffer(_vertexBuffer, (uint)UVStart, _uv);

        if (HasUV2)
            Graphics.Device.UpdateBuffer(_vertexBuffer, (uint)UV2Start, _uv2);

        if (HasNormals)
            Graphics.Device.UpdateBuffer(_vertexBuffer, (uint)NormalsStart, _normals);

        if (HasColors)
            Graphics.Device.UpdateBuffer(_vertexBuffer, (uint)ColorsStart, _colors);

        if (HasTangents)
            Graphics.Device.UpdateBuffer(_vertexBuffer, (uint)TangentsStart, _tangents);

        if (_indexFormat == IndexFormat.UInt16)
        {
            _indexBuffer = Graphics.Factory.CreateBuffer(new BufferDescription((uint)_indices16.Length * sizeof(ushort), BufferUsage.IndexBuffer));
            Graphics.Device.UpdateBuffer(_indexBuffer, 0, _indices16);
        }
        else if (_indexFormat == IndexFormat.UInt32)
        {
            _indexBuffer = Graphics.Factory.CreateBuffer(new BufferDescription((uint)_indices32.Length * sizeof(uint), BufferUsage.IndexBuffer));
            Graphics.Device.UpdateBuffer(_indexBuffer, 0, _indices32);
        }
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
        if ((value == null || length == 0 || length != (_vertices?.Length ?? 0)) && mustMatchLength)
            throw new ArgumentException("Array length should match vertices length");
        _changed = true;
        target = value;
    }

    public void RecalculateBounds()
    {
        ArgumentNullException.ThrowIfNull(_vertices);

        if (_vertices.Length < 1)
            throw new ArgumentException();

        Vector3F minVec = Vector3F.One * float.MaxValue;
        Vector3F maxVec = Vector3F.One * float.MinValue;
        foreach (Vector3F ptVector in _vertices)
        {
            minVec = Vector3.Min(minVec, ptVector);
            maxVec = Vector3.Max(maxVec, ptVector);
        }

        bounds = Bounds.CreateFromMinMax(minVec, maxVec);
    }

    public void RecalculateNormals()
    {
        if (_vertices == null || _vertices.Length < 3)
            return;

        if (_indices32 == null || _indices32.Length < 3)
            return;

        var normals = new Vector3F[_vertices.Length];

        for (int i = 0; i < _indices32.Length; i += 3)
        {
            uint ai = _indices32[i];
            uint bi = _indices32[i + 1];
            uint ci = _indices32[i + 2];

            Vector3F n = Vector3F.Normalize(Vector3F.Cross(
                _vertices[bi] - _vertices[ai],
                _vertices[ci] - _vertices[ai]
            ));

            normals[ai] += n;
            normals[bi] += n;
            normals[ci] += n;
        }

        for (int i = 0; i < _vertices.Length; i++)
            normals[i] = -Vector3F.Normalize(normals[i]);

        Normals = normals;
    }

    public void RecalculateTangents()
    {
        if (_vertices == null || _vertices.Length < 3)
            return;

        if (_indices32 == null || _indices32.Length < 3)
            return;

        if (_uv == null)
            return;

        var tangents = new Vector3F[_vertices.Length];

        for (int i = 0; i < _indices32.Length; i += 3)
        {
            uint ai = _indices32[i];
            uint bi = _indices32[i + 1];
            uint ci = _indices32[i + 2];

            Vector3F edge1 = _vertices[bi] - _vertices[ai];
            Vector3F edge2 = _vertices[ci] - _vertices[ai];

            Vector2F deltaUV1 = _uv[bi] - _uv[ai];
            Vector2F deltaUV2 = _uv[ci] - _uv[ai];

            float f = 1.0f / (deltaUV1.X * deltaUV2.Y - deltaUV2.X * deltaUV1.Y);

            Vector3F tangent;
            tangent.X = f * (deltaUV2.Y * edge1.X - deltaUV1.Y * edge2.X);
            tangent.Y = f * (deltaUV2.Y * edge1.Y - deltaUV1.Y * edge2.Y);
            tangent.Z = f * (deltaUV2.Y * edge1.Z - deltaUV1.Y * edge2.Z);

            tangents[ai] += tangent;
            tangents[bi] += tangent;
            tangents[ci] += tangent;
        }

        for (int i = 0; i < _vertices.Length; i++)
            tangents[i] = Vector3F.Normalize(tangents[i]);

        Tangents = tangents;
    }

    internal void RecalculateBufferOffsets()
    {
        const int floatSize = sizeof(float);

        const int vec2Size = floatSize * 2;
        const int vec3Size = floatSize * 3;
        const int byte4Size = 4;

        int vertLen = _vertices.Length;

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
        _vertexBuffer?.Dispose();
        _vertexBuffer = null;
        _indexBuffer?.Dispose();
        _indexBuffer = null;
    }

    public static Mesh CreateQuad(Vector2 scale)
    {
        Mesh mesh = new();

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
        Mesh mesh = new();

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

        mesh.Vertices = [.. vertices];
        mesh.UV = [.. uvs];
        mesh.IndexFormat = IndexFormat.UInt16;
        mesh.Indices16 = [.. indices];

        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();

        return mesh;
    }

    public static Mesh CreateCube(Vector3 size)
    {
        Mesh mesh = new();
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
        // TODO: Test, This hasent been tested like at all just assumed it will work
        Mesh mesh = new();

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

        mesh.Vertices = [.. vertices];
        mesh.UV = [.. uvs];
        mesh.IndexFormat = IndexFormat.UInt16;
        mesh.Indices16 = [.. indices];

        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();

        return mesh;
    }

    public static Mesh CreateTriangle(Vector3 a, Vector3 b, Vector3 c)
    {
        Mesh mesh = new()
        {
            Vertices = [a, b, c],
            IndexFormat = IndexFormat.UInt16,
            Indices16 = [0, 1, 2]
        };
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        return mesh;
    }

    public SerializedProperty Serialize(Serializer.SerializationContext ctx)
    {
        var compoundTag = SerializedProperty.NewCompound();

        using (MemoryStream memoryStream = new())
        using (BinaryWriter writer = new(memoryStream))
        {
            writer.Write((byte)_indexFormat);
            writer.Write((byte)_topology);

            WriteArray(writer, _vertices);
            WriteArray(writer, _normals);
            WriteArray(writer, _tangents);
            WriteArray(writer, _colors);
            WriteArray(writer, _uv);
            WriteArray(writer, _uv2);
            WriteArray(writer, _indices16);
            WriteArray(writer, _indices32);
            WriteArray(writer, _boneIndices);
            WriteArray(writer, _boneWeights);

            WriteArray(writer, bindPoses);

            compoundTag.Add("MeshData", new SerializedProperty(memoryStream.ToArray()));
        }

        return compoundTag;
    }

    private static unsafe void WriteArray<T>(BinaryWriter writer, T[]? data) where T : unmanaged
    {
        if (data == null)
        {
            writer.Write(0);
            return;
        }

        writer.Write(data.Length);

        fixed (T* dataPtr = data)
            writer.Write(new Span<byte>(dataPtr, sizeof(T) * data.Length));
    }

    public void Deserialize(SerializedProperty value, Serializer.SerializationContext ctx)
    {
        using (MemoryStream memoryStream = new(value["MeshData"].ByteArrayValue))
        using (BinaryReader reader = new(memoryStream))
        {
            _indexFormat = (IndexFormat)reader.ReadByte();
            _topology = (PrimitiveTopology)reader.ReadByte();

            _vertices = ReadArray<Vector3F>(reader);
            _normals = ReadArray<Vector3F>(reader);
            _tangents = ReadArray<Vector3F>(reader);
            _colors = ReadArray<Color32>(reader);
            _uv = ReadArray<Vector2F>(reader);
            _uv2 = ReadArray<Vector2F>(reader);
            _indices16 = ReadArray<ushort>(reader);
            _indices32 = ReadArray<uint>(reader);
            _boneIndices = ReadArray<Vector4F>(reader);
            _boneWeights = ReadArray<Vector4F>(reader);

            bindPoses = ReadArray<Matrix4x4F>(reader);

            _changed = true;
        }
    }

    private static unsafe T[] ReadArray<T>(BinaryReader reader) where T : unmanaged
    {
        int count = reader.ReadInt32();

        if (count == 0)
            return [];

        int size = sizeof(T) * count;
        byte[] bytes = reader.ReadBytes(size);

        T[] vals = new T[count];

        fixed (byte* bytesPtr = bytes)
        fixed (T* valsPtr = vals)
            Buffer.MemoryCopy(bytesPtr, valsPtr, size, size);

        return vals;
    }
}
