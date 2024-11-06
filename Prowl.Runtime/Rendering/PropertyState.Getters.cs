// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Matrix4x4F = System.Numerics.Matrix4x4;
using Vector2F = System.Numerics.Vector2;
using Vector3F = System.Numerics.Vector3;
using Vector4F = System.Numerics.Vector4;

namespace Prowl.Runtime.Rendering;

public partial class PropertyState
{
    public bool TryGetColor(string name, out Color value)
    {
        if (_values.TryGetValue(name, out ValueProperty prop) && prop.type == ValueType.Float && prop.width == 4 && prop.height == 1)
        {
            value = MemoryMarshal.Read<Color>(prop.data);
            return true;
        }
        value = default;
        return false;
    }

    public bool TryGetVector2(string name, out Vector2F value)
    {
        if (_values.TryGetValue(name, out ValueProperty prop) && prop.type == ValueType.Float && prop.width == 2 && prop.height == 1)
        {
            value = MemoryMarshal.Read<Vector2F>(prop.data);
            return true;
        }
        value = default;
        return false;
    }

    public bool TryGetVector3(string name, out Vector3F value)
    {
        if (_values.TryGetValue(name, out ValueProperty prop) && prop.type == ValueType.Float && prop.width == 3 && prop.height == 1)
        {
            value = MemoryMarshal.Read<Vector3F>(prop.data);
            return true;
        }
        value = default;
        return false;
    }

    public bool TryGetVector4(string name, out Vector4F value)
    {
        if (_values.TryGetValue(name, out ValueProperty prop) && prop.type == ValueType.Float && prop.width == 4 && prop.height == 1)
        {
            value = MemoryMarshal.Read<Vector4F>(prop.data);
            return true;
        }
        value = default;
        return false;
    }

    public bool TryGetFloat(string name, out float value)
    {
        if (_values.TryGetValue(name, out ValueProperty prop) && prop.type == ValueType.Float && prop.width == 1 && prop.height == 1)
        {
            value = MemoryMarshal.Read<float>(prop.data);
            return true;
        }
        value = default;
        return false;
    }

    public bool TryGetInt(string name, out int value)
    {
        if (_values.TryGetValue(name, out ValueProperty prop) && prop.type == ValueType.Int && prop.width == 1 && prop.height == 1)
        {
            value = MemoryMarshal.Read<int>(prop.data);
            return true;
        }
        value = default;
        return false;
    }

    public bool TryGetMatrix(string name, out Matrix4x4F value)
    {
        if (_values.TryGetValue(name, out ValueProperty prop) && prop.type == ValueType.Float && prop.width == 4 && prop.height == 4)
        {
            value = MemoryMarshal.Read<Matrix4x4F>(prop.data);
            return true;
        }
        value = default;
        return false;
    }

    public bool TryGetTexture(string name, out (Veldrid.Texture?, Veldrid.Sampler?) value)
    {
        return _textures.TryGetValue(name, out value);
    }

    public bool TryGetBuffer(string name, out (Veldrid.DeviceBuffer?, int, int) value)
    {
        return _buffers.TryGetValue(name, out value);
    }

    public bool TryGetIntArray(string name, out int[] value)
    {
        if (_values.TryGetValue(name, out ValueProperty prop) && prop.type == ValueType.Int && prop.width == 1 && prop.height == 1)
        {
            int length = prop.data.Length / sizeof(int);
            value = new int[length];
            Buffer.BlockCopy(prop.data, 0, value, 0, prop.data.Length);
            return true;
        }
        value = Array.Empty<int>();
        return false;
    }

    public bool TryGetFloatArray(string name, out float[] value)
    {
        if (_values.TryGetValue(name, out ValueProperty prop) && prop.type == ValueType.Float && prop.width == 1 && prop.height == 1)
        {
            int length = prop.data.Length / sizeof(float);
            value = new float[length];
            Buffer.BlockCopy(prop.data, 0, value, 0, prop.data.Length);
            return true;
        }
        value = Array.Empty<float>();
        return false;
    }

    public bool TryGetVector2Array(string name, out Vector2F[] value)
    {
        if (_values.TryGetValue(name, out ValueProperty prop) && prop.type == ValueType.Float && prop.width == 2 && prop.height == 1)
        {
            int length = prop.data.Length / (sizeof(float) * 2);
            value = new Vector2F[length];
            Buffer.BlockCopy(prop.data, 0, value, 0, prop.data.Length);
            return true;
        }
        value = Array.Empty<Vector2F>();
        return false;
    }

