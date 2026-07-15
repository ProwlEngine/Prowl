// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Echo;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Rendering.Shaders;
using Prowl.Vector;

namespace Prowl.Runtime.Resources;

[CreateAssetMenu("Material", Extension = ".mat", Order = 1)]
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

    /// <summary>
    /// Load a default embedded material returns a fresh clone every call so callers
    /// can freely mutate (SetTexture, SetFloat, ...) without stepping on each other.
    /// The underlying template is cached in <see cref="BuiltInAssets"/> so the .mat file
    /// is only deserialized once, but the returned instance is always yours to own.
    /// </summary>
    public static Material LoadDefault(DefaultMaterial material)
    {
        // Pull the shared template from the cache, then clone. Clone is a cheap deep-copy
        // of the property dictionaries + a shared shader reference materials are
        // configuration objects, not heavy resources.
        if (BuiltInAssets.Get(BuiltInAssets.GuidFor(material)) is Material template)
            return new Material(template);
        // Fallback if BuiltInAssets isn't initialized ParseDefault already returns a
        // fresh instance, no clone needed.
        return ParseDefault(material);
    }

    /// <summary>
    /// Raw deserialize of a default embedded material invoked by <see cref="BuiltInAssets"/>
    /// on first cache miss. Public callers should use <see cref="LoadDefault"/>.
    /// </summary>
    internal static Material ParseDefault(DefaultMaterial material)
    {
        string fileName = material switch
        {
            Prowl.Runtime.Resources.DefaultMaterial.Standard => "Standard.mat",
            Prowl.Runtime.Resources.DefaultMaterial.Particle => "Particle.mat",
            Prowl.Runtime.Resources.DefaultMaterial.Terrain => "Standard Terrain.mat",
            Prowl.Runtime.Resources.DefaultMaterial.Grass => "Grass.mat",
            _ => throw new ArgumentException($"Unknown default material: {material}")
        };

        string resourcePath = $"Assets/Defaults/{fileName}";
        using Stream stream = EmbeddedResources.GetStream(resourcePath);
        using var reader = new StreamReader(stream);
        string text = reader.ReadToEnd();
        var echo = EchoObject.ReadFromString(text);
        var mat = Serializer.Deserialize<Material>(echo);
        // AssetID/AssetPath/Name are set by BuiltInAssets.Get after this returns.
        return mat;
    }

    [SerializeField]
    private AssetRef<Shader> _shader;

    public Shader? Shader
    {
        get { EnsureNotDisposed(); return _shader.Res; }
        set { EnsureNotDisposed(); SetShader(value); }
    }

    /// <summary>The shader as an <see cref="AssetRef{Shader}"/>, for asset-reference editing.
    /// A material must always have a shader, so assigning an empty ref is ignored.</summary>
    public AssetRef<Shader> ShaderRef
    {
        get { EnsureNotDisposed(); return _shader; }
        set { EnsureNotDisposed(); if (value.Res != null) SetShader(value.Res); }
    }

    [SerializeField]
    public PropertyState _properties;

    /// <summary>Names of properties the user has explicitly set (vs auto-filled
    /// shader defaults). When the shader's defaults change, only NON-overridden
    /// entries get refreshed user customizations are preserved. Without this,
    /// stale defaults stick around forever.</summary>
    [SerializeField]
    public HashSet<string> _overrides = new();

    [SerializeIgnore]
    internal Dictionary<string, bool> _localKeywords;

    // Material batching optimization: materials with identical state (uniforms) are batched together
    // to minimize GPU state changes. The hash represents the current uniform values.
    [SerializeIgnore]
    private ulong _stateHash;

    // Dirty flag tracks when material properties have changed, triggering hash recalculation
    [SerializeIgnore]
    private bool _isDirty = true;


    public Material() : base("New Material")
    {
        _properties = new();
        _localKeywords = [];
        // Default to Standard shader so new materials are immediately usable
        var standard = Shader.LoadDefault(DefaultShader.Standard);
        if (standard != null)
            SetShader(standard);
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

    /// <summary>
    /// Copy constructor deep-clone every property value + a fresh keyword dict. The
    /// <see cref="Shader"/> reference is shared (shaders are immutable after parse).
    /// Use this when you need a mutable material seeded from <see cref="LoadDefault"/>
    /// or any other shared material so your mutations don't leak to other callers.
    /// </summary>
    public Material(Material source) : base(source?.Name ?? "New Material")
    {
        ArgumentNullException.ThrowIfNull(source);

        _shader = source._shader;
        _properties = new PropertyState(source._properties);
        _localKeywords = new Dictionary<string, bool>(source._localKeywords ?? []);
    }

    /// <summary>Returns a deep copy of this material (see <see cref="Material(Material)"/>).</summary>
    public Material Clone() { EnsureNotDisposed(); return new Material(this); }

    public void SetKeyword(string keyword, bool value) { EnsureNotDisposed(); _localKeywords[keyword] = value; }

    // Every public Set marks the property as user-overridden so subsequent shader
    // default-refreshes won't stomp the user's value.
    public void SetColor(string name, Color value)        { EnsureNotDisposed(); _overrides.Add(name); _properties.SetColor(name, value); MarkDirty(); }
    public void SetVector(string name, Float2 value)      { EnsureNotDisposed(); _overrides.Add(name); _properties.SetVector(name, value); MarkDirty(); }
    public void SetVector(string name, Float3 value)      { EnsureNotDisposed(); _overrides.Add(name); _properties.SetVector(name, value); MarkDirty(); }
    public void SetVector(string name, Float4 value)      { EnsureNotDisposed(); _overrides.Add(name); _properties.SetVector(name, value); MarkDirty(); }
    public void SetFloat(string name, float value)        { EnsureNotDisposed(); _overrides.Add(name); _properties.SetFloat(name, value); MarkDirty(); }
    public void SetInt(string name, int value)            { EnsureNotDisposed(); _overrides.Add(name); _properties.SetInt(name, value); MarkDirty(); }
    public void SetMatrix(string name, Float4x4 value)    { EnsureNotDisposed(); _overrides.Add(name); _properties.SetMatrix(name, value); MarkDirty(); }
    public void SetTexture(string name, Texture2D value)  { EnsureNotDisposed(); _overrides.Add(name); _properties.SetTexture(name, value); MarkDirty(); }
    public void SetTexture(string name, AssetRef<Texture2D> value) { EnsureNotDisposed(); _overrides.Add(name); _properties.SetTexture(name, value); MarkDirty(); }
    public void SetTexture3D(string name, Texture3D value){ EnsureNotDisposed(); _overrides.Add(name); _properties.SetTexture3D(name, value); MarkDirty(); }
    public void SetTexture3D(string name, AssetRef<Texture3D> value){ EnsureNotDisposed(); _overrides.Add(name); _properties.SetTexture3D(name, value); MarkDirty(); }
    public void SetTextureCube(string name, Cubemap value){ EnsureNotDisposed(); _overrides.Add(name); _properties.SetTextureCube(name, value); MarkDirty(); }
    public void SetTextureCube(string name, AssetRef<Cubemap> value){ EnsureNotDisposed(); _overrides.Add(name); _properties.SetTextureCube(name, value); MarkDirty(); }

    /// <summary>Forget the user override for <paramref name="name"/> next sync
    /// will refill it from the shader's current default. Useful for an inspector
    /// "revert to default" button.</summary>
    /// <remarks>
    /// Removes from BOTH the override set AND the backing <c>_properties</c> dict.
    /// If we only cleared <c>_overrides</c>, <c>ApplyMaterialUniforms</c> would still
    /// see the stale value in <c>_properties</c> and upload it anyway the defaults
    /// fill-in path only runs for keys not already in the property dict. This was a
    /// silent "revert does nothing" bug before.
    /// </remarks>
    public void RevertProperty(string name)
    {
        EnsureNotDisposed();
        _overrides.Remove(name);
        _properties?.RemoveProperty(name);
        MarkDirty();
    }

    /// <summary>True if the user has explicitly set this property (vs holding the
    /// shader's default value). Inspector uses this to highlight overridden fields.</summary>
    public bool IsOverridden(string name) { EnsureNotDisposed(); return _overrides.Contains(name); }

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

            case ShaderPropertyType.Int:
                _properties.SetInt(property.Name, (int)property);
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


    internal void SetShader(Shader? shader)
    {
        ArgumentNullException.ThrowIfNull(shader);

        if (shader == _shader.Res)
            return;

        _shader = new AssetRef<Shader>(shader);
        // Intentionally do NOT pre-fill _properties with shader defaults defaults
        // are read live from the shader at access time (see DrawShaderProperty
        // fallback + ApplyMaterialUniformsWithDefaults). Pre-filling would mark
        // every default as an "override" once the material is serialized.
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
        EnsureNotDisposed();
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

    public void OnAfterDeserialize()
    {
        // Migration: materials saved before the override-tracking model don't have
        // _overrides populated, but their _properties dictionary holds values the
        // user actually set. Treat every existing entry as an override so saved
        // values are preserved when the override-aware code paths take over.
        if (_overrides == null) _overrides = new HashSet<string>();
        if (_overrides.Count == 0 && _properties != null)
        {
            foreach (var name in _properties.EnumerateNames())
                _overrides.Add(name);
        }
        // No SyncShaderDefaults defaults are read live from the shader at access
        // time (see PropertyState.ApplyMaterialUniformsWithDefaults + the inspector's
        // DrawShaderProperty fallback). Materials only ever store overrides.
    }

    /// <summary>
    /// Refresh non-overridden properties from the shader's CURRENT defaults. Adds
    /// missing entries AND overwrites existing entries that aren't user-overridden,
    /// so changes to a property's default in the shader propagate immediately
    /// without dropping user customizations. Cheap enough to call every frame from
    /// the material inspector.
    /// </summary>
    public void SyncShaderDefaults()
    {
        EnsureNotDisposed();
        var shader = _shader.Res;
        if (shader == null) return;

        foreach (ShaderProperty prop in shader.Properties)
        {
            // User-set values are sacred leave them alone.
            if (_overrides.Contains(prop.Name)) continue;
            UpdatePropertyState(prop);
        }
    }

    private bool HasProperty(string name, ShaderPropertyType type)
    {
        return type switch
        {
            ShaderPropertyType.Float => _properties.HasFloat(name),
            ShaderPropertyType.Int => _properties.HasInt(name),
            ShaderPropertyType.Vector2 => _properties.HasVector2(name),
            ShaderPropertyType.Vector3 => _properties.HasVector3(name),
            ShaderPropertyType.Vector4 => _properties.HasVector4(name),
            ShaderPropertyType.Color => _properties.HasColor(name),
            ShaderPropertyType.Matrix => _properties.HasMatrix(name),
            ShaderPropertyType.Texture2D => _properties.HasTexture(name),
            ShaderPropertyType.Texture3D => _properties.HasTexture3D(name),
            _ => false,
        };
    }
}
