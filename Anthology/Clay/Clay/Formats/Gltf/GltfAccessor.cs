using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Prowl.Vector;

namespace Prowl.Clay.Formats.Gltf;

/// <summary>
/// Reads typed values from a glTF accessor, applying byte stride and sparse overlay.
/// </summary>
internal sealed class GltfAccessorReader
{
    private readonly GltfBufferStore _buffers;
    private readonly GltfDom _dom;

    public GltfAccessorReader(GltfDom dom, GltfBufferStore buffers)
    {
        _dom = dom;
        _buffers = buffers;
    }

    public GltfAccessorJson Get(int index)
    {
        if (_dom.Accessors is null || (uint)index >= (uint)_dom.Accessors.Length)
            throw new ImportException($"Missing accessor index {index}.");
        return _dom.Accessors[index];
    }

    public uint[] ReadUInts(int index)
    {
        var a = Get(index);
        if (a.Type != "SCALAR")
            throw new ImportException($"Accessor {index} expected SCALAR for index reads, got {a.Type}.");
        uint[] result = new uint[a.Count];
        DecodeScalarUInts(a, result);
        return result;
    }

    public Float2[] ReadVec2(int index)
    {
        var a = Get(index);
        EnsureType(a, "VEC2");
        Float2[] result = new Float2[a.Count];
        DecodeFloats(a, 2, MemoryMarshal.Cast<Float2, float>(result.AsSpan()));
        return result;
    }

    public Float3[] ReadVec3(int index)
    {
        var a = Get(index);
        EnsureType(a, "VEC3");
        Float3[] result = new Float3[a.Count];
        DecodeFloats(a, 3, MemoryMarshal.Cast<Float3, float>(result.AsSpan()));
        return result;
    }

    public Float4[] ReadVec4(int index)
    {
        var a = Get(index);
        EnsureType(a, "VEC4");
        Float4[] result = new Float4[a.Count];
        DecodeFloats(a, 4, MemoryMarshal.Cast<Float4, float>(result.AsSpan()));
        return result;
    }

    public Color[] ReadColor(int index)
    {
        var a = Get(index);
        int comps = a.Type switch
        {
            "VEC3" => 3,
            "VEC4" => 4,
            _ => throw new ImportException($"Accessor {index} expected VEC3/VEC4 for colors, got {a.Type}."),
        };
        float[] tmp = new float[a.Count * comps];
        DecodeFloats(a, comps, tmp);

        Color[] result = new Color[a.Count];
        for (int i = 0; i < a.Count; i++)
        {
            float r = tmp[i * comps + 0];
            float g = tmp[i * comps + 1];
            float b = tmp[i * comps + 2];
            float al = comps == 4 ? tmp[i * comps + 3] : 1f;
            result[i] = new Color(r, g, b, al);
        }
        return result;
    }

    /// <summary>Reads a SCALAR float accessor as a packed float array.</summary>
    public float[] ReadFloats1D(int index)
    {
        var a = Get(index);
        if (a.Type != "SCALAR")
            throw new ImportException($"Accessor {index} expected SCALAR for ReadFloats1D, got {a.Type}.");
        float[] result = new float[a.Count];
        DecodeFloats(a, 1, result);
        return result;
    }

    /// <summary>Reads any-typed numeric accessor as a flat float array; the caller specifies the
    /// component count per element. Useful for animation outputs where the dimension is known
    /// from the channel path.</summary>
    public float[] ReadFloats1DComponents(int index, int expectedComponents)
    {
        var a = Get(index);
        int comps = a.Type switch
        {
            "SCALAR" => 1,
            "VEC2" => 2,
            "VEC3" => 3,
            "VEC4" => 4,
            _ => throw new ImportException($"Accessor {index} type {a.Type} not supported as flat floats."),
        };
        if (comps != expectedComponents)
            throw new ImportException(
                $"Accessor {index} has {comps} components, caller expected {expectedComponents}.");
        float[] result = new float[a.Count * comps];
        DecodeFloats(a, comps, result);
        return result;
    }

