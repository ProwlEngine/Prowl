using System;
using System.Collections.Generic;
using System.Linq;
using Veldrid;

namespace Prowl.Runtime
{
    public class MaterialPropertyBlock
    {
        [SerializeField] private Dictionary<string, System.Numerics.Vector4> values = new();
        [SerializeField] private Dictionary<string, System.Numerics.Matrix4x4> matrices = new();
        [SerializeField] private Dictionary<string, Texture> textures = new();

        public MaterialPropertyBlock() { }

        public MaterialPropertyBlock(MaterialPropertyBlock clone)
        {
            values = new(clone.values);
            matrices = new(clone.matrices);
            textures = new(clone.textures);
        }


        public void SetColor(string name, Color value) => values[name] = value;
        public Color GetColor(string name) => values.TryGetValue(name, out System.Numerics.Vector4 value) ? value : Color.white;

        public void SetVector(string name, Vector2 value) => values[name] = new Vector4(value.x, value.y, 0, 0);
        public Vector2 GetVector2(string name) => values.TryGetValue(name, out System.Numerics.Vector4 value) ? new Vector2(value.X, value.Y) : Vector2.zero;
        
        public void SetVector(string name, Vector3 value) => values[name] = new Vector4(value.x, value.y, value.z, 0);
        public Vector3 GetVector3(string name) => values.TryGetValue(name, out System.Numerics.Vector4 value) ? new Vector3(value.X, value.Y, value.Z) : Vector3.zero;

        public void SetVector(string name, Vector4 value) => values[name] = value;
        public Vector4 GetVector4(string name) => values.ContainsKey(name) ? values[name] : Vector4.zero;

        public void SetFloat(string name, float value) => values[name] = new Vector4(value);
        public float GetFloat(string name) => values.ContainsKey(name) ? values[name].X : 0;

        public void SetInt(string name, int value) => values[name] = new Vector4(value, 0, 0, 0);
        public int GetInt(string name) => values.ContainsKey(name) ? (int)values[name].X : 0;

        public void SetMatrix(string name, Matrix4x4 value) => matrices[name] = value.ToFloat();
        public Matrix4x4 GetMatrix(string name) => matrices.ContainsKey(name) ? Matrix4x4.FromFloat(matrices[name]) : Matrix4x4.Identity;

        public void SetTexture(string name, Texture value) => textures[name] = value;
        
        public Texture? GetTexture(string name) => textures.ContainsKey(name) ? textures[name] : null;

        public void Clear()
        {
            values.Clear();
            textures.Clear();
            matrices.Clear();
        }
    }
}
