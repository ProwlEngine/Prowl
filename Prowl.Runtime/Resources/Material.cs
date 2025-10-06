using System;
using System.Collections.Generic;

using Prowl.Echo;
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
                s_defaultMaterial.SetColor("_MainColor", Color.white);
            }

            return s_defaultMaterial;
        }

        public static Material CreateDefaultMaterial()
        {
            return new Material(Game.AssetProvider.LoadAsset<Shader>("Assets/Defaults/Standard.shader"));
        }

        [SerializeField]
        private AssetRef<Shader> _shader;

        public AssetRef<Shader> Shader {
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

        public Material(AssetRef<Shader> shader, PropertyState? properties = null, Dictionary<string, bool>? keywords = null) : base("New Material")
        {
            if (shader.Res == null)
                throw new ArgumentNullException(nameof(shader));

            _properties = new();
            _localKeywords = keywords ?? new();

            Shader = shader;
            if(properties != null)
                _properties.ApplyOverride(properties);
        }

        public void SetKeyword(string keyword, bool value) => _localKeywords[keyword] = value;

        public void SetColor(string name, Color value) => _properties.SetColor(name, value);
        public void SetVector(string name, Vector2 value) => _properties.SetVector(name, value);
        public void SetVector(string name, Vector3 value) => _properties.SetVector(name, value);
        public void SetVector(string name, Vector4 value) => _properties.SetVector(name, value);
        public void SetFloat(string name, float value) => _properties.SetFloat(name, value);
        public void SetInt(string name, int value) => _properties.SetInt(name, value);
        public void SetMatrix(string name, Matrix4x4 value) => _properties.SetMatrix(name, value);
        public void SetTexture(string name, AssetRef<Texture2D> value) => _properties.SetTexture(name, value);

        #region Global Properties

        public static void SetGlobalColor(string name, Color value) => PropertyState.SetGlobalColor(name, value);
        public static void SetGlobalVector(string name, Vector2 value) => PropertyState.SetGlobalVector(name, value);
        public static void SetGlobalVector(string name, Vector3 value) => PropertyState.SetGlobalVector(name, value);
        public static void SetGlobalVector(string name, Vector4 value) => PropertyState.SetGlobalVector(name, value);
        public static void SetGlobalFloat(string name, float value) => PropertyState.SetGlobalFloat(name, value);
        public static void SetGlobalInt(string name, int value) => PropertyState.SetGlobalInt(name, value);
        public static void SetGlobalMatrix(string name, Matrix4x4 value) => PropertyState.SetGlobalMatrix(name, value);
        public static void SetGlobalTexture(string name, AssetRef<Texture2D> value) => PropertyState.SetGlobalTexture(name, value);

        #endregion

        private void UpdatePropertyState(ShaderProperty property)
        {
            switch (property.PropertyType)
            {
                case ShaderPropertyType.Texture2D:
                    _properties.SetTexture(property.Name, property.Texture2DValue.Res);
                    break;

                case ShaderPropertyType.Float:
                    _properties.SetFloat(property.Name, (float)property);
                    break;

                case ShaderPropertyType.Vector2:
                    _properties.SetVector(property.Name, (Vector2)property);
                    break;

                case ShaderPropertyType.Vector3:
                    _properties.SetVector(property.Name, (Vector3)property);
                    break;

                case ShaderPropertyType.Vector4:
                    _properties.SetVector(property.Name, (Vector4)property);
                    break;

                case ShaderPropertyType.Color:
                    _properties.SetColor(property.Name, (Color)property);
                    break;

                case ShaderPropertyType.Matrix:
                    _properties.SetMatrix(property.Name, (Matrix4x4)property);
                    break;
            }
        }


        internal void SetShader(AssetRef<Shader> shader)
        {
            if (shader == _shader)
                return;

            _shader = shader;
            foreach(var prop in shader.Res!.Properties)
                UpdatePropertyState(prop);
        }


        public void OnBeforeSerialize() { }

        public void OnAfterDeserialize() {}
    }

}
