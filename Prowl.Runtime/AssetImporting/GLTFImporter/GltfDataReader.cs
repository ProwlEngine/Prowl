using System;
using System.Buffers.Binary;
using Prowl.Vector;

namespace Prowl.Runtime.AssetImporting.Gltf;

/// <summary>
/// Reads typed arrays from the GLTF Buffer → BufferView → Accessor pipeline.
/// </summary>
public static class GltfDataReader
{
    public static float[] ReadScalars(GltfFile file, int accessorIndex)
    {
        var accessor = file.Root.Accessors[accessorIndex];
        int count = accessor.Count;
        var result = new float[count];

        if (accessor.BufferView == null)
            return result;

        var span = GetAccessorSpan(file, accessor, out int stride);
        int compSize = ComponentSize(accessor.ComponentType);
        bool normalize = accessor.Normalized == true;

        for (int i = 0; i < count; i++)
        {
            int offset = i * stride;
            result[i] = ReadComponentAsFloat(span, offset, accessor.ComponentType, normalize);
        }

        ApplySparseScalar(file, accessor, result);
        return result;
    }

    public static Float2[] ReadVec2(GltfFile file, int accessorIndex)
    {
        var accessor = file.Root.Accessors[accessorIndex];
        int count = accessor.Count;
        var result = new Float2[count];

        if (accessor.BufferView == null)
            return result;

        var span = GetAccessorSpan(file, accessor, out int stride);
        int compSize = ComponentSize(accessor.ComponentType);
        bool normalize = accessor.Normalized == true;

        for (int i = 0; i < count; i++)
        {
            int offset = i * stride;
            float x = ReadComponentAsFloat(span, offset, accessor.ComponentType, normalize);
            float y = ReadComponentAsFloat(span, offset + compSize, accessor.ComponentType, normalize);
            result[i] = new Float2(x, y);
        }

        ApplySparseVecN(file, accessor, 2, (values, idx) =>
        {
            result[idx] = new Float2(values[0], values[1]);
        });
        return result;
    }

    public static Float3[] ReadVec3(GltfFile file, int accessorIndex)
    {
        var accessor = file.Root.Accessors[accessorIndex];
        int count = accessor.Count;
        var result = new Float3[count];

        if (accessor.BufferView == null)
            return result;

        var span = GetAccessorSpan(file, accessor, out int stride);
        int compSize = ComponentSize(accessor.ComponentType);
        bool normalize = accessor.Normalized == true;

        for (int i = 0; i < count; i++)
        {
            int offset = i * stride;
            float x = ReadComponentAsFloat(span, offset, accessor.ComponentType, normalize);
            float y = ReadComponentAsFloat(span, offset + compSize, accessor.ComponentType, normalize);
            float z = ReadComponentAsFloat(span, offset + compSize * 2, accessor.ComponentType, normalize);
            result[i] = new Float3(x, y, z);
        }

        ApplySparseVecN(file, accessor, 3, (values, idx) =>
        {
            result[idx] = new Float3(values[0], values[1], values[2]);
        });
        return result;
    }

    public static Float4[] ReadVec4(GltfFile file, int accessorIndex)
    {
        var accessor = file.Root.Accessors[accessorIndex];
        int count = accessor.Count;
        var result = new Float4[count];

        if (accessor.BufferView == null)
            return result;

        var span = GetAccessorSpan(file, accessor, out int stride);
        int compSize = ComponentSize(accessor.ComponentType);
        bool normalize = accessor.Normalized == true;

        for (int i = 0; i < count; i++)
        {
            int offset = i * stride;
            float x = ReadComponentAsFloat(span, offset, accessor.ComponentType, normalize);
            float y = ReadComponentAsFloat(span, offset + compSize, accessor.ComponentType, normalize);
            float z = ReadComponentAsFloat(span, offset + compSize * 2, accessor.ComponentType, normalize);
            float w = ReadComponentAsFloat(span, offset + compSize * 3, accessor.ComponentType, normalize);
            result[i] = new Float4(x, y, z, w);
        }

        ApplySparseVecN(file, accessor, 4, (values, idx) =>
        {
            result[idx] = new Float4(values[0], values[1], values[2], values[3]);
        });
        return result;
    }

