// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Matrix4x4F = System.Numerics.Matrix4x4;
using Vector2F = System.Numerics.Vector2;
using Vector3F = System.Numerics.Vector3;
using Vector4F = System.Numerics.Vector4;

namespace Prowl.Runtime;

public struct Property
{
    public AssetRef<Texture>? texture;

    public ValueType type;
    public byte width;
    public byte height;
    public int arraySize;

    public byte[] data;


    public void CopyTo(ref Property property)
    {
        property.texture = texture;

        property.type = type;
        property.arraySize = arraySize;
        property.width = width;
        property.height = height;

        if ((data == null) != (property.data == null))
            property.data = null;

        if (data != null)
        {
            if (property.data == null || property.data.Length != data.Length)
                property.data = new byte[data.Length];

            Buffer.BlockCopy(data, 0, property.data, 0, data.Length);
        }
    }
}

public class PropertyState
{

    public bool IsEmpty => _values.Count == 0 && _buffers.Count == 0;

    [SerializeField] internal readonly Dictionary<string, Property> _values;

    internal readonly Dictionary<string, Veldrid.Texture?> _rawTextures;
    internal readonly Dictionary<string, GraphicsBuffer?> _buffers;



    public PropertyState()
    {
        _values = new();
        _rawTextures = new();
        _buffers = new();
    }

    public PropertyState(PropertyState clone)
    {
        _values = new(clone._values);
        _rawTextures = new(clone._rawTextures);
        _buffers = new(clone._buffers);
    }

    public void ApplyOverride(PropertyState overrideState)
    {
        foreach (var pair in overrideState._values)
        {
            Property prop = _values.GetValueOrDefault(pair.Key, default);

            pair.Value.CopyTo(ref prop);

            _values[pair.Key] = prop;
        }

        foreach (var pair in overrideState._rawTextures)
            _rawTextures[pair.Key] = pair.Value;

        foreach (var pair in overrideState._buffers)
            _buffers[pair.Key] = pair.Value;
    }


    public void SetColor(string name, Color value) => WriteData(name, value, ValueType.Float, 4, 1);
    public void SetVector(string name, Vector2F value) => WriteData(name, value, ValueType.Float, 2, 1);
    public void SetVector(string name, Vector3F value) => WriteData(name, value, ValueType.Float, 3, 1);
    public void SetVector(string name, Vector4F value) => WriteData(name, value, ValueType.Float, 4, 1);
    public void SetFloat(string name, float value) => WriteData(name, value, ValueType.Float, 1, 1);
    public void SetInt(string name, int value) => WriteData(name, value, ValueType.Int, 1, 1);
    public void SetMatrix(string name, Matrix4x4F value) => WriteData(name, value, ValueType.Float, 4, 4);
    public void SetTexture(string name, AssetRef<Texture> value) => _values[name] = new Property { texture = value };
    public void SetRawTexture(string name, Veldrid.Texture value) => _rawTextures[name] = value;
    public void SetBuffer(string name, GraphicsBuffer value) => _buffers[name] = value;

    public void SetIntArray(string name, int[] values) => WriteData(name, values, ValueType.Int, 1, 1);
    public void SetFloatArray(string name, float[] values) => WriteData(name, values, ValueType.Float, 1, 1);
    public void SetVectorArray(string name, Vector2F[] values) => WriteData(name, values, ValueType.Float, 2, 1);
    public void SetVectorArray(string name, Vector3F[] values) => WriteData(name, values, ValueType.Float, 3, 1);
    public void SetVectorArray(string name, Vector4F[] values) => WriteData(name, values, ValueType.Float, 4, 1);
    public void SetColorArray(string name, Color[] values) => WriteData(name, values, ValueType.Float, 4, 1);
    public void SetMatrixArray(string name, Matrix4x4F[] values) => WriteData(name, values, ValueType.Float, 4, 4);


    public void Clear()
    {
        _values.Clear();
        _buffers.Clear();
    }


    private unsafe void WriteData<T>(string name, T newData, ValueType type, int width, int height) where T : unmanaged
    {
        int tSize = sizeof(T);

        Property property = _values.GetValueOrDefault(name, default);

        property.type = type;
        property.arraySize = 0;
        property.width = (byte)width;
        property.height = (byte)height;
        property.texture = null;

        // If the previous value was larger than a vector4, and the new value isn't
        // the array size will be downgraded to save on memory footprint.
        if (property.data == null || property.data.Length < tSize || (tSize < 16 && property.data.Length != tSize))
        {
            property.data = new byte[tSize];

            _values[name] = property;
        }

        MemoryMarshal.Write(new Span<byte>(property.data), newData);
    }


    private unsafe void WriteData<T>(string name, T[] newData, ValueType type, int width, int height) where T : unmanaged
    {
        int tSize = sizeof(T) * newData.Length;

        Property property = _values.GetValueOrDefault(name, default);

        property.type = type;
        property.arraySize = 0;
        property.width = (byte)width;
        property.height = (byte)height;
        property.texture = null;

        // If the previous value was larger than a vector4, and the new value isn't
        // the array size will be downgraded to save on memory footprint.
        if (property.data == null || property.data.Length < tSize || (tSize < 16 && property.data.Length != tSize))
        {
            property.data = new byte[tSize];

            _values[name] = property;
        }


        fixed (T* dataPtr = newData)
        fixed (byte* valuePtr = property.data)
            Buffer.MemoryCopy(dataPtr, valuePtr, property.data.Length, tSize);
    }
}
