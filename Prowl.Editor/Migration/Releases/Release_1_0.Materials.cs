// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Echo;

namespace Prowl.Editor.Migration.Releases;

internal sealed partial class Release_1_0
{
    private const string MaterialType = "Prowl.Runtime.Resources.Material";

    // BuiltInAssets.GuidFor of the shaders and placeholder textures
    internal static readonly Guid LitShader = new("63d5be44-f531-7354-a255-717cb71004bb");
    internal static readonly Guid UnlitShader = new("d782631b-1c8c-dc5c-a4ab-2269e945dac5");
    internal static readonly Guid GrassShader = new("a95b73d2-d6fb-b25b-8918-a1ad5616656c");
    internal static readonly Guid TerrainShader = new("96a4f4ef-53bf-7a5e-a12c-fec5060898f7");

    internal static readonly Guid ProceduralSkyShader = new("73295c5c-0e55-4550-ae74-f13cd62796d2");

    // Cubemap, Panorama and SixSided skies, which draw their texels as nits
    internal static readonly Guid[] TextureSkyShaders =
    [
        new("297b078c-ab54-2f59-b3ab-028c50f8653b"), new("e9e47399-dfb7-885c-b3db-0810c196199f"), new("71c042a1-257c-e154-9ca7-ef454643498e"),
    ];

    internal static readonly Guid[] ExposedSkyShaders = [ProceduralSkyShader, .. TextureSkyShaders];

    // White, Gray18, Normal, Surface and Emission
    internal static readonly Guid[] PlaceholderTextures =
    [
        new("61fe6b22-0991-6a59-8c79-69a3dba090a9"), new("2ee97772-6cc9-e253-b2e6-3cf300ec7d37"), new("0c9986f4-620f-3053-acf5-aaa504c7e31c"),
        new("96be834e-eb6c-fe51-a195-30e5cd7e9bed"), new("4ad67725-8cd3-f45c-a783-5e565ac55462"),
    ];

    // SurfaceType and SurfaceCull values
    private const long Opaque = 0, Cutout = 1, Transparent = 2, CullBack = 0, CullOff = 2;

    internal readonly record struct LegacyShader(bool Lit, long Type, long Cull, bool Anisotropic);

    /// <summary>The standard family's shaders from before Graphite, by the GUID of "$Default:Shader/{name}", and what replaces each.</summary>
    internal static readonly Dictionary<Guid, (string Name, LegacyShader Shader)> LegacyShaders = new()
    {
        [new("87bedf66-4e8b-0056-bc2e-4f8c7fc22a5a")] = ("Standard", new(true, Opaque, CullBack, false)),
        [new("c6106f68-16a6-b853-91af-d1881110020c")] = ("StandardDoubleSided", new(true, Opaque, CullOff, false)),
        [new("7e3083d0-d387-bb51-b517-7038b9b76c88")] = ("StandardCutout", new(true, Cutout, CullBack, false)),
        [new("ae29a3ac-51e2-5654-bfb5-8397b9c5b03b")] = ("StandardCutoutDoubleSided", new(true, Cutout, CullOff, false)),
        [new("240e4abf-a84d-9552-a2a5-838019d9176c")] = ("StandardTransparent", new(true, Transparent, CullBack, false)),
        [new("92cf3fc4-05df-4557-aa99-f645339b8a99")] = ("StandardTransparentDoubleSided", new(true, Transparent, CullOff, false)),
        [new("52ae7100-bf9b-7452-bc61-75b99ad23e08")] = ("StandardAnisotropic", new(true, Opaque, CullBack, true)),
        [new("87b20c59-6ac0-5550-82eb-94c197bb6d75")] = ("StandardAnisotropicDoubleSided", new(true, Opaque, CullOff, true)),
        [new("dd13ed73-9c83-a651-8eec-f27ad7b34282")] = ("StandardAnisotropicCutout", new(true, Cutout, CullBack, true)),
        [new("03a8d062-f44a-6e54-a721-2ead7bb060a9")] = ("StandardAnisotropicCutoutDoubleSided", new(true, Cutout, CullOff, true)),
        [new("393900fd-622a-825a-a3ac-299817ee4259")] = ("StandardAnisotropicTransparent", new(true, Transparent, CullBack, true)),
        [new("24deaefc-0cf1-8358-bea6-62d125445c77")] = ("StandardAnisotropicTransparentDoubleSided", new(true, Transparent, CullOff, true)),
        [new("128233d8-ed4d-c256-8b89-9259e666ee16")] = ("UnlitDoubleSided", new(false, Opaque, CullOff, false)),
        [new("c2e682d0-b298-575e-972e-63e5ac2c7ecb")] = ("UnlitCutout", new(false, Cutout, CullBack, false)),
        [new("e87160ba-dc98-8451-96cb-2b5ae412a276")] = ("UnlitCutoutDoubleSided", new(false, Cutout, CullOff, false)),
        [new("487ac35b-7c4e-2d59-b0be-1b7a652d21d2")] = ("UnlitTransparent", new(false, Transparent, CullBack, false)),
        [new("c2ac8c4a-5b6e-6c5d-9028-66bf10641ff9")] = ("UnlitTransparentDoubleSided", new(false, Transparent, CullOff, false)),
    };

