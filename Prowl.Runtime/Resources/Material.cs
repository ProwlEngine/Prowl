// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Prowl.Echo;
using Prowl.Graphite;
using Prowl.Graphite.ShaderDef;
using Prowl.Runtime.Rendering;
using Prowl.Vector;

using ShaderProperty = Prowl.Runtime.Rendering.Shaders.ShaderProperty;
using ShaderPropertyType = Prowl.Runtime.Rendering.Shaders.ShaderPropertyType;

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
            Resources.DefaultMaterial.Standard => "Standard.mat",
            Resources.DefaultMaterial.Particle => "Particle.mat",
            Resources.DefaultMaterial.Terrain => "Standard Terrain.mat",
            Resources.DefaultMaterial.Grass => "Grass.mat",
            _ => throw new ArgumentException($"Unknown default material: {material}")
        };

        string resourcePath = $"Assets/Defaults/{fileName}";
        using Stream stream = EmbeddedResources.GetStream(resourcePath);
        using var reader = new StreamReader(stream);
        string text = reader.ReadToEnd();
        var echo = EchoObject.ReadFromString(text);
        var mat = Serializer.Deserialize<Material>(echo);

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

    /// <summary>
    /// Names of properties the user has explicitly set (vs auto-filled
    /// shader defaults). When the shader's defaults change, only NON-overridden
    /// entries get refreshed user customizations are preserved. Without this,
    /// stale defaults stick around forever.
    /// </summary>
    [SerializeField]
    public HashSet<string> PropertyOverrides = new();

    [SerializeIgnore]
    internal Dictionary<int, Keyword> _localKeywords;

    // Serializable mirror of every property value set on this material
    [SerializeField]
    internal Dictionary<string, MaterialProperty> _properties;

    [SerializeIgnore]
    private ulong _stateHash;

    [SerializeIgnore]
    private bool _isDirty = true;


    public Material() : base("New Material")
    {
        _properties = new();
        _localKeywords = new();

        // Default to Standard shader so new materials are immediately usable
        var standard = Shader.LoadDefault(DefaultShader.Standard);

        if (standard != null)
            Shader = standard;
    }


    public Material(Shader shader) : base("New Material")
    {
        ArgumentNullException.ThrowIfNull(shader);

        _properties = new();
        _localKeywords = new();

        Shader = shader;
    }


    /// <summary>
    /// Copy constructor deep-clone every property value + a fresh keyword set. The
    /// <see cref="Shader"/> reference is shared (shaders are immutable after parse).
    /// Use this when you need a mutable material seeded from <see cref="LoadDefault"/>
    /// or any other shared material so your mutations don't leak to other callers.
    /// </summary>
    public Material(Material source) : base(source?.Name ?? "New Material")
    {
        ArgumentNullException.ThrowIfNull(source);

        _shader = source._shader;

        _properties = new Dictionary<string, MaterialProperty>(source._properties ?? []);

        _localKeywords = new(source._localKeywords ?? []);
        PropertyOverrides = new(source.PropertyOverrides ?? []);
    }


    /// <summary>Returns a deep copy of this material (see <see cref="Material(Material)"/>).</summary>
    public Material Clone() { EnsureNotDisposed(); return new Material(this); }

    /// <summary>Records a local keyword as currently set on this material.</summary>
    public void SetKeyword(Keyword keyword)
    {
        _localKeywords[keyword.NameId] = keyword;
    }

    /// <summary>
    /// Sets a boolean feature keyword (e.g. "HAS_NORMALS"). Bridges the old string+bool keyword API
    /// onto the variant-system <see cref="Keyword"/> (value "true"/"false").
    /// </summary>
    public void SetKeyword(string name, bool enabled)
        => SetKeyword(new Keyword(name, enabled ? "true" : "false"));

    private void Store(string name, MaterialProperty value)
    {
        _properties[name] = value;
        MarkDirty();
    }

    // Every public Set marks the property as user-overridden so subsequent shader
    // default-refreshes won't stomp the user's value.
    public void SetColor(string name, Color value) { PropertyOverrides.Add(name); Store(name, MaterialProperty.FromColor(value)); }
    public void SetVector(string name, Float2 value) { PropertyOverrides.Add(name); Store(name, MaterialProperty.FromVector(value)); }
    public void SetVector(string name, Float3 value) { PropertyOverrides.Add(name); Store(name, MaterialProperty.FromVector(value)); }
    public void SetVector(string name, Float4 value) { PropertyOverrides.Add(name); Store(name, MaterialProperty.FromVector(value)); }
    public void SetFloat(string name, float value) { PropertyOverrides.Add(name); Store(name, MaterialProperty.FromFloat(value)); }
    public void SetInt(string name, int value) { PropertyOverrides.Add(name); Store(name, MaterialProperty.FromInt(value)); }
    public void SetMatrix(string name, Float4x4 value) { PropertyOverrides.Add(name); Store(name, MaterialProperty.FromMatrix(value)); }
    public void SetTexture(string name, Texture2D value) { PropertyOverrides.Add(name); Store(name, MaterialProperty.FromTexture(new AssetRef<Texture2D>(value))); }
    public void SetTexture(string name, AssetRef<Texture2D> value) { PropertyOverrides.Add(name); Store(name, MaterialProperty.FromTexture(value)); }
    public void SetTexture3D(string name, Texture3D value) { PropertyOverrides.Add(name); Store(name, MaterialProperty.FromTexture3D(new AssetRef<Texture3D>(value))); }
    public void SetTexture3D(string name, AssetRef<Texture3D> value) { PropertyOverrides.Add(name); Store(name, MaterialProperty.FromTexture3D(value)); }
    public void SetTextureCube(string name, Cubemap value) { PropertyOverrides.Add(name); Store(name, MaterialProperty.FromTextureCube(new AssetRef<Cubemap>(value))); }
    public void SetTextureCube(string name, AssetRef<Cubemap> value) { PropertyOverrides.Add(name); Store(name, MaterialProperty.FromTextureCube(value)); }

    /// <summary>
    /// Builds a fresh runtime <see cref="PropertySet"/> from this material's stored
    /// property values. Numeric values map to their typed uniform setters; textures
    /// are resolved from their <see cref="AssetRef{T}"/> and bound with their sampler,
    /// skipping any that aren't currently loaded.
    /// </summary>
    public PropertySet BuildPropertySet()
    {
        var set = new PropertySet(_properties.Count);

        foreach (KeyValuePair<string, MaterialProperty> kv in _properties)
        {
            string name = kv.Key;
            MaterialProperty prop = kv.Value;

            switch (prop.Type)
            {
                case MaterialPropertyType.Float:
                    set.SetFloat(name, prop.Value.X);
                    break;

                case MaterialPropertyType.Int:
                    set.SetInt(name, (int)prop.Value.X);
                    break;

                case MaterialPropertyType.Vector2:
                    set.SetFloat2(name, new Float2(prop.Value.X, prop.Value.Y));
                    break;

                case MaterialPropertyType.Vector3:
                    set.SetFloat3(name, new Float3(prop.Value.X, prop.Value.Y, prop.Value.Z));
                    break;

                case MaterialPropertyType.Vector4:
                case MaterialPropertyType.Color:
                    set.SetFloat4(name, prop.Value);
                    break;

                case MaterialPropertyType.Matrix:
                    set.SetMatrix(name, prop.Matrix);
                    break;

                case MaterialPropertyType.Texture2D:
                    {
                        Texture2D? tex = prop.Tex2D.Res;
                        if (tex?.Handle != null)
                            set.SetTexture(name, tex.Handle, tex.Sampler);
                        break;
                    }

                case MaterialPropertyType.Texture3D:
                    {
                        Texture3D? tex = prop.Tex3D.Res;
                        if (tex?.Handle != null)
                            set.SetTexture(name, tex.Handle, tex.Sampler);
                        break;
                    }

                case MaterialPropertyType.TextureCube:
                    {
                        Cubemap? tex = prop.TexCube.Res;
                        if (tex?.Handle != null)
                            set.SetTexture(name, tex.Handle, tex.Sampler);
                        break;
                    }
            }
        }

        return set;
    }

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
        PropertyOverrides.Remove(name);
        _properties.Remove(name);
        MarkDirty();
    }

    /// <summary>True if the user has explicitly set this property (vs holding the
    /// shader's default value). Inspector uses this to highlight overridden fields.</summary>
    public bool IsOverridden(string name) => PropertyOverrides.Contains(name);

    private void UpdatePropertyState(ShaderProperty property)
    {
        switch (property.PropertyType)
        {
            case ShaderPropertyType.Texture2D:
                Store(property.Name, MaterialProperty.FromTexture(new AssetRef<Texture2D>(property.Texture2DValue)));
                break;

            case ShaderPropertyType.Texture3D:
                Store(property.Name, MaterialProperty.FromTexture3D(new AssetRef<Texture3D>(property.Texture3DValue)));
                break;

            case ShaderPropertyType.Float:
                Store(property.Name, MaterialProperty.FromFloat((float)property));
                break;

            case ShaderPropertyType.Int:
                Store(property.Name, MaterialProperty.FromInt((int)property));
                break;

            case ShaderPropertyType.Vector2:
                Store(property.Name, MaterialProperty.FromVector((Float2)property));
                break;

            case ShaderPropertyType.Vector3:
                Store(property.Name, MaterialProperty.FromVector((Float3)property));
                break;

            case ShaderPropertyType.Vector4:
                Store(property.Name, MaterialProperty.FromVector((Float4)property));
                break;

            case ShaderPropertyType.Color:
                Store(property.Name, MaterialProperty.FromColor((Color)property));
                break;

            case ShaderPropertyType.Matrix:
                Store(property.Name, MaterialProperty.FromMatrix((Float4x4)property));
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
            _stateHash = ComputeHash();
            _isDirty = false;
        }
        return _stateHash;
    }

    /// <summary>
    /// Computes an FNV-1a 64-bit hash over every stored property. Entries are ordered
    /// by name so the result is stable regardless of dictionary iteration order.
    /// </summary>
    private ulong ComputeHash()
    {
        ulong hash = 14695981039346656037UL; // FNV-1a offset basis

        foreach (KeyValuePair<string, MaterialProperty> kv in _properties.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            hash ^= (ulong)kv.Key.GetHashCode();
            hash *= 1099511628211UL;
            hash ^= (ulong)kv.Value.GetHashCode();
            hash *= 1099511628211UL;
        }

        return hash;
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
        if (PropertyOverrides == null) PropertyOverrides = new HashSet<string>();
        _properties ??= new();
        _localKeywords ??= new();
        if (PropertyOverrides.Count == 0)
        {
            foreach (string name in _properties.Keys)
                PropertyOverrides.Add(name);
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
            if (PropertyOverrides.Contains(prop.Name))
                continue;

            UpdatePropertyState(prop);
        }
    }
}
