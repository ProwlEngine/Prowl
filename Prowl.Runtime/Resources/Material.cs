// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Prowl.Echo;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Rendering.Shaders;
using Prowl.Vector;

namespace Prowl.Runtime.Resources;

[CreateAssetMenu("Material", Extension = ".mat", Order = 1)]
public sealed class Material : Asset, ISerializationCallbackReceiver
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
        => new(BuiltInAssets.Load<Material>(BuiltInAssets.GuidFor(material)));

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
        return Serializer.Deserialize<Material>(echo);
    }

    [SerializeField]
    private Shader? _shader;

    /// <summary>The material's shader. One that was never set, or is missing or failed to load, is the built-in Standard shader.</summary>
    public Shader? Shader
    {
        get
        {
            EnsureLoaded();
            return _shader is { State: not (AssetState.Missing or AssetState.Failed) } shader ? shader : Shader.LoadDefault(DefaultShader.Standard);
        }
        set { EnsureLoaded(); SetShader(value); }
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

    // Order free hash of the enabled keywords, kept up to date as they change so finding a shader variant is one lookup
    [SerializeIgnore]
    private ulong _keywordHash;

    [SerializeIgnore]
    private bool _keywordHashValid;

    /// <summary>Hash of the enabled keywords, the key a shader pass caches this material's variant under.</summary>
    internal ulong KeywordHash
    {
        get
        {
            if (!_keywordHashValid)
            {
                ulong sum = 0;
                foreach (KeyValuePair<string, bool> kv in _localKeywords)
                    if (kv.Value) sum += KeywordBits(kv.Key);
                _keywordHash = sum;
                _keywordHashValid = true;
            }
            return _keywordHash;
        }
    }

    private static ulong KeywordBits(string keyword) =>
        PropertyState.Mix((uint)keyword.GetHashCode() * 0x9E3779B97F4A7C15UL + (ulong)keyword.Length + 1);

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
    public Material(Material source) : base(source.IsValid() ? source.Name : "New Material")
    {
        ArgumentNullException.ThrowIfNull(source);
        source.EnsureLoaded();

        _shader = source._shader;
        _properties = new PropertyState(source._properties);
        _localKeywords = new Dictionary<string, bool>(source._localKeywords ?? []);
    }

    /// <summary>Returns a deep copy of this material (see <see cref="Material(Material)"/>).</summary>
    public Material Clone() { EnsureLoaded(); return new Material(this); }

    // Keywords the renderer sets from the mesh it is drawing. Batches are already split by mesh, so these stay out of
    // the state hash, and the renderer switching them for every draw does not make the hash rebuild.
    private static readonly HashSet<string> s_drawKeywords =
    [
        "HAS_NORMALS", "HAS_TANGENTS", "HAS_UV", "HAS_UV2", "HAS_COLORS",
        "HAS_BONEINDICES", "HAS_BONEWEIGHTS", "SKINNED", "BLENDSHAPES", "GPU_INSTANCING",
    ];

    public void SetKeyword(string keyword, bool value)
    {
        EnsureLoaded();
        if (_localKeywords.TryGetValue(keyword, out bool current) && current == value) return;
        _localKeywords[keyword] = value;
        if (_keywordHashValid)
        {
            if (value) _keywordHash += KeywordBits(keyword);
            else if (current) _keywordHash -= KeywordBits(keyword);
        }
        if (!s_drawKeywords.Contains(keyword)) MarkDirty();
    }

    // Every public Set marks the property as user-overridden so subsequent shader
    // default-refreshes won't stomp the user's value.
    public void SetColor(string name, Color value)        { EnsureLoaded(); _overrides.Add(name); _properties.SetColor(name, value); MarkDirty(); }
    public void SetVector(string name, Float2 value)      { EnsureLoaded(); _overrides.Add(name); _properties.SetVector(name, value); MarkDirty(); }
    public void SetVector(string name, Float3 value)      { EnsureLoaded(); _overrides.Add(name); _properties.SetVector(name, value); MarkDirty(); }
    public void SetVector(string name, Float4 value)      { EnsureLoaded(); _overrides.Add(name); _properties.SetVector(name, value); MarkDirty(); }
    public void SetFloat(string name, float value)        { EnsureLoaded(); _overrides.Add(name); _properties.SetFloat(name, value); MarkDirty(); }
    public void SetInt(string name, int value)            { EnsureLoaded(); _overrides.Add(name); _properties.SetInt(name, value); MarkDirty(); }
    public void SetMatrix(string name, Float4x4 value)    { EnsureLoaded(); _overrides.Add(name); _properties.SetMatrix(name, value); MarkDirty(); }
    public void SetTexture(string name, Texture2D value)  { EnsureLoaded(); _overrides.Add(name); _properties.SetTexture(name, value); MarkDirty(); }
    public void SetTexture3D(string name, Texture3D value){ EnsureLoaded(); _overrides.Add(name); _properties.SetTexture3D(name, value); MarkDirty(); }

    /// <summary>Binds a compute buffer to the storage block of that name. Buffers live only at runtime and are not saved.</summary>
    public void SetBuffer(string name, ComputeBuffer value) { EnsureLoaded(); _properties.SetBuffer(name, value); MarkDirty(); }
    public void SetTextureCube(string name, Cubemap value){ EnsureLoaded(); _overrides.Add(name); _properties.SetTextureCube(name, value); MarkDirty(); }

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
        EnsureLoaded();
        _overrides.Remove(name);
        _properties?.RemoveProperty(name);
        MarkDirty();
    }

    /// <summary>True if the user has explicitly set this property (vs holding the
    /// shader's default value). Inspector uses this to highlight overridden fields.</summary>
    public bool IsOverridden(string name) { EnsureLoaded(); return _overrides.Contains(name); }

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

        if (shader == _shader)
            return;

        _shader = shader;
        // Intentionally do NOT pre-fill _properties with shader defaults defaults
        // are read live from the shader at access time (see DrawShaderProperty
        // fallback + ApplyMaterialUniformsWithDefaults). Pre-filling would mark
        // every default as an "override" once the material is serialized.
        _isDirty = true;
    }

    /// <summary>
    /// Gets a hash of everything that decides how this material draws: its shader, enabled keywords and
    /// uniform values. The renderer batches materials with equal hashes, so two materials only share a
    /// hash when either one could draw the other's objects. Properties and keywords are cached until dirty.
    /// </summary>
    public ulong GetStateHash()
    {
        EnsureLoaded();
        if (_isDirty)
        {
            _stateHash = HashKeywords(_properties.ComputeHash());
            _isDirty = false;
        }

        // The shader is resolved live since a missing or failed shader falls back without dirtying.
        ulong hash = _stateHash ^ (ulong)Shader.InstanceID;
        return hash * 1099511628211UL;
    }

    private ulong HashKeywords(ulong hash)
    {
        ulong sum = 0;
        foreach (KeyValuePair<string, bool> kv in _localKeywords)
            if (kv.Value && !s_drawKeywords.Contains(kv.Key))
                sum += PropertyState.Mix((ulong)(uint)kv.Key.GetHashCode());
        return (hash ^ PropertyState.Mix(sum)) * 1099511628211UL;
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
        // A reimport reads into this same instance, so the cached batch hash is stale.
        MarkDirty();

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
        EnsureLoaded();
        var shader = Shader;
        if (shader == null) return;

        foreach (ShaderProperty prop in shader.Properties)
        {
            // User-set values are sacred leave them alone.
            if (_overrides.Contains(prop.Name)) continue;
            UpdatePropertyState(prop);
        }
        MarkDirty();
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