    private static readonly string[] s_legacySlots = ["_MainTex", "_NormalTex", "_SurfaceTex", "_OcclusionTex", "_EmissionTex", "_ParallaxMap", "_TranslucencyMap", "_AnisoDirectionMap"];

    /// <summary>Runs convert on every material in a tree, saved as an asset or inline. Convert returns whether it changed one.</summary>
    internal static EchoObject? ConvertMaterials(EchoObject echo, Func<RawMaterial, bool> convert)
    {
        bool changed = false;
        ForEachCompound(echo, tag =>
        {
            if (RawMaterial.IsMaterial(tag)) changed |= convert(new RawMaterial(tag));
        });
        return changed ? echo : null;
    }

    /// <summary>
    /// A material of a shader from before Graphite moves onto Lit or Unlit with the same look: the surface the old
    /// shader stood for, no placeholder textures, metallic dropped where no surface map gave it, emission on where an
    /// emission map was set.
    /// </summary>
    internal static bool AdoptLegacyShader(RawMaterial m)
    {
        if (m.Shader is not { } shader || !LegacyShaders.TryGetValue(shader, out var legacy)) return false;
        m.Shader = legacy.Shader.Lit ? LitShader : UnlitShader;
        if (!m.HasSurface) m.SetSurface(legacy.Shader.Type, legacy.Shader.Cull);

        bool hadSurfaceMap = m.Texture("_SurfaceTex") is { } surface && !IsPlaceholder(surface);
        bool hadEmissionMap = m.Texture("_EmissionTex") is { } emission && !IsPlaceholder(emission);
        foreach (string slot in s_legacySlots)
        {
            if (m.Texture(slot) is { } texture && IsPlaceholder(texture)) m.Forget(slot);
        }
        if (!legacy.Shader.Lit) return true;

        if (!hadSurfaceMap) m.Forget("_Metallic");
        if (hadEmissionMap) m.SetOwn("_ints", "_Emission", new EchoObject(1));
        if (!legacy.Shader.Anisotropic) m.Forget("_Anisotropy");
        else if (!m.IsOverridden("_Anisotropy")) m.SetOwn("_floats", "_Anisotropy", new EchoObject(0.5f));
        return true;
    }

    private static bool IsPlaceholder(EchoObject texture)
        => texture.TryGet("$asset", out EchoObject? asset) && asset!.TagType == EchoType.String
            && Guid.TryParse(asset.StringValue, out Guid guid) && PlaceholderTextures.Contains(guid);

    /// <summary>
    /// Lit and Unlit materials move onto Graphite's OpenPBR names, sky exposure becomes intensity, in nits for the
    /// texture skies, grass takes Lit's alpha cutoff and drops its old translucency, terrain drops the holes toggle.
    /// </summary>
    internal static bool AdoptOpenPbr(RawMaterial m)
    {
        Guid? shader = m.Shader;
        if (shader == LitShader || shader == UnlitShader) AdoptOpenPbr(m, shader == LitShader);
        else if (shader == ProceduralSkyShader) m.Move("_Exposure", "intensity");
        else if (shader is { } sky && TextureSkyShaders.Contains(sky)) TextureSkyIntensity(m);
        else if (shader == GrassShader)
        {
            m.Move("_AlphaCutoff", "alpha_cutoff");
            foreach (string name in (string[])["_MainTex", "_Translucency", "_ScatterPower", "_ScatterDistortion", "_ScatterScale"])
                m.Forget(name);
        }
        else if (shader == TerrainShader) m.Forget("_HasHoles");
        return m.Changed;
    }