    public Float4x4[] ReadMat4(int index)
    {
        var a = Get(index);
        EnsureType(a, "MAT4");
        float[] tmp = new float[a.Count * 16];
        DecodeFloats(a, 16, tmp);

        Float4x4[] result = new Float4x4[a.Count];
        for (int i = 0; i < a.Count; i++)
        {
            int o = i * 16;
            // glTF stores matrices as 16 floats in column-major order.
            result[i] = new Float4x4(
                new Float4(tmp[o + 0],  tmp[o + 1],  tmp[o + 2],  tmp[o + 3]),
                new Float4(tmp[o + 4],  tmp[o + 5],  tmp[o + 6],  tmp[o + 7]),
                new Float4(tmp[o + 8],  tmp[o + 9],  tmp[o + 10], tmp[o + 11]),
                new Float4(tmp[o + 12], tmp[o + 13], tmp[o + 14], tmp[o + 15]));
        }
        return result;
    }

    private static void EnsureType(GltfAccessorJson a, string expected)
    {
        if (a.Type != expected)
            throw new ImportException($"Accessor expected {expected}, got {a.Type}.");
    }

    private void DecodeFloats(GltfAccessorJson a, int componentsPerElement, Span<float> dst)
    {
        int total = a.Count * componentsPerElement;
        if (dst.Length != total)
            throw new ImportException(
                $"Destination float buffer length {dst.Length} mismatched accessor element count {total}.");

        if (a.BufferView is { } viewIndex)
        {
            var view = _buffers.GetBufferView(viewIndex);
            ReadOnlySpan<byte> data = _buffers.GetBufferView(view);
            int compBytes = ComponentByteSize(a.ComponentType);
            int elementBytes = compBytes * componentsPerElement;
            int stride = view.ByteStride is { } s && s > 0 ? s : elementBytes;
            int byteOffset = a.ByteOffset;

            for (int i = 0; i < a.Count; i++)
            {
                int srcOff = byteOffset + i * stride;
                int dstOff = i * componentsPerElement;
                for (int c = 0; c < componentsPerElement; c++)
                    dst[dstOff + c] = ReadComponentAsFloat(data, srcOff + c * compBytes, a.ComponentType, a.Normalized);
            }
        }
        else
        {
            dst.Clear();
        }

        if (a.Sparse is not null)
            ApplySparseFloat(a, a.Sparse, componentsPerElement, dst);
    }

    private void DecodeScalarUInts(GltfAccessorJson a, Span<uint> dst)
    {
        if (a.BufferView is { } viewIndex)
        {
            var view = _buffers.GetBufferView(viewIndex);
            ReadOnlySpan<byte> data = _buffers.GetBufferView(view);
            int compBytes = ComponentByteSize(a.ComponentType);
            int stride = view.ByteStride is { } s && s > 0 ? s : compBytes;
            int byteOffset = a.ByteOffset;

            for (int i = 0; i < a.Count; i++)
            {
                int srcOff = byteOffset + i * stride;
                dst[i] = ReadComponentAsUInt(data, srcOff, a.ComponentType);
            }
        }
        else
        {
            dst.Clear();
        }

        if (a.Sparse is not null)
            ApplySparseScalarUInt(a, a.Sparse, dst);
    }

