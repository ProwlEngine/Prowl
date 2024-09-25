// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Matrix4x4F = System.Numerics.Matrix4x4;
using Vector2F = System.Numerics.Vector2;
using Vector3F = System.Numerics.Vector3;
using Vector4F = System.Numerics.Vector4;

namespace Prowl.Runtime;

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
        return new Material(Application.AssetProvider.LoadAsset<Shader>("Defaults/DefaultUnlit.shader"));
    }


    [SerializeField]
    internal AssetRef<Shader> _shader;

    public AssetRef<Shader> Shader
    {
        get => _shader;
        set => SetShader(value);
    }

    [SerializeField]
    internal List<SerializedShaderProperty> _serializedProperties;

    [NonSerialized]
    private Dictionary<string, int> _propertyLookup;

    [NonSerialized]
    internal PropertyState _properties;

    [NonSerialized]
    internal KeywordState _localKeywords;


    internal Material() : base("New Material")
    {
        _properties = new();
        _localKeywords = KeywordState.Default;
    }

    public Material(AssetRef<Shader> shader, PropertyState? properties = null, KeywordState? keywords = null) : base("New Material")
    {
        if (shader.Res == null)
            throw new ArgumentNullException(nameof(shader));

        Shader = shader;
        _properties = properties ?? new();
        _localKeywords = keywords ?? KeywordState.Default;
    }

    public void SetKeyword(string keyword, string value) => _localKeywords.SetKey(keyword, value);

    public void SetColor(string name, Color value) => _properties.SetColor(name, value);
    public void SetVector(string name, Vector2F value) => _properties.SetVector(name, value);
    public void SetVector(string name, Vector3F value) => _properties.SetVector(name, value);
    public void SetVector(string name, Vector4F value) => _properties.SetVector(name, value);
    public void SetFloat(string name, float value) => _properties.SetFloat(name, value);
    public void SetInt(string name, int value) => _properties.SetInt(name, value);
    public void SetMatrix(string name, Matrix4x4F value) => _properties.SetMatrix(name, value);
    public void SetTexture(string name, AssetRef<Texture> value) => _properties.SetTexture(name, value);


    public void SetFloatArray(string name, float[] values) => _properties.SetFloatArray(name, values);
    public void SetIntArray(string name, int[] values) => _properties.SetIntArray(name, values);
    public void SetVectorArray(string name, Vector2F[] values) => _properties.SetVectorArray(name, values);
    public void SetVectorArray(string name, Vector3F[] values) => _properties.SetVectorArray(name, values);
    public void SetVectorArray(string name, Vector4F[] values) => _properties.SetVectorArray(name, values);
    public void SetColorArray(string name, Color[] values) => _properties.SetColorArray(name, values);
    public void SetMatrixArray(string name, Matrix4x4F[] values) => _properties.SetMatrixArray(name, values);


    public void SetProperty(string name, double value)
        => SetPropertyInternal(name, value);

    public void SetProperty(string name, Vector2 value)
        => SetPropertyInternal(name, value);

    public void SetProperty(string name, Vector3 value)
        => SetPropertyInternal(name, value);

    public void SetProperty(string name, Vector4 value)
        => SetPropertyInternal(name, value);

    public void SetProperty(string name, Color value)
        => SetPropertyInternal(name, value);

    public void SetProperty(string name, Matrix4x4 value)
        => SetPropertyInternal(name, value);

    public void SetProperty(string name, AssetRef<Texture2D> value)
        => SetPropertyInternal(name, value);

    public void SetProperty(string name, AssetRef<Texture3D> value)
        => SetPropertyInternal(name, value);

    private void SetPropertyInternal(string name, object? value)
    {
        if (_propertyLookup.TryGetValue(name, out int val))
        {
            SerializedShaderProperty prop = _serializedProperties[val];

            prop.defaultProperty = value;

            UpdatePropertyState(prop);

            _serializedProperties[val] = prop;
        }
    }


    public void SyncPropertyBlock()
    {
        foreach (SerializedShaderProperty prop in _serializedProperties)
            UpdatePropertyState(prop);
    }


    private void UpdatePropertyState(SerializedShaderProperty property)
    {
        switch (property.propertyType)
        {
            case ShaderPropertyType.Texture2D:
                _properties.SetTexture(property.name, ((AssetRef<Texture2D>)property.defaultProperty).Res);
                break;

            case ShaderPropertyType.Texture3D:
                _properties.SetTexture(property.name, ((AssetRef<Texture3D>)property.defaultProperty).Res);
                break;

            case ShaderPropertyType.Float:
                _properties.SetFloat(property.name, (float)property.defaultProperty);
                break;

            case ShaderPropertyType.Vector2:
                _properties.SetVector(property.name, (Vector2)property.defaultProperty);
                break;

            case ShaderPropertyType.Vector3:
                _properties.SetVector(property.name, (Vector3)property.defaultProperty);
                break;

            case ShaderPropertyType.Vector4:
                _properties.SetVector(property.name, (Vector4)property.defaultProperty);
                break;

            case ShaderPropertyType.Color:
                _properties.SetColor(property.name, (Color)property.defaultProperty);
                break;

            case ShaderPropertyType.Matrix:
                _properties.SetMatrix(property.name, ((Matrix4x4)property.defaultProperty).ToFloat());
                break;
        }
    }


    internal void SetShader(AssetRef<Shader> shader)
    {
        _shader = shader;

        _serializedProperties ??= [];
        _propertyLookup ??= [];

        _serializedProperties.Clear();
        _propertyLookup.Clear();

        foreach (SerializedShaderProperty prop in shader.Res.Properties)
        {
            _serializedProperties.Add(prop);
            _propertyLookup.Add(prop.name, _serializedProperties.Count - 1);
        }
    }


    public void OnBeforeSerialize() { }

    public void OnAfterDeserialize()
    {
        _propertyLookup ??= [];

        foreach (SerializedShaderProperty prop in Shader.Res.Properties)
            _propertyLookup.Add(prop.name, _serializedProperties.Count - 1);

        SyncPropertyBlock();
    }
}