    /// <summary>
    /// The tint and texture become base_color and its map, the surface texture's G and B the roughness and metalness
    /// maps, then the normal, occlusion and height maps, anisotropy, and emission when it was on with its intensity in nits.
    /// Transparent and cutout Lit materials take their opacity from the tint's alpha. An Unlit material without a
    /// luminance glows at the one that keeps its old look.
    /// </summary>
    private static void AdoptOpenPbr(RawMaterial m, bool lit)
    {
        if (!lit && !m.HasNumber("emission_luminance"))
            m.SetOwn("_floats", "emission_luminance", new EchoObject(LegacyLuminance));
        if (lit) AdoptAnisotropy(m);

        if (!m.Has("_colors", "_MainColor") && !m.Has("_textures", "_MainTex") && !m.Has("_floats", "_Roughness") && !m.Has("_floats", "_Metallic"))
            return;

        float alpha = m.Get("_colors", "_MainColor") is { } tint && tint.TryGet("A", out EchoObject? a) ? a!.FloatValue : 1f;
        m.Move("_MainColor", "base_color");
        m.MoveMap("_MainTex", "base_color_map", -1);
        m.Move("_AlphaCutoff", "alpha_cutoff");
        m.Forget("_InstanceTint");
        if (lit && m.IsSeeThrough)
        {
            m.SetOwn("_floats", "geometry_opacity", new EchoObject(alpha));
            m.CopyMap("base_color_map", "geometry_opacity_map", 3);
        }
        if (!lit) return;

        m.Move("_Metallic", "base_metalness");
        m.Move("_Roughness", "specular_roughness");
        m.CopyMap("_SurfaceTex", "base_metalness_map", 2);
        m.MoveMap("_SurfaceTex", "specular_roughness_map", 1);
        m.MoveMap("_NormalTex", "geometry_normal_map", -1);
        m.Move("_NormalScale", "normal_scale");
        m.MoveMap("_OcclusionTex", "ambient_occlusion_map", 0);
        m.MoveMap("_ParallaxMap", "geometry_height_map", 1);
        m.Move("_Parallax", "height_scale");
        m.Forget("_ParallaxSteps");

        if (m.Number("_Emission") != 0)
        {
            EchoObject emissive = m.Get("_colors", "_EmissiveColor")?.Clone() ?? RawMaterial.White();
            m.SetOwn("_colors", "emission_color", emissive);
            float intensity = m.HasNumber("_EmissionIntensity") ? m.Number("_EmissionIntensity") : 1f;
            m.SetOwn("_floats", "emission_luminance", new EchoObject(intensity * LegacyLuminance));
            m.MoveMap("_EmissionTex", "emission_color_map", -1);
        }
        foreach (string name in (string[])["_Emission", "_EmissiveColor", "_EmissionIntensity", "_EmissionTex", "_EmissionTex_ST", "_EmissionTexUV"])
            m.Forget(name);
    }

    /// <summary>
    /// The old anisotropy a made the tangent's roughness 1 + |a| and the bitangent's 1 - |a| times the roughness, so
    /// specular_roughness_anisotropy takes the same ratio of the two alphas. A negative one stretched along the
    /// bitangent, which a quarter turn of tangent_rotation keeps. The direction map reads RG the same way as
    /// geometry_tangent_map.
    /// </summary>
    private static void AdoptAnisotropy(RawMaterial m)
    {
        if (m.HasNumber("_Anisotropy"))
        {
            float old = Math.Clamp(m.Number("_Anisotropy"), -1f, 1f);
            float ratio = (1f - Math.Abs(old)) / (1f + Math.Abs(old));
            if (old != 0f) m.SetOwn("_floats", "specular_roughness_anisotropy", new EchoObject(1f - ratio * ratio));
            if (old < 0f) m.SetOwn("_floats", "tangent_rotation", new EchoObject(MathF.PI * 0.5f));
            m.Forget("_Anisotropy");
        }
        m.MoveMap("_AnisoDirectionMap", "geometry_tangent_map", -1);
    }

    // Texture skies drew their texels at an exposure of 1, their intensity becomes the nits that keep that look
    private static void TextureSkyIntensity(RawMaterial m)
    {
        if (!m.HasNumber("_Exposure") && m.HasNumber("intensity")) return;
        float exposure = m.HasNumber("_Exposure") ? m.Number("_Exposure") : 1f;
        m.Forget("_Exposure");
        m.SetOwn("_floats", "intensity", new EchoObject(exposure * LegacyLuminance));
    }

