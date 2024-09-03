using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Matrix4x4F = System.Numerics.Matrix4x4;
using Vector2F = System.Numerics.Vector2;
using Vector3F = System.Numerics.Vector3;
using Vector4F = System.Numerics.Vector4;

namespace Prowl.Runtime
{
    public class PropertyState
    {
        [SerializeField] internal Dictionary<string, byte[]> _values;
        [SerializeField] internal Dictionary<string, AssetRef<Texture>> _textures;

        internal Dictionary<string, GraphicsBuffer?> _buffers;

        public PropertyState()
        {
            _values = new();
            _textures = new();
            _buffers = new();
        }

        public PropertyState(PropertyState clone)
        {
            _values = new(clone._values);
            _textures = new(clone._textures);
            _buffers = new(clone._buffers);
        }

        public void ApplyOverride(PropertyState overrideState)
        {
            foreach (var pair in overrideState._values)
            {
                byte[] value = _values.GetValueOrDefault(pair.Key, new byte[pair.Value.Length]);

                if (value.Length != pair.Value.Length)
                {
                    value = new byte[pair.Value.Length];
                    _values[pair.Key] = value;
                }

                Buffer.BlockCopy(pair.Value, 0, value, 0, value.Length);
            }

            foreach (var pair in overrideState._textures)
                _textures[pair.Key] = pair.Value;

            foreach (var pair in overrideState._buffers)
                _buffers[pair.Key] = pair.Value;
        }


        public void SetColor(string name, Color value) => WriteData(name, value);
        public void SetVector(string name, Vector2F value) => WriteData(name, value);
        public void SetVector(string name, Vector3F value) => WriteData(name, value);
        public void SetVector(string name, Vector4F value) => WriteData(name, value);
        public void SetFloat(string name, float value) => WriteData(name, value);
        public void SetInt(string name, int value) => WriteData(name, value);
        public void SetMatrix(string name, Matrix4x4F value) => WriteData(name, value);
        public void SetTexture(string name, AssetRef<Texture> value) => _textures[name] = value;
        public void SetBuffer(string name, GraphicsBuffer value) => _buffers[name] = value;


        public void Clear()
        {
            _values.Clear();
            _textures.Clear();
            _buffers.Clear();
        }


        private unsafe void WriteData<T>(string property, T newData) where T : unmanaged
        {
            int tSize = sizeof(T);

            if (!_values.TryGetValue(property, out byte[] value))
            {
                value = new byte[tSize];
                _values[property] = value;
            }

            // If the previous value was larger than a vector4, and the new value isn't
            // the array size will be downgraded to save on memory footprint.
            if (value.Length < tSize || (tSize < 16 && value.Length != tSize))
            {
                value = new byte[tSize];
                _values[property] = value;
            }

            MemoryMarshal.Write(value, newData);
        }


        public unsafe T InterpretAs<T>(string property) where T : unmanaged
        {
            if (!_values.TryGetValue(property, out byte[] value))
                return default;

            if (value.Length < sizeof(T))
                return default;

            return MemoryMarshal.Read<T>(value);
        }
    }
}
