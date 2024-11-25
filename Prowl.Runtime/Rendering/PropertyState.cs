// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Prowl.Echo;

using Matrix4x4F = System.Numerics.Matrix4x4;
using Vector2F = System.Numerics.Vector2;
using Vector3F = System.Numerics.Vector3;
using Vector4F = System.Numerics.Vector4;

namespace Prowl.Runtime.Rendering;

public struct ValueProperty
{
    public ValueType type;
    public byte width;
    public byte height;
    public int arraySize;

    public byte[] data;


    public readonly void CopyTo(ref ValueProperty property)
    {
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

public partial class PropertyState
{
    public bool IsEmpty => _values.Count == 0 && _buffers.Count == 0;

    [SerializeIgnore]
    internal Dictionary<string, ValueProperty> _values;

    [SerializeIgnore]
    internal Dictionary<string, (Veldrid.Texture?, Veldrid.Sampler?)> _textures;

    [SerializeIgnore]
    internal Dictionary<string, (Veldrid.DeviceBuffer?, int, int)> _buffers;


    public PropertyState()
    {
        _values = [];
        _textures = [];
        _buffers = [];
    }

    public PropertyState(PropertyState clone)
    {
        _values = new(clone._values);
        _textures = new(clone._textures);
        _buffers = new(clone._buffers);
    }

    public void ApplyOverride(PropertyState overrideState)
    {
        foreach (KeyValuePair<string, ValueProperty> pair in overrideState._values)
        {
            ValueProperty prop = _values.GetValueOrDefault(pair.Key, default);

            pair.Value.CopyTo(ref prop);

            _values[pair.Key] = prop;
        }

        foreach (KeyValuePair<string, (Veldrid.Texture?, Veldrid.Sampler?)> pair in overrideState._textures)
            _textures[pair.Key] = pair.Value;

        foreach (KeyValuePair<string, (Veldrid.DeviceBuffer?, int, int)> pair in overrideState._buffers)
            _buffers[pair.Key] = pair.Value;
    }


    public void SetColor(string name, Color value)
        => WriteData(name, value, ValueType.Float, 4, 1);

    public void SetVector(string name, Vector2F value)
        => WriteData(name, value, ValueType.Float, 2, 1);

    public void SetVector(string name, Vector3F value)
        => WriteData(name, value, ValueType.Float, 3, 1);

    public void SetVector(string name, Vector4F value)
        => WriteData(name, value, ValueType.Float, 4, 1);

    public void SetFloat(string name, float value)
        => WriteData(name, value, ValueType.Float, 1, 1);

    public void SetInt(string name, int value)
        => WriteData(name, value, ValueType.Int, 1, 1);

    public void SetMatrix(string name, Matrix4x4F value)
        => WriteData(name, value, ValueType.Float, 4, 4);
    public void SetTexture(string name, Texture value)
        => _textures[name] = (value.InternalTexture, value.Sampler.InternalSampler);

    public void SetRawTexture(string name, Veldrid.Texture value, Veldrid.Sampler? sampler = null)
        => _textures[name] = (value, sampler);

    public void SetBuffer(string name, GraphicsBuffer value, int start = 0, int length = -1)
        => _buffers[name] = (value.Buffer, start, length);

    public void SetRawBuffer(string name, Veldrid.DeviceBuffer value, int start = 0, int length = -1)
        => _buffers[name] = (value, start, length);

    public void SetIntArray(string name, int[] values)
        => WriteData(name, values, ValueType.Int, 1, 1);

    public void SetFloatArray(string name, float[] values)
        => WriteData(name, values, ValueType.Float, 1, 1);

    public void SetVectorArray(string name, Vector2F[] values)
        => WriteData(name, values, ValueType.Float, 2, 1);

    public void SetVectorArray(string name, Vector3F[] values)
        => WriteData(name, values, ValueType.Float, 3, 1);

    public void SetVectorArray(string name, Vector4F[] values)
        => WriteData(name, values, ValueType.Float, 4, 1);

    public void SetColorArray(string name, Color[] values)
        => WriteData(name, values, ValueType.Float, 4, 1);

    public void SetMatrixArray(string name, Matrix4x4F[] values)
        => WriteData(name, values, ValueType.Float, 4, 4);


    public void Clear()
    {
        _values.Clear();
        _buffers.Clear();
    }

    private unsafe void WriteData<T>(string name, T newData, ValueType type, int width, int height) where T : unmanaged
    {
        int tSize = sizeof(T);

        ValueProperty property = _values.GetValueOrDefault(name, default);

        property.type = type;
        property.arraySize = 0;
        property.width = (byte)width;
        property.height = (byte)height;

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

        ValueProperty property = _values.GetValueOrDefault(name, default);

        property.type = type;
        property.arraySize = 0;
        property.width = (byte)width;
        property.height = (byte)height;

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
