// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Prowl.Echo;
using Prowl.Graphite.Rendering;
using Prowl.Graphite.ShaderDef;
using Prowl.Vector;

using GMaterial = Prowl.Graphite.Rendering.Material;
using GShader = Prowl.Graphite.Rendering.Shader;
using GTexture = Prowl.Graphite.Texture;

namespace Prowl.Runtime.Resources;

/// <summary>
/// A shader with values for its properties and keywords. It keeps its values itself, so it saves, loads and works
/// headless, and mirrors them into a Graphite material once a renderer draws with it.
/// </summary>
[CreateAssetMenu("Material", Extension = ".mat", Order = 1)]
public sealed class Material : Asset, ISerializationCallbackReceiver
{
    /// <summary>Returns a new material of the Lit shader.</summary>
    public static Material DefaultMaterial => new(Shader.LoadDefault(DefaultShader.Lit));

    /// <summary>A fresh copy of a built in material, so callers can change it without touching anyone else's.</summary>
    public static Material LoadDefault(DefaultMaterial material)
        => new(BuiltInAssets.Load<Material>(BuiltInAssets.GuidFor(material)));

    /// <summary>Raw read of a built in material, called by <see cref="BuiltInAssets"/> on its first use.</summary>
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

        using Stream stream = EmbeddedResources.GetStream($"Assets/Defaults/{fileName}");
        using var reader = new StreamReader(stream);
        return Serializer.Deserialize<Material>(EchoObject.ReadFromString(reader.ReadToEnd()));
    }

    [SerializeField]
    private Shader? _shader;

    [SerializeField]
    private Material? _parent;

    /// <summary>
    /// The material's shader. Never set, it is the parent's, or Lit without a parent. Missing or failed to load, it is
    /// the Invalid shader, so the problem shows.
    /// </summary>
    public Shader? Shader
    {
        get
        {
            EnsureLoaded();
            if (_shader.IsNotValid())
                return _parent.IsValid() ? _parent.Shader : Shader.LoadDefault(DefaultShader.Lit);
            return _shader.State is AssetState.Missing or AssetState.Failed ? Shader.LoadDefault(DefaultShader.Invalid) : _shader;
        }
        set { EnsureLoaded(); SetShader(value); }
    }

    /// <summary>
    /// The material this one is a variant of: every value, keyword and surface setting it does not set itself comes from
    /// the parent, and follows the parent's changes.
    /// </summary>
    public Material? Parent
    {
        get { EnsureLoaded(); return _parent; }
        set
        {
            EnsureLoaded();
            for (Material? m = value; m.IsValid(); m = m._parent)
                if (m == this) throw new InvalidOperationException("A material cannot be a variant of itself.");
            _parent = value;
            if (_gpu != null) _gpu.Parent = value.IsValid() ? value.GpuMaterial : null;
            SyncShader();
        }
    }

    [SerializeField]
    public MaterialProperties _properties;

    /// <summary>Names of the properties set on this material. The others read the shader's defaults.</summary>
    [SerializeField]
    public HashSet<string> _overrides = [];

    /// <summary>The value of each keyword axis this material sets.</summary>
    [SerializeField]
    private Dictionary<string, string> _keywords = [];

    [SerializeField]
    private int _renderQueue = -1;

    [SerializeField]
    private bool _castShadows = true;

    public Material() : base("New Material")
    {
        _properties = new();
    }

    public Material(Shader shader, MaterialProperties? properties = null, Dictionary<string, bool>? keywords = null) : base("New Material")
    {
        ArgumentNullException.ThrowIfNull(shader);
        _properties = properties != null ? new MaterialProperties(properties) : new();
        if (properties != null)
            foreach (string name in _properties.EnumerateNames()) _overrides.Add(name);
        _shader = shader;
        if (keywords != null)
            foreach (KeyValuePair<string, bool> keyword in keywords) _keywords[keyword.Key] = keyword.Value ? VariantSpace.ToggleValues[1] : VariantSpace.ToggleValues[0];
    }

    /// <summary>A copy of another material that shares its shader and textures.</summary>
    public Material(Material source) : base(source.IsValid() ? source.Name : "New Material")
    {
        ArgumentNullException.ThrowIfNull(source);
        source.EnsureLoaded();

        _shader = source._shader;
        _parent = source._parent;
        _surface = source._surface;
        _properties = new MaterialProperties(source._properties);
        _overrides = new HashSet<string>(source._overrides);
        _keywords = new Dictionary<string, string>(source._keywords);
        _renderQueue = source._renderQueue;
        _castShadows = source._castShadows;
    }

    /// <summary>Returns a copy of this material (see <see cref="Material(Material)"/>).</summary>
    public Material Clone() { EnsureLoaded(); return new Material(this); }

    internal void SetShader(Shader? shader)
    {
        if (shader == _shader) return;
        _shader = shader;
        SyncShader();
    }

    #region Values

    public void SetColor(string name, Color value) { EnsureLoaded(); _overrides.Add(name); _properties.SetColor(name, value); Push(name); }
    public void SetVector(string name, Float2 value) { EnsureLoaded(); _overrides.Add(name); _properties.SetVector(name, value); Push(name); }
    public void SetVector(string name, Float3 value) { EnsureLoaded(); _overrides.Add(name); _properties.SetVector(name, value); Push(name); }
    public void SetVector(string name, Float4 value) { EnsureLoaded(); _overrides.Add(name); _properties.SetVector(name, value); Push(name); }
    public void SetFloat(string name, float value) { EnsureLoaded(); _overrides.Add(name); _properties.SetFloat(name, value); Push(name); }
    public void SetInt(string name, int value) { EnsureLoaded(); _overrides.Add(name); _properties.SetInt(name, value); Push(name); }
    public void SetMatrix(string name, Float4x4 value) { EnsureLoaded(); _overrides.Add(name); _properties.SetMatrix(name, value); Push(name); }
    public void SetTexture(string name, Texture2D? value) { EnsureLoaded(); _overrides.Add(name); _properties.SetTexture(name, value); Push(name); }
    public void SetTexture3D(string name, Texture3D? value) { EnsureLoaded(); _overrides.Add(name); _properties.SetTexture3D(name, value); Push(name); }
    public void SetTextureCube(string name, Cubemap? value) { EnsureLoaded(); _overrides.Add(name); _properties.SetTextureCube(name, value); Push(name); }

    /// <summary>The material's colour, or the shader's default when it sets none.</summary>
    public Color GetColor(string name)
    {
        EnsureLoaded();
        if (_properties.HasColor(name)) return _properties.GetColor(name);
        if (_parent.IsValid()) return _parent.GetColor(name);
        return Default(name) is { } property ? ToColor(property.Default.Vector) : Color.White;
    }

    public float GetFloat(string name)
    {
        EnsureLoaded();
        if (_properties.HasFloat(name)) return _properties.GetFloat(name);
        if (_properties.HasInt(name)) return _properties.GetInt(name);
        if (_parent.IsValid()) return _parent.GetFloat(name);
        return Default(name)?.Default.Float ?? 0;
    }

    public int GetInt(string name)
    {
        EnsureLoaded();
        if (_properties.HasInt(name)) return _properties.GetInt(name);
        if (_properties.HasFloat(name)) return (int)_properties.GetFloat(name);
        if (_parent.IsValid()) return _parent.GetInt(name);
        return Default(name)?.Default.Int ?? 0;
    }

    public Float4 GetVector(string name)
    {
        EnsureLoaded();
        if (_properties.HasVector4(name)) return _properties.GetVector4(name);
        if (_properties.HasVector3(name)) { Float3 v = _properties.GetVector3(name); return new Float4(v.X, v.Y, v.Z, 0); }
        if (_properties.HasVector2(name)) { Float2 v = _properties.GetVector2(name); return new Float4(v.X, v.Y, 0, 0); }
        if (_parent.IsValid()) return _parent.GetVector(name);
        return Default(name)?.Default.Vector ?? Float4.Zero;
    }

    public Float4x4 GetMatrix(string name)
    {
        EnsureLoaded();
        if (_properties.HasMatrix(name)) return _properties.GetMatrix(name);
        return _parent.IsValid() ? _parent.GetMatrix(name) : Default(name)?.Default.Matrix ?? Float4x4.Identity;
    }

    /// <summary>Sets a texture's scale (xy) and offset (zw), kept as the vector "name_ST".</summary>
    public void SetTextureScaleOffset(string name, Float4 scaleOffset) => SetVector(name + "_ST", scaleOffset);

    /// <summary>Sets the channel a map reads a scalar input from, 0 to 3 for R to A, kept as the int "name_Channel".</summary>
    public void SetTextureChannel(string name, int channel) => SetInt(name + "_Channel", channel);

    /// <summary>Sets the UV set a map samples, 0 or 1, kept as the int "name_UVSet".</summary>
    public void SetTextureUVSet(string name, int uvSet) => SetInt(name + "_UVSet", uvSet);

    /// <summary>A texture's scale (xy) and offset (zw), (1, 1, 0, 0) when nothing sets it.</summary>
    public Float4 GetTextureScaleOffset(string name)
    {
        EnsureLoaded();
        string key = name + "_ST";
        if (_properties.HasVector4(key)) return _properties.GetVector4(key);
        return _parent.IsValid() ? _parent.GetTextureScaleOffset(name) : MaterialValue.IdentityScaleOffset;
    }

    /// <summary>Turns a map's UV counter clockwise by radians after its scale, before its offset, kept as the float "name_Rotation".</summary>
    public void SetTextureRotation(string name, float radians) => SetFloat(name + "_Rotation", radians);

    /// <summary>The channel a map reads a scalar input from, R (0) when nothing sets it.</summary>
    public int GetTextureChannel(string name)
    {
        EnsureLoaded();
        string key = name + "_Channel";
        if (_properties.HasInt(key)) return _properties.GetInt(key);
        return _parent.IsValid() ? _parent.GetTextureChannel(name) : 0;
    }

    /// <summary>The UV set a map samples, 0 when nothing sets it.</summary>
    public int GetTextureUVSet(string name)
    {
        EnsureLoaded();
        string key = name + "_UVSet";
        if (_properties.HasInt(key)) return _properties.GetInt(key);
        return _parent.IsValid() ? _parent.GetTextureUVSet(name) : 0;
    }

    /// <summary>A map's rotation in radians, 0 when nothing sets it.</summary>
    public float GetTextureRotation(string name)
    {
        EnsureLoaded();
        string key = name + "_Rotation";
        if (_properties.HasFloat(key)) return _properties.GetFloat(key);
        return _parent.IsValid() ? _parent.GetTextureRotation(name) : 0f;
    }

    public Texture2D? GetTexture(string name) { EnsureLoaded(); return _overrides.Contains(name) || _parent.IsNotValid() ? _properties.GetTexture(name) : _parent.GetTexture(name); }
    public Texture3D? GetTexture3D(string name) { EnsureLoaded(); return _overrides.Contains(name) || _parent.IsNotValid() ? _properties.GetTexture3D(name) : _parent.GetTexture3D(name); }
    public Cubemap? GetTextureCube(string name) { EnsureLoaded(); return _overrides.Contains(name) || _parent.IsNotValid() ? _properties.GetTextureCube(name) : _parent.GetTextureCube(name); }

    /// <summary>Whether the shader declares a property of that name.</summary>
    public bool HasProperty(string name) { EnsureLoaded(); return Default(name) != null; }

    private MaterialProperty? Default(string name)
    {
        Shader? shader = Shader;
        return shader.IsValid() ? shader.FindProperty(name) : null;
    }

    private static Color ToColor(Float4 v) => new(v.X, v.Y, v.Z, v.W);

    /// <summary>Forgets the value set for <paramref name="name"/>, so it reads the shader's default again.</summary>
    public void RevertProperty(string name)
    {
        EnsureLoaded();
        _overrides.Remove(name);
        _properties.Remove(name);
        _gpu?.ClearOverride(name);
        _boundTextures.Remove(name);
        if (_gpu != null && SettingTexture(name) is { } texture)
        {
            _gpu.ClearOverride(texture);
            Push(texture);
            foreach (string suffix in s_textureSettings) Push(texture + suffix);
        }
    }

    /// <summary>True if this material sets the property rather than reading the shader's default.</summary>
    public bool IsOverridden(string name) { EnsureLoaded(); return _overrides.Contains(name); }

    /// <summary>Kept for callers from before defaults were read live from the shader. Does nothing.</summary>
    public void SyncShaderDefaults() { }

    #endregion

    #region Keywords

    /// <summary>Turns a toggle keyword on or off.</summary>
    public void SetKeyword(string keyword, bool value) => SetKeyword(keyword, value ? VariantSpace.ToggleValues[1] : VariantSpace.ToggleValues[0]);

    /// <summary>Sets the value of a keyword axis, such as <c>_SURFACE</c> to <c>CUTOUT</c>.</summary>
    public void SetKeyword(string axis, string value)
    {
        EnsureLoaded();
        if (_keywords.TryGetValue(axis, out string? current) && current == value) return;
        _keywords[axis] = value;
        PushKeyword(axis, value);
    }

    /// <summary>The material axis of a Graphite shader by name, null when it has none of that name.</summary>
    public static MaterialAxis? FindAxis(GShader shader, string name)
    {
        foreach (VariantSpace axis in shader.MaterialAxes)
            if (axis.Name == name) return shader.Axis(name);
        return null;
    }

    // Axes the shader lacks, or values it does not take, stay on this material only
    private void PushKeyword(string axis, string? value)
    {
        if (_gpu == null || FindAxis(_gpu.Shader, axis) is not { } gpuAxis) return;
        if (value != null && !gpuAxis.Values.Contains(value))
        {
            Debug.LogWarningOnce($"Material.Keyword.{_gpu.Shader.Name}.{axis}.{value}", $"The axis {axis} of {_gpu.Shader.Name} has no value {value}, only {string.Join(" ", gpuAxis.Values)}.");
            return;
        }
        _gpu.Set(gpuAxis, value);
    }

    /// <summary>
    /// The value a keyword axis draws with: set here or on a parent, else what the surface and properties drive, else the
    /// axis default. Headless, only what is set here or on a parent.
    /// </summary>
    public string GetKeyword(string axis)
    {
        EnsureLoaded();
        if (_keywords.TryGetValue(axis, out string? value)) return value;
        if (GpuMaterial is { } gpu) return FindAxis(gpu.Shader, axis) is { } gpuAxis ? gpu.Get(gpuAxis) : "";
        return _parent.IsValid() ? _parent.GetKeyword(axis) : "";
    }

    /// <summary>Whether this material sets the axis itself.</summary>
    public bool IsKeywordOverridden(string axis) { EnsureLoaded(); return _keywords.ContainsKey(axis); }

    /// <summary>Forgets the keyword set for an axis, so it follows the surface, properties or parent again.</summary>
    public void RevertKeyword(string axis)
    {
        EnsureLoaded();
        if (_keywords.Remove(axis)) PushKeyword(axis, null);
    }

    /// <summary>Whether a toggle keyword is on.</summary>
    public bool IsKeywordEnabled(string keyword) => GetKeyword(keyword) == VariantSpace.ToggleValues[1];

    /// <summary>The keyword axes this material sets.</summary>
    public IReadOnlyDictionary<string, string> Keywords { get { EnsureLoaded(); return _keywords; } }

    #endregion

    [SerializeField]
    private SurfaceData? _surface;

    /// <summary>
    /// How the surface draws: opaque, cut out or transparent, its blend and culling. Shaders of the standard family follow
    /// it through their _SURFACE axis, and any shader takes its render state and queue. Null inherits the parent's, and
    /// with none the shader's own passes decide.
    /// </summary>
    public SurfaceSettings? Surface
    {
        get
        {
            EnsureLoaded();
            if (_surface is { } local) return local.Settings;
            return _parent.IsValid() ? _parent.Surface : null;
        }
        set
        {
            EnsureLoaded();
            _surface = value is { } settings ? new SurfaceData(settings) : null;
            if (_gpu != null) _gpu.Surface = _surface?.Settings.ToGraphite();
        }
    }

    /// <summary>Whether this material sets its surface itself rather than taking the parent's or the shader's.</summary>
    public bool IsSurfaceOverridden { get { EnsureLoaded(); return _surface != null; } }

    /// <summary>The queue the material draws in. Negative takes the surface's, the parent's, or else the shader's.</summary>
    public int RenderQueue
    {
        get
        {
            EnsureLoaded();
            if (_renderQueue >= 0) return _renderQueue;
            if (_surface is { } local) return local.Settings.RenderQueue;
            if (_parent.IsValid()) return _parent.RenderQueue;
            Shader? shader = Shader;
            return shader.IsValid() ? shader.RenderQueue : GShader.DefaultRenderQueue;
        }
        set { EnsureLoaded(); _renderQueue = value; if (_gpu != null) _gpu.RenderQueue = value; }
    }

    /// <summary>Whether this material sets its queue itself.</summary>
    public bool IsRenderQueueOverridden { get { EnsureLoaded(); return _renderQueue >= 0; } }

    /// <summary>Whether objects drawn with the material cast shadows.</summary>
    public bool CastShadows
    {
        get { EnsureLoaded(); return _castShadows; }
        set { EnsureLoaded(); _castShadows = value; if (_gpu != null) _gpu.CastShadows = value; }
    }

    #region Global Properties

    public static void SetGlobalColor(string name, Color value) => Graphics.Renderer?.Globals.SetColor(name, new Float4(value.R, value.G, value.B, value.A));
    public static void SetGlobalVector(string name, Float2 value) => Graphics.Renderer?.Globals.SetFloat2(name, value);
    public static void SetGlobalVector(string name, Float3 value) => Graphics.Renderer?.Globals.SetFloat3(name, value);
    public static void SetGlobalVector(string name, Float4 value) => Graphics.Renderer?.Globals.SetFloat4(name, value);
    public static void SetGlobalFloat(string name, float value) => Graphics.Renderer?.Globals.SetFloat(name, value);
    public static void SetGlobalInt(string name, int value) => Graphics.Renderer?.Globals.SetInt(name, value);
    public static void SetGlobalMatrix(string name, Float4x4 value) => Graphics.Renderer?.Globals.SetMatrix(name, value);
    public static void SetGlobalTexture(string name, Texture2D value) => Graphics.Renderer?.Globals.SetTexture(name, value.GpuTexture, value.Sampler);
    public static void SetGlobalTexture3D(string name, Texture3D value) => Graphics.Renderer?.Globals.SetTexture(name, value.GpuTexture, value.Sampler);

    #endregion

    #region Graphite

    private static readonly List<WeakReference<Material>> s_live = [];

    [NonSerialized, NotHeld, SerializeIgnore, NotContent]
    private GMaterial? _gpu;

    // The Graphite texture and sampler last given for each texture property, so a texture that changed is given again
    [NonSerialized, NotHeld, SerializeIgnore, NotContent]
    private Dictionary<string, (GTexture? Texture, Graphite.SamplerDescription Sampler)> _boundTextures = [];

    /// <summary>
    /// The Graphite material, made with every value on first use. Null when headless or when neither the shader nor the
    /// Invalid shader parsed.
    /// </summary>
    public GMaterial? GpuMaterial
    {
        get
        {
            EnsureLoaded();
            if (_gpu != null || Graphics.Renderer == null) return _gpu;
            GShader? shader = ResolveShader();
            if (shader == null) return null;
            _gpu = Graphics.Renderer.CreateMaterial(shader);
            _boundTextures = [];
            if (_parent.IsValid()) _gpu.Parent = _parent.GpuMaterial;
            PushAll();
            lock (s_live) s_live.Add(new WeakReference<Material>(this));
            return _gpu;
        }
    }

    // A shader not loaded yet starts loading, and the Invalid shader stands in until SyncShader sees it ready
    private GShader? ResolveShader()
    {
        Shader? shader = Shader;
        if (shader.IsValid() && shader.GpuIfLoaded is { } gpu) return gpu;
        if (shader.IsValid() && shader.State == AssetState.Unloaded) _ = shader.LoadAsync();
        Shader invalid = Shader.GetDefault(DefaultShader.Invalid);
        return invalid.IsValid() ? invalid.GpuShader : null;
    }

    private void SyncShader()
    {
        if (_gpu == null) return;
        GShader? shader = ResolveShader();
        if (shader == null || shader == _gpu.Shader) return;
        _gpu.Shader = shader;
        _boundTextures.Clear();
        PushAll();
    }

    private void PushAll()
    {
        if (_gpu == null) return;
        foreach ((Graphite.PropertyID name, _) in new List<(Graphite.PropertyID, MaterialValue)>(_gpu.LocalValues))
            if (!_overrides.Contains(Graphite.PropertyID.ToString(name) ?? "")) _gpu.ClearOverride(name);
        foreach (string name in _properties.EnumerateNames()) Push(name);
        foreach (KeyValuePair<string, string> keyword in _keywords) PushKeyword(keyword.Key, keyword.Value);
        _gpu.RenderQueue = _renderQueue;
        _gpu.CastShadows = _castShadows;
        _gpu.Surface = _surface?.Settings.ToGraphite();
    }

    // Gives one value to the Graphite material, when the shader has the property
    private void Push(string name)
    {
        if (_gpu == null) return;
        if (_gpu.Shader.FindProperty(name) < 0)
        {
            if (SettingTexture(name) is not { } texture) return;
            if (name.EndsWith("_ST", StringComparison.Ordinal) && _properties.HasVector4(name))
                _gpu.SetTextureScaleOffset(texture, _properties.GetVector4(name));
            else if (name.EndsWith("_Channel", StringComparison.Ordinal) && _properties.HasInt(name))
                _gpu.SetTextureChannel(texture, _properties.GetInt(name));
            else if (name.EndsWith("_UVSet", StringComparison.Ordinal) && _properties.HasInt(name))
                _gpu.SetTextureUVSet(texture, _properties.GetInt(name));
            else if (name.EndsWith("_Rotation", StringComparison.Ordinal) && _properties.HasFloat(name))
                _gpu.SetTextureRotation(texture, _properties.GetFloat(name));
            return;
        }
        MaterialProperties p = _properties;
        if (p._colors.TryGetValue(name, out Color color)) _gpu.SetColor(name, new Float4(color.R, color.G, color.B, color.A));
        else if (p._floats.TryGetValue(name, out float f)) PushNumber(name, f);
        else if (p._ints.TryGetValue(name, out int i)) PushNumber(name, i);
        else if (p._vectors2.TryGetValue(name, out Float2 v2)) _gpu.SetFloat2(name, v2);
        else if (p._vectors3.TryGetValue(name, out Float3 v3)) _gpu.SetFloat3(name, v3);
        else if (p._vectors4.TryGetValue(name, out Float4 v4)) PushVector(name, v4);
        else if (p._matrices.TryGetValue(name, out Float4x4 m)) _gpu.SetMatrix(name, m);
        else if (p._textures.TryGetValue(name, out Texture2D? t2)) PushTexture(name, t2);
        else if (p._textures3D.TryGetValue(name, out Texture3D? t3)) PushTexture(name, t3);
        else if (p._texturesCube.TryGetValue(name, out Cubemap? tc)) PushTexture(name, tc);
    }

    private static readonly string[] s_textureSettings = ["_ST", "_Channel", "_UVSet", "_Rotation"];

    // The texture property a "name_ST", "name_Channel", "name_UVSet" or "name_Rotation" setting belongs to, or null
    private string? SettingTexture(string name)
    {
        if (_gpu == null) return null;
        foreach (string suffix in s_textureSettings)
        {
            if (!name.EndsWith(suffix, StringComparison.Ordinal)) continue;
            string texture = name[..^suffix.Length];
            int index = _gpu.Shader.FindProperty(texture);
            return index >= 0 && _gpu.Shader.Properties[index].Type >= MaterialPropertyType.Texture2D ? texture : null;
        }
        return null;
    }

    // Older materials store ints and floats under either kind, so the value follows the shader's type
    private void PushNumber(string name, float value)
    {
        MaterialPropertyType type = _gpu!.Shader.Properties[_gpu.Shader.FindProperty(name)].Type;
        if (type == MaterialPropertyType.Int) _gpu.SetInt(name, (int)value);
        else _gpu.SetFloat(name, value);
    }

    private void PushVector(string name, Float4 value)
    {
        MaterialPropertyType type = _gpu!.Shader.Properties[_gpu.Shader.FindProperty(name)].Type;
        if (type == MaterialPropertyType.Color) _gpu.SetColor(name, value);
        else _gpu.SetFloat4(name, value);
    }

    private void PushTexture(string name, Texture? texture)
    {
        if (_gpu!.Shader.FindProperty(name) < 0) return;
        if (texture.IsValid() && BuiltInAssets.IsPlaceholderTexture(texture.AssetID)) texture = null;
        GTexture? gpu = texture.IsValid() ? texture.GpuIfLoaded : null;
        Graphite.SamplerDescription sampler = texture.IsValid() ? texture.SamplerUnchecked : Graphite.SamplerDescription.Linear;
        _gpu!.SetTexture(name, gpu, sampler);
        _boundTextures[name] = (gpu, sampler);
    }

    private static readonly List<(string Name, Texture Texture)> s_stale = [];

    // Gives again every texture whose Graphite texture or sampler changed, such as one that finished loading
    private void SyncTextures()
    {
        foreach (KeyValuePair<string, (GTexture? Texture, Graphite.SamplerDescription Sampler)> bound in _boundTextures)
        {
            Texture? texture = _properties.GetTexture(bound.Key);
            if (texture.IsNotValid()) texture = _properties.GetTexture3D(bound.Key);
            if (texture.IsNotValid()) texture = _properties.GetTextureCube(bound.Key);
            if (texture.IsNotValid()) continue;
            if (texture.State == AssetState.Unloaded && bound.Value.Texture == null)
                _ = texture.LoadAsync();
            GTexture? gpu = texture.GpuIfLoaded;
            if (gpu != bound.Value.Texture || (gpu != null && !texture.SamplerUnchecked.Equals(bound.Value.Sampler)))
                s_stale.Add((bound.Key, texture));
        }
        foreach ((string name, Texture texture) in s_stale) PushTexture(name, texture);
        s_stale.Clear();
    }

    /// <summary>Brings every live Graphite material up to date with its shader and textures. Main thread, once a frame.</summary>
    internal static void SyncAll()
    {
        lock (s_live)
        {
            for (int i = s_live.Count - 1; i >= 0; i--)
            {
                if (!s_live[i].TryGetTarget(out Material? material) || material._gpu == null)
                {
                    s_live.RemoveAt(i);
                    continue;
                }
                if (!material.IsLoaded) continue;
                material.SyncShader();
                material.SyncTextures();
            }
        }
    }

    #endregion

    public void OnBeforeSerialize() { }

    public void OnAfterDeserialize()
    {
        _overrides ??= [];
        _keywords ??= [];
        _properties ??= new();
        if (_overrides.Count == 0)
            foreach (string name in _properties.EnumerateNames()) _overrides.Add(name);
    }

    // The Graphite material outlives reloads, so renderers drawing with it see the new values
    protected override void TakeContent(Asset staging)
    {
        base.TakeContent(staging);
        if (_gpu == null) return;
        _boundTextures.Clear();
        SyncShader();
        PushAll();
    }

    protected override void OnUnload()
    {
        if (!IsDisposed) return;
        Graphics.DeferDispose(_gpu);
        _gpu = null;
    }

    // Echo friendly copy of the surface settings
    private struct SurfaceData
    {
        public SurfaceType Type;
        public SurfaceBlend Blend;
        public SurfaceCull Cull;

        public SurfaceData(SurfaceSettings settings) { Type = settings.Type; Blend = settings.Blend; Cull = settings.Cull; }

        public readonly SurfaceSettings Settings => new(Type, Blend, Cull);
    }

    // A runtime material nothing references still frees its Graphite material. Database materials are never collected.
    ~Material()
    {
        if (!Registered) Dispose();
    }
}