    /// <summary>
    /// A material's saved Echo: its _properties dictionaries by kind, its _overrides set, _keywords and _surface. The
    /// dictionaries keep the form they were saved in, the entries list Echo writes or the keyed compound older files have.
    /// </summary>
    internal sealed class RawMaterial
    {
        private static readonly string[] s_kinds = ["_colors", "_vectors2", "_vectors3", "_vectors4", "_floats", "_ints", "_matrices", "_textures", "_textures3D", "_texturesCube"];

        private readonly EchoObject _material;
        private readonly EchoObject _properties;
        private readonly List<EchoObject> _overrides;

        public bool Changed { get; private set; }

        public static bool IsMaterial(EchoObject tag)
        {
            if (!tag.TryGet("_properties", out EchoObject? properties) || properties!.TagType != EchoType.Compound) return false;
            string? type = TypeName(tag);
            return type == MaterialType || (type == null && tag.Contains("_shader"));
        }

        // A material saved before overrides were tracked counts every stored value as set, as the material does on load
        public RawMaterial(EchoObject material)
        {
            _material = material;
            _properties = material["_properties"];
            _overrides = OverrideList();
            if (_overrides.Count == 0)
            {
                foreach (string name in s_kinds.SelectMany(kind => Keys(Dictionary(kind, false))))
                    _overrides.Add(new EchoObject(name));
            }
        }

        public static EchoObject White()
        {
            var color = EchoObject.NewCompound();
            foreach (string channel in new[] { "R", "G", "B", "A" })
                color[channel] = new EchoObject(1f);
            return color;
        }

        public Guid? Shader
        {
            get => _material.TryGet("_shader", out EchoObject? shader) && shader!.TagType == EchoType.Compound
                && shader.TryGet("$asset", out EchoObject? asset) && asset!.TagType == EchoType.String
                && Guid.TryParse(asset.StringValue, out Guid guid) ? guid : null;
            set
            {
                var shader = EchoObject.NewCompound();
                shader["$asset"] = new EchoObject(value!.Value.ToString());
                _material["_shader"] = shader;
                Changed = true;
            }
        }

        // Nullable SurfaceData, saved as the struct, as a compound holding it under "value", or as null
        private EchoObject? Surface
        {
            get
            {
                if (!_material.TryGet("_surface", out EchoObject? surface) || surface!.TagType != EchoType.Compound) return null;
                if (surface.TryGet("value", out EchoObject? inner)) return inner!.TagType == EchoType.Compound ? inner : null;
                return surface;
            }
        }

        public bool HasSurface => Surface != null;

        public void SetSurface(long type, long cull)
        {
            var surface = EchoObject.NewCompound();
            surface["Type"] = new EchoObject(type);
            surface["Blend"] = new EchoObject(0L);
            surface["Cull"] = new EchoObject(cull);
            _material["_surface"] = surface;
            Changed = true;
        }

        // A cutout or transparent surface, or a _SURFACE keyword other than OPAQUE
        public bool IsSeeThrough
        {
            get
            {
                if (Surface is { } surface && surface.TryGet("Type", out EchoObject? type) && IsNumber(type!) && type.LongValue is Cutout or Transparent) return true;
                return _material.TryGet("_keywords", out EchoObject? keywords) && keywords!.TagType == EchoType.Compound
                    && Find(keywords, "_SURFACE") is { TagType: EchoType.String } axis && axis.StringValue != "OPAQUE";
            }
        }

        public bool Has(string kind, string name) => Find(Dictionary(kind, false), name) != null;

        public EchoObject? Get(string kind, string name) => Find(Dictionary(kind, false), name);

        // A texture that is set, its value an asset reference or inline texture
        public EchoObject? Texture(string name) => Get("_textures", name) is { TagType: EchoType.Compound } texture ? texture : null;

        public bool HasNumber(string name) => Has("_floats", name) || Has("_ints", name);

        // A float or int, as older materials store numbers under either kind
        public float Number(string name)
            => Get("_floats", name) is { } value ? value.FloatValue : Get("_ints", name) is { } whole ? whole.IntValue : 0;

        public bool IsOverridden(string name) => _overrides.Any(o => o.TagType == EchoType.String && o.StringValue == name);

        /// <summary>Stores one value under a name, dropping any value of another kind it had, and marks it set.</summary>
        public void SetOwn(string kind, string name, EchoObject value)
        {
            Remove(name);
            Set(Dictionary(kind, true), name, value);
            Override(name);
            Changed = true;
        }

