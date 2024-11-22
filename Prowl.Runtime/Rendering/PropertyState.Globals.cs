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
    internal static Dictionary<string, ValueProperty> _globalValues;
    internal static Dictionary<string, (Veldrid.Texture?, Veldrid.Sampler?)> _globalTextures;
    internal static Dictionary<string, (Veldrid.DeviceBuffer?, int, int)> _globalBuffers;

    static PropertyState()
    {
        _globalValues = [];
        _globalTextures = [];
        _globalBuffers = [];
    }


    #region Write Global Data

    public static void SetGlobalColor(string name, Color value)
        => WriteGlobalData(name, value, ValueType.Float, 4, 1);

    public static void SetGlobalVector(string name, Vector2F value)
        => WriteGlobalData(name, value, ValueType.Float, 2, 1);

    public static void SetGlobalVector(string name, Vector3F value)
        => WriteGlobalData(name, value, ValueType.Float, 3, 1);

    public static void SetGlobalVector(string name, Vector4F value)
        => WriteGlobalData(name, value, ValueType.Float, 4, 1);

    public static void SetGlobalFloat(string name, float value)
        => WriteGlobalData(name, value, ValueType.Float, 1, 1);

    public static void SetGlobalInt(string name, int value)
        => WriteGlobalData(name, value, ValueType.Int, 1, 1);

    public static void SetGlobalMatrix(string name, Matrix4x4F value)
        => WriteGlobalData(name, value, ValueType.Float, 4, 4);

    public static void SetGlobalTexture(string name, Texture value)
        => _globalTextures[name] = (value.InternalTexture, value.Sampler.InternalSampler);

    public static void SetGlobalRawTexture(string name, Veldrid.Texture value, Veldrid.Sampler? sampler = null)
        => _globalTextures[name] = (value, sampler);

    public static void SetGlobalBuffer(string name, GraphicsBuffer value, int start = 0, int length = -1)
        => _globalBuffers[name] = (value.Buffer, start, length);

    public static void SetGlobalRawBuffer(string name, Veldrid.DeviceBuffer value, int start = 0, int length = -1)
        => _globalBuffers[name] = (value, start, length);

    public static void SetGlobalIntArray(string name, int[] values)
        => WriteGlobalData(name, values, ValueType.Int, 1, 1);

    public static void SetGlobalFloatArray(string name, float[] values)
        => WriteGlobalData(name, values, ValueType.Float, 1, 1);

    public static void SetGlobalVectorArray(string name, Vector2F[] values)
        => WriteGlobalData(name, values, ValueType.Float, 2, 1);

    public static void SetGlobalVectorArray(string name, Vector3F[] values)
        => WriteGlobalData(name, values, ValueType.Float, 3, 1);

    public static void SetGlobalVectorArray(string name, Vector4F[] values)
        => WriteGlobalData(name, values, ValueType.Float, 4, 1);

    public static void SetGlobalColorArray(string name, Color[] values)
        => WriteGlobalData(name, values, ValueType.Float, 4, 1);

    public static void SetGlobalMatrixArray(string name, Matrix4x4F[] values)
        => WriteGlobalData(name, values, ValueType.Float, 4, 4);

    public static void ClearGlobalData()
    {
        _globalValues.Clear();
        _globalBuffers.Clear();
        _globalTextures.Clear();
    }

    private static unsafe void WriteGlobalData<T>(string name, T newData, ValueType type, int width, int height) where T : unmanaged
    {
        int tSize = sizeof(T);

        ValueProperty property = _globalValues.GetValueOrDefault(name, default);

        property.type = type;
        property.arraySize = 0;
        property.width = (byte)width;
        property.height = (byte)height;

        if (property.data == null || property.data.Length < tSize || (tSize < 16 && property.data.Length != tSize))
        {
            property.data = new byte[tSize];

            _globalValues[name] = property;
        }

        MemoryMarshal.Write(new Span<byte>(property.data), newData);
    }

    private static unsafe void WriteGlobalData<T>(string name, T[] newData, ValueType type, int width, int height) where T : unmanaged
    {
        int tSize = sizeof(T) * newData.Length;

        ValueProperty property = _globalValues.GetValueOrDefault(name, default);

        property.type = type;
        property.arraySize = 0;
        property.width = (byte)width;
        property.height = (byte)height;

        if (property.data == null || property.data.Length < tSize || (tSize < 16 && property.data.Length != tSize))
        {
            property.data = new byte[tSize];

            _globalValues[name] = property;
        }

        fixed (T* dataPtr = newData)
        fixed (byte* valuePtr = property.data)
            Buffer.MemoryCopy(dataPtr, valuePtr, property.data.Length, tSize);
    }

    #endregion

    #region Read Global Data

    public static bool TryGetGlobalColor(string name, out Color value)
    {
        if (_globalValues.TryGetValue(name, out ValueProperty prop) && prop.type == ValueType.Float && prop.width == 4 && prop.height == 1)
        {
            value = MemoryMarshal.Read<Color>(prop.data);
            return true;
        }
        value = default;
        return false;
    }

    public static bool TryGetGlobalVector2(string name, out Vector2F value)
    {
        if (_globalValues.TryGetValue(name, out ValueProperty prop) && prop.type == ValueType.Float && prop.width == 2 && prop.height == 1)
        {
            value = MemoryMarshal.Read<Vector2F>(prop.data);
            return true;
        }
        value = default;
        return false;
    }

    public static bool TryGetGlobalVector3(string name, out Vector3F value)
    {
        if (_globalValues.TryGetValue(name, out ValueProperty prop) && prop.type == ValueType.Float && prop.width == 3 && prop.height == 1)
        {
            value = MemoryMarshal.Read<Vector3F>(prop.data);
            return true;
        }
        value = default;
        return false;
    }

    public static bool TryGetGlobalVector4(string name, out Vector4F value)
    {
        if (_globalValues.TryGetValue(name, out ValueProperty prop) && prop.type == ValueType.Float && prop.width == 4 && prop.height == 1)
        {
            value = MemoryMarshal.Read<Vector4F>(prop.data);
            return true;
        }
        value = default;
        return false;
    }

    public static bool TryGetGlobalFloat(string name, out float value)
    {
        if (_globalValues.TryGetValue(name, out ValueProperty prop) && prop.type == ValueType.Float && prop.width == 1 && prop.height == 1)
        {
            value = MemoryMarshal.Read<float>(prop.data);
            return true;
        }
        value = default;
        return false;
    }

    public static bool TryGetGlobalInt(string name, out int value)
    {
        if (_globalValues.TryGetValue(name, out ValueProperty prop) && prop.type == ValueType.Int && prop.width == 1 && prop.height == 1)
        {
            value = MemoryMarshal.Read<int>(prop.data);
            return true;
        }
        value = default;
        return false;
    }

    public static bool TryGetGlobalMatrix(string name, out Matrix4x4F value)
    {
        if (_globalValues.TryGetValue(name, out ValueProperty prop) && prop.type == ValueType.Float && prop.width == 4 && prop.height == 4)
        {
            value = MemoryMarshal.Read<Matrix4x4F>(prop.data);
            return true;
        }
        value = default;
        return false;
    }

    public static bool TryGetGlobalTexture(string name, out (Veldrid.Texture?, Veldrid.Sampler?) value)
    {
        return _globalTextures.TryGetValue(name, out value);
    }

    public static bool TryGetGlobalBuffer(string name, out (Veldrid.DeviceBuffer?, int, int) value)
    {
        return _globalBuffers.TryGetValue(name, out value);
    }

    public static bool TryGetGlobalIntArray(string name, out int[] value)
    {
        if (_globalValues.TryGetValue(name, out ValueProperty prop) && prop.type == ValueType.Int && prop.width == 1 && prop.height == 1)
        {
            int length = prop.data.Length / sizeof(int);
            value = new int[length];
            Buffer.BlockCopy(prop.data, 0, value, 0, prop.data.Length);
            return true;
        }
        value = Array.Empty<int>();
        return false;
    }

    public static bool TryGetGlobalFloatArray(string name, out float[] value)
    {
        if (_globalValues.TryGetValue(name, out ValueProperty prop) && prop.type == ValueType.Float && prop.width == 1 && prop.height == 1)
        {
            int length = prop.data.Length / sizeof(float);
            value = new float[length];
            Buffer.BlockCopy(prop.data, 0, value, 0, prop.data.Length);
            return true;
        }
        value = Array.Empty<float>();
        return false;
    }

    public static bool TryGetGlobalVector2Array(string name, out Vector2F[] value)
    {
        if (_globalValues.TryGetValue(name, out ValueProperty prop) && prop.type == ValueType.Float && prop.width == 2 && prop.height == 1)
        {
            int length = prop.data.Length / (sizeof(float) * 2);
            value = new Vector2F[length];
            Buffer.BlockCopy(prop.data, 0, value, 0, prop.data.Length);
            return true;
        }
        value = Array.Empty<Vector2F>();
        return false;
    }

    public static bool TryGetGlobalVector3Array(string name, out Vector3F[] value)
    {
        if (_globalValues.TryGetValue(name, out ValueProperty prop) && prop.type == ValueType.Float && prop.width == 3 && prop.height == 1)
        {
            int length = prop.data.Length / (sizeof(float) * 3);
            value = new Vector3F[length];
            Buffer.BlockCopy(prop.data, 0, value, 0, prop.data.Length);
            return true;
        }
        value = Array.Empty<Vector3F>();
        return false;
    }

    public static bool TryGetGlobalVector4Array(string name, out Vector4F[] value)
    {
        if (_globalValues.TryGetValue(name, out ValueProperty prop) && prop.type == ValueType.Float && prop.width == 4 && prop.height == 1)
        {
            int length = prop.data.Length / (sizeof(float) * 4);
            value = new Vector4F[length];
            Buffer.BlockCopy(prop.data, 0, value, 0, prop.data.Length);
            return true;
        }
        value = Array.Empty<Vector4F>();
        return false;
    }

    public static bool TryGetGlobalColorArray(string name, out Color[] value)
    {
        if (_globalValues.TryGetValue(name, out ValueProperty prop) && prop.type == ValueType.Float && prop.width == 4 && prop.height == 1)
        {
            int length = prop.data.Length / (sizeof(float) * 4);
            value = new Color[length];
            Buffer.BlockCopy(prop.data, 0, value, 0, prop.data.Length);
            return true;
        }
        value = Array.Empty<Color>();
        return false;
    }

    public static bool TryGetGlobalMatrixArray(string name, out Matrix4x4F[] value)
    {
        if (_globalValues.TryGetValue(name, out ValueProperty prop) && prop.type == ValueType.Float && prop.width == 4 && prop.height == 4)
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
    public static Color GetGlobalColor(string name) => TryGetGlobalColor(name, out Color value) ? value : throw new KeyNotFoundException($"Global color property '{name}' not found");
    public static Vector2F GetGlobalVector2(string name) => TryGetGlobalVector2(name, out Vector2F value) ? value : throw new KeyNotFoundException($"Global vector2 property '{name}' not found");
    public static Vector3F GetGlobalVector3(string name) => TryGetGlobalVector3(name, out Vector3F value) ? value : throw new KeyNotFoundException($"Global vector3 property '{name}' not found");
    public static Vector4F GetGlobalVector4(string name) => TryGetGlobalVector4(name, out Vector4F value) ? value : throw new KeyNotFoundException($"Global vector4 property '{name}' not found");
    public static float GetGlobalFloat(string name) => TryGetGlobalFloat(name, out float value) ? value : throw new KeyNotFoundException($"Global float property '{name}' not found");
    public static int GetGlobalInt(string name) => TryGetGlobalInt(name, out int value) ? value : throw new KeyNotFoundException($"Global int property '{name}' not found");
    public static Matrix4x4F GetGlobalMatrix(string name) => TryGetGlobalMatrix(name, out Matrix4x4F value) ? value : throw new KeyNotFoundException($"Global matrix property '{name}' not found");
    public static int[] GetGlobalIntArray(string name) => TryGetGlobalIntArray(name, out int[] value) ? value : throw new KeyNotFoundException($"Global int array property '{name}' not found");
    public static float[] GetGlobalFloatArray(string name) => TryGetGlobalFloatArray(name, out float[] value) ? value : throw new KeyNotFoundException($"Global float array property '{name}' not found");
    public static Vector2F[] GetGlobalVector2Array(string name) => TryGetGlobalVector2Array(name, out Vector2F[] value) ? value : throw new KeyNotFoundException($"Global vector2 array property '{name}' not found");
    public static Vector3F[] GetGlobalVector3Array(string name) => TryGetGlobalVector3Array(name, out Vector3F[] value) ? value : throw new KeyNotFoundException($"Global vector3 array property '{name}' not found");
    public static Vector4F[] GetGlobalVector4Array(string name) => TryGetGlobalVector4Array(name, out Vector4F[] value) ? value : throw new KeyNotFoundException($"Global vector4 array property '{name}' not found");
    public static Color[] GetGlobalColorArray(string name) => TryGetGlobalColorArray(name, out Color[] value) ? value : throw new KeyNotFoundException($"Global color array property '{name}' not found");
    public static Matrix4x4F[] GetGlobalMatrixArray(string name) => TryGetGlobalMatrixArray(name, out Matrix4x4F[] value) ? value : throw new KeyNotFoundException($"Global matrix array property '{name}' not found");

    #endregion
}