    public bool TryGetVector3Array(string name, out Vector3F[] value)
    {
        if (_values.TryGetValue(name, out ValueProperty prop) && prop.type == ValueType.Float && prop.width == 3 && prop.height == 1)
        {
            int length = prop.data.Length / (sizeof(float) * 3);
            value = new Vector3F[length];
            Buffer.BlockCopy(prop.data, 0, value, 0, prop.data.Length);
            return true;
        }
        value = Array.Empty<Vector3F>();
        return false;
    }

    public bool TryGetVector4Array(string name, out Vector4F[] value)
    {
        if (_values.TryGetValue(name, out ValueProperty prop) && prop.type == ValueType.Float && prop.width == 4 && prop.height == 1)
        {
            int length = prop.data.Length / (sizeof(float) * 4);
            value = new Vector4F[length];
            Buffer.BlockCopy(prop.data, 0, value, 0, prop.data.Length);
            return true;
        }
        value = Array.Empty<Vector4F>();
        return false;
    }

    public bool TryGetColorArray(string name, out Color[] value)
    {
        if (_values.TryGetValue(name, out ValueProperty prop) && prop.type == ValueType.Float && prop.width == 4 && prop.height == 1)
        {
            int length = prop.data.Length / (sizeof(float) * 4);
            value = new Color[length];
            Buffer.BlockCopy(prop.data, 0, value, 0, prop.data.Length);
            return true;
        }
        value = Array.Empty<Color>();
        return false;
    }

    public bool TryGetMatrixArray(string name, out Matrix4x4F[] value)
    {
        if (_values.TryGetValue(name, out ValueProperty prop) && prop.type == ValueType.Float && prop.width == 4 && prop.height == 4)
        {
            int length = prop.data.Length / (sizeof(float) * 16);
            value = new Matrix4x4F[length];
            Buffer.BlockCopy(prop.data, 0, value, 0, prop.data.Length);
            return true;
        }
        value = Array.Empty<Matrix4x4F>();
        return false;
    }

    // Convenience methods that throw if the value doesn't exist
    public Color GetColor(string name) => TryGetColor(name, out Color value) ? value : throw new KeyNotFoundException($"Color property '{name}' not found");
    public Vector2F GetVector2(string name) => TryGetVector2(name, out Vector2F value) ? value : throw new KeyNotFoundException($"Vector2 property '{name}' not found");
    public Vector3F GetVector3(string name) => TryGetVector3(name, out Vector3F value) ? value : throw new KeyNotFoundException($"Vector3 property '{name}' not found");
    public Vector4F GetVector4(string name) => TryGetVector4(name, out Vector4F value) ? value : throw new KeyNotFoundException($"Vector4 property '{name}' not found");
    public float GetFloat(string name) => TryGetFloat(name, out float value) ? value : throw new KeyNotFoundException($"Float property '{name}' not found");
    public int GetInt(string name) => TryGetInt(name, out int value) ? value : throw new KeyNotFoundException($"Int property '{name}' not found");
    public Matrix4x4F GetMatrix(string name) => TryGetMatrix(name, out Matrix4x4F value) ? value : throw new KeyNotFoundException($"Matrix property '{name}' not found");
    public int[] GetIntArray(string name) => TryGetIntArray(name, out int[] value) ? value : throw new KeyNotFoundException($"Int array property '{name}' not found");
    public float[] GetFloatArray(string name) => TryGetFloatArray(name, out float[] value) ? value : throw new KeyNotFoundException($"Float array property '{name}' not found");
    public Vector2F[] GetVector2Array(string name) => TryGetVector2Array(name, out Vector2F[] value) ? value : throw new KeyNotFoundException($"Vector2 array property '{name}' not found");
    public Vector3F[] GetVector3Array(string name) => TryGetVector3Array(name, out Vector3F[] value) ? value : throw new KeyNotFoundException($"Vector3 array property '{name}' not found");
    public Vector4F[] GetVector4Array(string name) => TryGetVector4Array(name, out Vector4F[] value) ? value : throw new KeyNotFoundException($"Vector4 array property '{name}' not found");
    public Color[] GetColorArray(string name) => TryGetColorArray(name, out Color[] value) ? value : throw new KeyNotFoundException($"Color array property '{name}' not found");
    public Matrix4x4F[] GetMatrixArray(string name) => TryGetMatrixArray(name, out Matrix4x4F[] value) ? value : throw new KeyNotFoundException($"Matrix array property '{name}' not found");
}