        public void Move(string from, string to)
        {
            if (Get("_colors", from) is { } color) SetOwn("_colors", to, color.Clone());
            else if (HasNumber(from)) SetOwn("_floats", to, new EchoObject(Number(from)));
            else return;
            Forget(from);
        }

        // Copies a map with its scale offset and UV set under a new name, reading channel when it is not -1
        public void CopyMap(string from, string to, int channel)
        {
            if (Texture(from) is not { } texture) return;
            SetOwn("_textures", to, texture.Clone());
            if (Get("_vectors4", from + "_ST") is { } scaleOffset) SetOwn("_vectors4", to + "_ST", scaleOffset.Clone());
            int uvSet = (int)(HasNumber(from + "UV") ? Number(from + "UV") : Get("_ints", from + "_UVSet")?.IntValue ?? 0);
            if (uvSet != 0) SetOwn("_ints", to + "_UVSet", new EchoObject(uvSet));
            if (channel >= 0) SetOwn("_ints", to + "_Channel", new EchoObject(channel));
        }

        public void MoveMap(string from, string to, int channel)
        {
            CopyMap(from, to, channel);
            Forget(from);
            Forget(from + "_ST");
            Forget(from + "UV");
        }

        public void Forget(string name)
        {
            Remove(name);
            if (_overrides.RemoveAll(o => o.TagType == EchoType.String && o.StringValue == name) > 0) Changed = true;
        }

        private void Remove(string name)
        {
            foreach (string kind in s_kinds)
            {
                if (Delete(Dictionary(kind, false), name)) Changed = true;
            }
        }

        private void Override(string name)
        {
            if (!IsOverridden(name)) _overrides.Add(new EchoObject(name));
        }

        private List<EchoObject> OverrideList()
        {
            if (_material.TryGet("_overrides", out EchoObject? overrides))
            {
                if (overrides!.TagType == EchoType.List) return overrides.List;
                if (overrides.TagType == EchoType.Compound && overrides.TryGet("$values", out EchoObject? values) && values!.TagType == EchoType.List) return values.List;
            }
            var created = EchoObject.NewCompound();
            created["$values"] = EchoObject.NewList();
            _material["_overrides"] = created;
            return created["$values"].List;
        }

        private static readonly EchoObject s_empty = EchoObject.NewCompound();

        private EchoObject Dictionary(string kind, bool create)
        {
            if (_properties.TryGet(kind, out EchoObject? dictionary) && dictionary!.TagType == EchoType.Compound) return dictionary;
            if (!create) return s_empty;
            var created = EchoObject.NewCompound();
            created["entries"] = EchoObject.NewList();
            _properties[kind] = created;
            return created;
        }

        private static List<EchoObject>? Entries(EchoObject dictionary)
            => dictionary.TryGet("entries", out EchoObject? list) && list!.TagType == EchoType.List ? list.List : null;

        private static bool IsEntry(EchoObject entry, string key)
            => entry.TagType == EchoType.Compound && entry.TryGet("key", out EchoObject? k) && k!.TagType == EchoType.String && k.StringValue == key;

        private static IEnumerable<string> Keys(EchoObject dictionary)
        {
            if (Entries(dictionary) is { } entries)
                return entries.Where(e => e.TagType == EchoType.Compound && e.TryGet("key", out EchoObject? k) && k!.TagType == EchoType.String).Select(e => e["key"].StringValue);
            return dictionary.GetNames().Where(n => n is not ("$id" or "$type"));
        }

        private static EchoObject? Find(EchoObject dictionary, string key)
        {
            if (Entries(dictionary) is { } entries)
                return entries.FirstOrDefault(e => IsEntry(e, key))?.Get("value");
            return key is "$id" or "$type" ? null : dictionary.Get(key);
        }

        private static bool Delete(EchoObject dictionary, string key)
        {
            if (Entries(dictionary) is { } entries) return entries.RemoveAll(e => IsEntry(e, key)) > 0;
            return key is not ("$id" or "$type") && dictionary.Remove(key);
        }

        private static void Set(EchoObject dictionary, string key, EchoObject value)
        {
            if (Entries(dictionary) is not { } entries)
            {
                dictionary[key] = value;
                return;
            }
            var entry = EchoObject.NewCompound();
            entry["key"] = new EchoObject(key);
            entry["value"] = value;
            entries.Add(entry);
        }
    }
}
