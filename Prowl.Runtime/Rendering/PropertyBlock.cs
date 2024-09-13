// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Matrix4x4F = System.Numerics.Matrix4x4;
using Vector2F = System.Numerics.Vector2;
using Vector3F = System.Numerics.Vector3;
using Vector4F = System.Numerics.Vector4;

namespace Prowl.Runtime
{
    public class PropertyBlock
    {
        [SerializeField, HideInInspector]
        private PropertyState _internalState;

        public PropertyBlock()
        {
            _internalState = new();
        }

        public PropertyBlock(PropertyBlock clone)
        {
            _internalState.ApplyOverride(clone._internalState);
        }


        public static implicit operator PropertyState(PropertyBlock block)
            => block._internalState;


        public void SetColor(string name, Color value) => _internalState.SetColor(name, value);
        public void SetVector(string name, Vector2F value) => _internalState.SetVector(name, value);
        public void SetVector(string name, Vector3F value) => _internalState.SetVector(name, value);
        public void SetVector(string name, Vector4F value) => _internalState.SetVector(name, value);
        public void SetFloat(string name, float value) => _internalState.SetFloat(name, value);
        public void SetInt(string name, int value) => _internalState.SetInt(name, value);
        public void SetMatrix(string name, Matrix4x4F value) => _internalState.SetMatrix(name, value);
    }
}