    public static Float4x4[] ReadMat4(GltfFile file, int accessorIndex)
    {
        var accessor = file.Root.Accessors[accessorIndex];
        int count = accessor.Count;
        var result = new Float4x4[count];

        if (accessor.BufferView == null)
            return result;

        var span = GetAccessorSpan(file, accessor, out int stride);
        int compSize = ComponentSize(accessor.ComponentType);
        bool normalize = accessor.Normalized == true;

        for (int i = 0; i < count; i++)
        {
            int offset = i * stride;
            float[] m = new float[16];
            for (int j = 0; j < 16; j++)
                m[j] = ReadComponentAsFloat(span, offset + compSize * j, accessor.ComponentType, normalize);

            // GLTF stores matrices in column-major order
            result[i] = new Float4x4(
                new Float4(m[0], m[1], m[2], m[3]),
                new Float4(m[4], m[5], m[6], m[7]),
                new Float4(m[8], m[9], m[10], m[11]),
                new Float4(m[12], m[13], m[14], m[15])
            );
        }

        // Sparse support for MAT4 is rare but handle it
        if (accessor.Sparse != null)
        {
            var sparse = accessor.Sparse;
            int[] sparseIndices = ReadSparseIndices(file, sparse);
            var valuesView = file.Root.BufferViews[sparse.Values.BufferView];
            var valuesBuffer = file.Buffers[valuesView.Buffer];
            int valOffset = valuesView.ByteOffset + sparse.Values.ByteOffset;
            int valStride = compSize * 16;

            for (int s = 0; s < sparse.Count; s++)
            {
                int vOff = valOffset + s * valStride;
                float[] m = new float[16];
                for (int j = 0; j < 16; j++)
                    m[j] = ReadComponentAsFloat(valuesBuffer.AsSpan(), vOff + compSize * j, accessor.ComponentType, normalize);

                result[sparseIndices[s]] = new Float4x4(
                    new Float4(m[0], m[1], m[2], m[3]),
                    new Float4(m[4], m[5], m[6], m[7]),
                    new Float4(m[8], m[9], m[10], m[11]),
                    new Float4(m[12], m[13], m[14], m[15])
                );
            }
        }

        return result;
    }

