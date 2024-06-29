using System;
using System.Collections.Generic;
using System.Linq;
using Veldrid;

namespace Prowl.Runtime
{
    public class PropertyState
    {
        private Dictionary<string, System.Numerics.Vector4> values; 
        private Dictionary<string, System.Numerics.Matrix4x4> matrices;
        private Dictionary<string, Texture> textures;

        public PropertyState() 
        { 
            values = new();
            matrices = new();
            textures = new();
        }

        public PropertyState(PropertyState clone)
        {
            values = new(clone.values);
            matrices = new(clone.matrices);
            textures = new(clone.textures);
        }

        public void ApplyOverride(PropertyState overrideState)
        {
            foreach (var pair in overrideState.values)
                values[pair.Key] = pair.Value;

            foreach (var pair in overrideState.matrices)
                matrices[pair.Key] = pair.Value;

            foreach (var pair in overrideState.textures)
                textures[pair.Key] = pair.Value;
        }


        public void SetColor(string name, Color value) => values[name] = value;
        public Color GetColor(string name) => values.GetValueOrDefault(name, Color.white);

        public void SetVector(string name, Vector2 value) => values[name] = new Vector4(value.x, value.y, 0, 0);
        public Vector2 GetVector2(string name)
        {
            Vector4 value = values.GetValueOrDefault(name, Vector4.zero);
            return new Vector2(value.x, value.y);
        }

        public void SetVector(string name, Vector3 value) => values[name] = new Vector4(value.x, value.y, value.z, 0);
        public Vector3 GetVector3(string name) 
        {
            Vector4 value = values.GetValueOrDefault(name, Vector4.zero);
            return new Vector3(value.x, value.y, value.z);
        }

        public void SetVector(string name, Vector4 value) => values[name] = value;
        public Vector4 GetVector4(string name) => values.GetValueOrDefault(name, Vector4.zero);

        public void SetFloat(string name, float value) => values[name] = new Vector4(value);
        public float GetFloat(string name)
        {
            Vector4 value = values.GetValueOrDefault(name, Vector4.zero);
            return (float)value.x;
        }

        public void SetInt(string name, int value) => values[name] = new Vector4(value, 0, 0, 0);
        public int GetInt(string name) 
        {
            Vector4 value = values.GetValueOrDefault(name, Vector4.zero);
            return (int)value.x;
        }

        public void SetMatrix(string name, Matrix4x4 value) => matrices[name] = value.ToFloat();
        public Matrix4x4 GetMatrix(string name) => Matrix4x4.FromFloat(matrices.GetValueOrDefault(name, System.Numerics.Matrix4x4.Identity));

        public void SetTexture(string name, Texture value) => textures[name] = value;
        public Texture GetTexture(string name) => textures.GetValueOrDefault(name, Texture2D.EmptyWhite);

        public void Clear()
        {
            values.Clear();
            textures.Clear();
            matrices.Clear();
        }
    }
}
