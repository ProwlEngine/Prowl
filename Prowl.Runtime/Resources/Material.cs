// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Rendering.Shaders;
using Prowl.Vector;

namespace Prowl.Runtime.Resources;

public sealed class Material : EngineObject, ISerializationCallbackReceiver
{
    private static Shader s_defaultShader;

    /// <summary>
    /// Returns a new instance of a default Standalone Material
    /// </summary>
    public static Material DefaultMaterial
    {
        get
        {
            if (s_defaultShader == null)
                s_defaultShader = Shader.LoadDefault(DefaultShader.Standard);
            var mat = new Material();
            mat.SetColor("_MainColor", Color.White);
            return mat;
        } 
    }

    [SerializeField]
    private Shader _shader;

    public Shader Shader
    {
        get => _shader;
        set => SetShader(value);
    }

    [SerializeField]
    public PropertyState _properties;

    [SerializeIgnore]
    internal Dictionary<string, bool> _localKeywords;

    // Material batching optimization: materials with identical state (uniforms) are batched together
    // to minimize GPU state changes. The hash represents the current uniform values.
    [SerializeIgnore]
    private ulong _stateHash;

    // Dirty flag tracks when material properties have changed, triggering hash recalculation
    [SerializeIgnore]
    private bool _isDirty = true;


    internal Material() : base("New Material")
    {
        _properties = new();
        _localKeywords = [];
    }

    public Material(Shader shader, PropertyState? properties = null, Dictionary<string, bool>? keywords = null) : base("New Material")
    {
        ArgumentNullException.ThrowIfNull(shader);

        _properties = new();
        _localKeywords = keywords ?? [];

        Shader = shader;
        if (properties != null)
            _properties.ApplyOverride(properties);
    }

    public void SetKeyword(string keyword, bool value) => _localKeywords[keyword] = value;

    public void SetColor(string name, Color value) { _properties.SetColor(name, value); MarkDirty(); }
    public void SetVector(string name, Float2 value) { _properties.SetVector(name, value); MarkDirty(); }
    public void SetVector(string name, Float3 value) { _properties.SetVector(name, value); MarkDirty(); }
    public void SetVector(string name, Float4 value) { _properties.SetVector(name, value); MarkDirty(); }
    public void SetFloat(string name, float value) { _properties.SetFloat(name, value); MarkDirty(); }
    public void SetInt(string name, int value) { _properties.SetInt(name, value); MarkDirty(); }
    public void SetMatrix(string name, Float4x4 value) { _properties.SetMatrix(name, value); MarkDirty(); }
    public void SetTexture(string name, Texture2D value) { _properties.SetTexture(name, value); MarkDirty(); }
    public void SetTexture3D(string name, Texture3D value) { _properties.SetTexture3D(name, value); MarkDirty(); }

    #region Global Properties

    public static void SetGlobalColor(string name, Color value) => PropertyState.SetGlobalColor(name, value);
    public static void SetGlobalVector(string name, Float2 value) => PropertyState.SetGlobalVector(name, value);
    public static void SetGlobalVector(string name, Float3 value) => PropertyState.SetGlobalVector(name, value);
    public static void SetGlobalVector(string name, Float4 value) => PropertyState.SetGlobalVector(name, value);
    public static void SetGlobalFloat(string name, float value) => PropertyState.SetGlobalFloat(name, value);
    public static void SetGlobalInt(string name, int value) => PropertyState.SetGlobalInt(name, value);
    public static void SetGlobalMatrix(string name, Float4x4 value) => PropertyState.SetGlobalMatrix(name, value);
    public static void SetGlobalTexture(string name, Texture2D value) => PropertyState.SetGlobalTexture(name, value);
    public static void SetGlobalTexture3D(string name, Texture3D value) => PropertyState.SetGlobalTexture3D(name, value);

    #endregion

    private void UpdatePropertyState(ShaderProperty property)
    {
        switch (property.PropertyType)
        {
            case ShaderPropertyType.Texture2D:
                _properties.SetTexture(property.Name, property.Texture2DValue);
                break;

            case ShaderPropertyType.Texture3D:
                _properties.SetTexture3D(property.Name, property.Texture3DValue);
                break;

            case ShaderPropertyType.Float:
                _properties.SetFloat(property.Name, (float)property);
                break;

            case ShaderPropertyType.Vector2:
                _properties.SetVector(property.Name, (Float2)property);
                break;

            case ShaderPropertyType.Vector3:
                _properties.SetVector(property.Name, (Float3)property);
                break;

            case ShaderPropertyType.Vector4:
                _properties.SetVector(property.Name, (Float4)property);
                break;

            case ShaderPropertyType.Color:
                _properties.SetColor(property.Name, (Color)property);
                break;

            case ShaderPropertyType.Matrix:
                _properties.SetMatrix(property.Name, (Float4x4)property);
                break;
        }
    }


    internal void SetShader(Shader shader)
    {
        ArgumentNullException.ThrowIfNull(shader);

        if (shader == _shader)
            return;

        _shader = shader;
        foreach (ShaderProperty prop in shader.Properties)
            UpdatePropertyState(prop);

        _isDirty = true;
    }

    /// <summary>
    /// Gets a hash representing the current material state (uniform values only, not keywords or shader).
    /// The hash is used by the renderer to batch objects with identical material properties together,
    /// minimizing GPU uniform binding overhead. The hash is cached and only recalculated when dirty.
    /// </summary>
    /// <returns>A 64-bit hash of all material uniform values</returns>
    public ulong GetStateHash()
    {
        if (_isDirty)
        {
            _stateHash = _properties.ComputeHash();
            _isDirty = false;
        }
        return _stateHash;
    }

    /// <summary>
    /// Marks the material as dirty, forcing a hash recalculation on next GetStateHash() call.
    /// Called automatically when any material property is modified.
    /// </summary>
    private void MarkDirty()
    {
        _isDirty = true;
    }

    public void OnBeforeSerialize() { }

    public void OnAfterDeserialize() { }
}