    private void ApplySparseFloat(GltfAccessorJson parent, GltfAccessorSparse sparse, int componentsPerElement, Span<float> dst)
    {
        var indicesView = _buffers.GetBufferView(sparse.Indices.BufferView);
        var valuesView = _buffers.GetBufferView(sparse.Values.BufferView);
        ReadOnlySpan<byte> idxData = _buffers.GetBufferView(indicesView);
        ReadOnlySpan<byte> valData = _buffers.GetBufferView(valuesView);
        int idxCompBytes = ComponentByteSize(sparse.Indices.ComponentType);
        int valCompBytes = ComponentByteSize(parent.ComponentType);
        int valuesElementBytes = valCompBytes * componentsPerElement;

        for (int i = 0; i < sparse.Count; i++)
        {
            int idxOff = sparse.Indices.ByteOffset + i * idxCompBytes;
            uint logicalIndex = ReadComponentAsUInt(idxData, idxOff, sparse.Indices.ComponentType);
            int valOff = sparse.Values.ByteOffset + i * valuesElementBytes;
            int dstOff = (int)logicalIndex * componentsPerElement;
            for (int c = 0; c < componentsPerElement; c++)
                dst[dstOff + c] = ReadComponentAsFloat(valData, valOff + c * valCompBytes, parent.ComponentType, parent.Normalized);
        }
    }

    private void ApplySparseScalarUInt(GltfAccessorJson parent, GltfAccessorSparse sparse, Span<uint> dst)
    {
        var indicesView = _buffers.GetBufferView(sparse.Indices.BufferView);
        var valuesView = _buffers.GetBufferView(sparse.Values.BufferView);
        ReadOnlySpan<byte> idxData = _buffers.GetBufferView(indicesView);
        ReadOnlySpan<byte> valData = _buffers.GetBufferView(valuesView);
        int idxCompBytes = ComponentByteSize(sparse.Indices.ComponentType);
        int valCompBytes = ComponentByteSize(parent.ComponentType);

        for (int i = 0; i < sparse.Count; i++)
        {
            int idxOff = sparse.Indices.ByteOffset + i * idxCompBytes;
            uint logicalIndex = ReadComponentAsUInt(idxData, idxOff, sparse.Indices.ComponentType);
            int valOff = sparse.Values.ByteOffset + i * valCompBytes;
            dst[(int)logicalIndex] = ReadComponentAsUInt(valData, valOff, parent.ComponentType);
        }
    }

    private static int ComponentByteSize(int componentType) => componentType switch
    {
        GltfComponentType.Byte => 1,
        GltfComponentType.UnsignedByte => 1,
        GltfComponentType.Short => 2,
        GltfComponentType.UnsignedShort => 2,
        GltfComponentType.UnsignedInt => 4,
        GltfComponentType.Float => 4,
        _ => throw new ImportException($"Unknown component type {componentType}."),
    };

    private static uint ReadComponentAsUInt(ReadOnlySpan<byte> data, int offset, int componentType) => componentType switch
    {
        GltfComponentType.UnsignedByte => data[offset],
        GltfComponentType.UnsignedShort => BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2)),
        GltfComponentType.UnsignedInt => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4)),
        GltfComponentType.Byte => (uint)(sbyte)data[offset],
        GltfComponentType.Short => (uint)BinaryPrimitives.ReadInt16LittleEndian(data.Slice(offset, 2)),
        _ => throw new ImportException($"Component type {componentType} not valid for integer reads."),
    };

    private static float ReadComponentAsFloat(ReadOnlySpan<byte> data, int offset, int componentType, bool normalized) => componentType switch
    {
        GltfComponentType.Float => BinaryPrimitives.ReadSingleLittleEndian(data.Slice(offset, 4)),
        GltfComponentType.UnsignedByte => normalized ? data[offset] / 255f : data[offset],
        GltfComponentType.Byte => normalized ? MathF.Max((sbyte)data[offset] / 127f, -1f) : (sbyte)data[offset],
        GltfComponentType.UnsignedShort => normalized
            ? BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2)) / 65535f
            : BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2)),
        GltfComponentType.Short => normalized
            ? MathF.Max(BinaryPrimitives.ReadInt16LittleEndian(data.Slice(offset, 2)) / 32767f, -1f)
            : BinaryPrimitives.ReadInt16LittleEndian(data.Slice(offset, 2)),
        GltfComponentType.UnsignedInt => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4)),
        _ => throw new ImportException($"Unknown component type {componentType} for float read."),
    };
}