    public static uint[] ReadIndices(GltfFile file, int accessorIndex)
    {
        var accessor = file.Root.Accessors[accessorIndex];
        int count = accessor.Count;
        var result = new uint[count];

        if (accessor.BufferView == null)
            return result;

        var span = GetAccessorSpan(file, accessor, out int stride);

        for (int i = 0; i < count; i++)
        {
            int offset = i * stride;
            result[i] = accessor.ComponentType switch
            {
                5121 => span[offset],                                           // unsigned byte
                5123 => BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(offset, 2)), // unsigned short
                5125 => BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset, 4)), // unsigned int
                _ => throw new NotSupportedException($"Unsupported index component type: {accessor.ComponentType}")
            };
        }

        // Sparse support for indices accessor
        if (accessor.Sparse != null)
        {
            var sparse = accessor.Sparse;
            int[] sparseIndices = ReadSparseIndices(file, sparse);
            var valuesView = file.Root.BufferViews[sparse.Values.BufferView];
            var valuesBuffer = file.Buffers[valuesView.Buffer];
            int valOffset = valuesView.ByteOffset + sparse.Values.ByteOffset;
            int compSize = ComponentSize(accessor.ComponentType);

            for (int s = 0; s < sparse.Count; s++)
            {
                int vOff = valOffset + s * compSize;
                result[sparseIndices[s]] = accessor.ComponentType switch
                {
                    5121 => valuesBuffer[vOff],
                    5123 => BinaryPrimitives.ReadUInt16LittleEndian(valuesBuffer.AsSpan(vOff, 2)),
                    5125 => BinaryPrimitives.ReadUInt32LittleEndian(valuesBuffer.AsSpan(vOff, 4)),
                    _ => throw new NotSupportedException($"Unsupported index component type: {accessor.ComponentType}")
                };
            }
        }

        return result;
    }

    public static Color[] ReadColors(GltfFile file, int accessorIndex)
    {
        var accessor = file.Root.Accessors[accessorIndex];
        int count = accessor.Count;
        var result = new Color[count];
        int compCount = TypeComponentCount(accessor.Type); // 3 for VEC3, 4 for VEC4

        if (accessor.BufferView == null)
        {
            for (int i = 0; i < count; i++)
                result[i] = new Color(0, 0, 0, 1);
            return result;
        }

        var span = GetAccessorSpan(file, accessor, out int stride);
        int compSize = ComponentSize(accessor.ComponentType);

        for (int i = 0; i < count; i++)
        {
            int offset = i * stride;
            float r = ReadColorComponent(span, offset, accessor.ComponentType);
            float g = ReadColorComponent(span, offset + compSize, accessor.ComponentType);
            float b = ReadColorComponent(span, offset + compSize * 2, accessor.ComponentType);
            float a = 1.0f;
            if (compCount >= 4)
                a = ReadColorComponent(span, offset + compSize * 3, accessor.ComponentType);

            result[i] = new Color(r, g, b, a);
        }

        // Sparse support for colors
        if (accessor.Sparse != null)
        {
            var sparse = accessor.Sparse;
            int[] sparseIndices = ReadSparseIndices(file, sparse);
            var valuesView = file.Root.BufferViews[sparse.Values.BufferView];
            var valuesBuffer = file.Buffers[valuesView.Buffer];
            int valOffset = valuesView.ByteOffset + sparse.Values.ByteOffset;
            int valStride = compSize * compCount;

            for (int s = 0; s < sparse.Count; s++)
            {
                int vOff = valOffset + s * valStride;
                ReadOnlySpan<byte> vSpan = valuesBuffer.AsSpan();
                float r = ReadColorComponent(vSpan, vOff, accessor.ComponentType);
                float g = ReadColorComponent(vSpan, vOff + compSize, accessor.ComponentType);
                float b = ReadColorComponent(vSpan, vOff + compSize * 2, accessor.ComponentType);
                float a = 1.0f;
                if (compCount >= 4)
                    a = ReadColorComponent(vSpan, vOff + compSize * 3, accessor.ComponentType);

                result[sparseIndices[s]] = new Color(r, g, b, a);
            }
        }

        return result;
    }

    // ─── Helpers ───────────────────────────────────────────────────────

    private static ReadOnlySpan<byte> GetAccessorSpan(GltfFile file, GltfAccessor accessor, out int stride)
    {
        var bufferView = file.Root.BufferViews[accessor.BufferView!.Value];
        var buffer = file.Buffers[bufferView.Buffer];
        int baseOffset = bufferView.ByteOffset + accessor.ByteOffset;
        int compSize = ComponentSize(accessor.ComponentType);
        int compCount = TypeComponentCount(accessor.Type);
        stride = bufferView.ByteStride ?? (compSize * compCount);
        return buffer.AsSpan(baseOffset);
    }

    private static int ComponentSize(int componentType)
    {
        return componentType switch
        {
            5120 => 1, // sbyte
            5121 => 1, // byte
            5122 => 2, // short
            5123 => 2, // ushort
            5125 => 4, // uint
            5126 => 4, // float
            _ => throw new NotSupportedException($"Unknown GLTF component type: {componentType}")
        };
    }

    private static int TypeComponentCount(string type)
    {
        return type switch
        {
            "SCALAR" => 1,
            "VEC2" => 2,
            "VEC3" => 3,
            "VEC4" => 4,
            "MAT4" => 16,
            _ => throw new NotSupportedException($"Unknown GLTF accessor type: {type}")
        };
    }

    /// <summary>
    /// Reads a single component from the buffer and returns it as a float.
    /// Applies normalization if requested.
    /// </summary>
    private static float ReadComponentAsFloat(ReadOnlySpan<byte> span, int offset, int componentType, bool normalize)
    {
        switch (componentType)
        {
            case 5120: // sbyte
            {
                sbyte v = (sbyte)span[offset];
                return normalize ? MathF.Max(v / 127.0f, -1.0f) : v;
            }
            case 5121: // byte
            {
                byte v = span[offset];
                return normalize ? v / 255.0f : v;
            }
            case 5122: // short
            {
                short v = BinaryPrimitives.ReadInt16LittleEndian(span.Slice(offset, 2));
                return normalize ? MathF.Max(v / 32767.0f, -1.0f) : v;
            }
            case 5123: // ushort
            {
                ushort v = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(offset, 2));
                return normalize ? v / 65535.0f : v;
            }
            case 5125: // uint
            {
                uint v = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset, 4));
                return normalize ? v / 4294967295.0f : v;
            }
            case 5126: // float
            {
                return BinaryPrimitives.ReadSingleLittleEndian(span.Slice(offset, 4));
            }
            default:
                throw new NotSupportedException($"Unknown GLTF component type: {componentType}");
        }
    }

    /// <summary>
    /// Reads a color component with implicit normalization for integer types.
    /// Colors always normalize integers regardless of the Normalized flag.
    /// </summary>
    private static float ReadColorComponent(ReadOnlySpan<byte> span, int offset, int componentType)
    {
        return componentType switch
        {
            5121 => span[offset] / 255.0f,                                                    // byte → [0,1]
            5123 => BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(offset, 2)) / 65535.0f, // ushort → [0,1]
            5126 => BinaryPrimitives.ReadSingleLittleEndian(span.Slice(offset, 4)),            // float direct
            _ => throw new NotSupportedException($"Unsupported color component type: {componentType}")
        };
    }

    private static int[] ReadSparseIndices(GltfFile file, GltfSparse sparse)
    {
        var indicesView = file.Root.BufferViews[sparse.Indices.BufferView];
        var indicesBuffer = file.Buffers[indicesView.Buffer];
        int indOffset = indicesView.ByteOffset + sparse.Indices.ByteOffset;
        int indCompSize = ComponentSize(sparse.Indices.ComponentType);
        var result = new int[sparse.Count];

        ReadOnlySpan<byte> span = indicesBuffer.AsSpan();
        for (int s = 0; s < sparse.Count; s++)
        {
            int off = indOffset + s * indCompSize;
            result[s] = sparse.Indices.ComponentType switch
            {
                5121 => span[off],
                5123 => BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(off, 2)),
                5125 => (int)BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(off, 4)),
                _ => throw new NotSupportedException($"Unsupported sparse index component type: {sparse.Indices.ComponentType}")
            };
        }

        return result;
    }

    /// <summary>
    /// Applies sparse overrides for scalar accessors.
    /// </summary>
    private static void ApplySparseScalar(GltfFile file, GltfAccessor accessor, float[] result)
    {
        if (accessor.Sparse == null) return;

        var sparse = accessor.Sparse;
        int[] sparseIndices = ReadSparseIndices(file, sparse);
        var valuesView = file.Root.BufferViews[sparse.Values.BufferView];
        var valuesBuffer = file.Buffers[valuesView.Buffer];
        int valOffset = valuesView.ByteOffset + sparse.Values.ByteOffset;
        int compSize = ComponentSize(accessor.ComponentType);
        bool normalize = accessor.Normalized == true;

        ReadOnlySpan<byte> span = valuesBuffer.AsSpan();
        for (int s = 0; s < sparse.Count; s++)
        {
            int vOff = valOffset + s * compSize;
            result[sparseIndices[s]] = ReadComponentAsFloat(span, vOff, accessor.ComponentType, normalize);
        }
    }

    /// <summary>
    /// Applies sparse overrides for vector (VEC2/VEC3/VEC4) accessors.
    /// </summary>
    private static void ApplySparseVecN(GltfFile file, GltfAccessor accessor, int compCount, Action<float[], int> assign)
    {
        if (accessor.Sparse == null) return;

        var sparse = accessor.Sparse;
        int[] sparseIndices = ReadSparseIndices(file, sparse);
        var valuesView = file.Root.BufferViews[sparse.Values.BufferView];
        var valuesBuffer = file.Buffers[valuesView.Buffer];
        int valOffset = valuesView.ByteOffset + sparse.Values.ByteOffset;
        int compSize = ComponentSize(accessor.ComponentType);
        bool normalize = accessor.Normalized == true;
        int valStride = compSize * compCount;

        ReadOnlySpan<byte> span = valuesBuffer.AsSpan();
        float[] values = new float[compCount];

        for (int s = 0; s < sparse.Count; s++)
        {
            int vOff = valOffset + s * valStride;
            for (int c = 0; c < compCount; c++)
                values[c] = ReadComponentAsFloat(span, vOff + c * compSize, accessor.ComponentType, normalize);

            assign(values, sparseIndices[s]);
        }
    }
}
