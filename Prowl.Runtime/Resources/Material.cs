using System;
using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Vector;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Rendering.Shaders;

namespace Prowl.Runtime.Resources
{
    public sealed class Material : EngineObject, ISerializationCallbackReceiver
    {
        private static Material s_defaultMaterial;

        public static Material GetDefaultMaterial()
        {
            if (s_defaultMaterial == null)
            {
                s_defaultMaterial = CreateDefaultMaterial();
                s_defaultMaterial.SetColor("_MainColor", Colors.White);
            }

            return s_defaultMaterial;
        }

        public static Material CreateDefaultMaterial()
        {
            return new Material(Shader.LoadDefault(DefaultShader.Standard));
        }

        [SerializeField]
        private Shader _shader;

        public Shader Shader {
            get => _shader;
            set => SetShader(value);
        }

        [SerializeField]
        public PropertyState _properties;

        [SerializeIgnore]
        internal Dictionary<string, bool> _localKeywords;


        internal Material() : base("New Material")
        {
            _properties = new();
            _localKeywords = new();
        }

        public Material(Shader shader, PropertyState? properties = null, Dictionary<string, bool>? keywords = null) : base("New Material")
        {
            if (shader == null)
                throw new ArgumentNullException(nameof(shader));

            _properties = new();
            _localKeywords = keywords ?? new();

            Shader = shader;
            if(properties != null)
                _properties.ApplyOverride(properties);
        }

        public void SetKeyword(string keyword, bool value) => _localKeywords[keyword] = value;

        public void SetColor(string name, Float4 value) => _properties.SetColor(name, value);
        public void SetVector(string name, Double2 value) => _properties.SetVector(name, value);
        public void SetVector(string name, Double3 value) => _properties.SetVector(name, value);
        public void SetVector(string name, Double4 value) => _properties.SetVector(name, value);
        public void SetFloat(string name, float value) => _properties.SetFloat(name, value);
        public void SetInt(string name, int value) => _properties.SetInt(name, value);
        public void SetMatrix(string name, Double4x4 value) => _properties.SetMatrix(name, value);
        public void SetTexture(string name, Texture2D value) => _properties.SetTexture(name, value);

        #region Global Properties

        public static void SetGlobalColor(string name, Float4 value) => PropertyState.SetGlobalColor(name, value);
        public static void SetGlobalVector(string name, Double2 value) => PropertyState.SetGlobalVector(name, value);
        public static void SetGlobalVector(string name, Double3 value) => PropertyState.SetGlobalVector(name, value);
        public static void SetGlobalVector(string name, Double4 value) => PropertyState.SetGlobalVector(name, value);
        public static void SetGlobalFloat(string name, float value) => PropertyState.SetGlobalFloat(name, value);
        public static void SetGlobalInt(string name, int value) => PropertyState.SetGlobalInt(name, value);
        public static void SetGlobalMatrix(string name, Double4x4 value) => PropertyState.SetGlobalMatrix(name, value);
        public static void SetGlobalTexture(string name, Texture2D value) => PropertyState.SetGlobalTexture(name, value);

        #endregion

        private void UpdatePropertyState(ShaderProperty property)
        {
            switch (property.PropertyType)
            {
                case ShaderPropertyType.Texture2D:
                    _properties.SetTexture(property.Name, property.Texture2DValue);
                    break;

                case ShaderPropertyType.Float:
                    _properties.SetFloat(property.Name, (float)property);
                    break;

                case ShaderPropertyType.Vector2:
                    _properties.SetVector(property.Name, (Double2)property);
                    break;

                case ShaderPropertyType.Vector3:
                    _properties.SetVector(property.Name, (Double3)property);
                    break;

                case ShaderPropertyType.Vector4:
                    _properties.SetVector(property.Name, (Double4)property);
                    break;

                case ShaderPropertyType.Color:
                    _properties.SetColor(property.Name, (Float4)property);
                    break;

                case ShaderPropertyType.Matrix:
                    _properties.SetMatrix(property.Name, (Double4x4)property);
                    break;
            }
        }


        internal void SetShader(Shader shader)
        {
            if (shader == null)
                throw new ArgumentNullException(nameof(shader));

            if (shader == _shader)
                return;

            _shader = shader;
            foreach(var prop in shader.Properties)
                UpdatePropertyState(prop);
        }


        public void OnBeforeSerialize() { }

        public void OnAfterDeserialize() {}
    }

}
