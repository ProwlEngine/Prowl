// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Graphite.Rendering;
using Prowl.Graphite.ShaderDef;

using GShader = Prowl.Graphite.Rendering.Shader;

namespace Prowl.Runtime.Resources;

/// <summary>
/// A shader in Graphite's ShaderDef format: its source, the parsed definition, and the Graphite shader materials draw
/// with, which declares the material's properties and keyword axes. A source that fails to parse leaves the shader
/// without a Graphite shader, and its materials draw with the Invalid shader.
/// </summary>
public sealed class Shader : Asset, ISerializable
{
    private static readonly ConcurrentDictionary<string, string> s_sourcePaths = new(StringComparer.Ordinal);

    private string _source = "";
    private string _sourcePath = "";
    [NotHeld] private GShader? _gpu;
    private string? _error;

    internal Shader() : base("New Shader") { }

    /// <summary>A shader from ShaderDef source. <paramref name="sourcePath"/> is the file includes resolve against.</summary>
    public static Shader FromSource(string source, string sourcePath = "")
    {
        Shader shader = new();
        shader.SetSource(source, sourcePath);
        return shader;
    }

    /// <summary>The ShaderDef source.</summary>
    public string Source { get { EnsureLoaded(); return _source; } }

    /// <summary>The file the source came from, which relative includes resolve against.</summary>
    public string SourcePath { get { EnsureLoaded(); return _sourcePath; } }

    /// <summary>The Graphite shader, null when the source failed to parse.</summary>
    public GShader? GpuShader { get { EnsureLoaded(); return _gpu; } }

    /// <summary>The Graphite shader without loading, for code that runs every frame.</summary>
    internal GShader? GpuIfLoaded => IsLoaded ? _gpu : null;

    /// <summary>Why the source failed to parse, null when it parsed.</summary>
    public string? Error { get { EnsureLoaded(); return _error; } }

    /// <summary>The properties a material of this shader has, in declaration order.</summary>
    public IReadOnlyList<MaterialProperty> Properties { get { EnsureLoaded(); return (IReadOnlyList<MaterialProperty>?)_gpu?.Properties ?? []; } }

    /// <summary>The keyword axes a material sets, each with its values.</summary>
    public IReadOnlyList<VariantSpace> Keywords { get { EnsureLoaded(); return (IReadOnlyList<VariantSpace>?)_gpu?.MaterialAxes ?? []; } }

    /// <summary>The render queue its materials draw in by default, 2500 and up transparent.</summary>
    public int RenderQueue { get { EnsureLoaded(); return _gpu?.RenderQueue ?? GShader.DefaultRenderQueue; } }

    /// <summary>Whether the shader has a pass of this type, such as ShadowCaster.</summary>
    public bool HasPass(PassType type) { EnsureLoaded(); return _gpu?.HasPass(type) ?? false; }

    public MaterialProperty? FindProperty(string name)
    {
        EnsureLoaded();
        if (_gpu == null) return null;
        int index = _gpu.FindProperty(name);
        return index < 0 ? null : _gpu.Properties[index];
    }

    /// <summary>A shader of Graphite's library, such as "Graphite/Standard/Lit.shader", sharing the renderer's instance.</summary>
    internal static Shader FromLibrary(string libraryPath)
    {
        Shader shader = new();
        shader.SetSource(LibrarySource(libraryPath), libraryPath, Graphics.LibraryShader(libraryPath));
        return shader;
    }

    /// <summary>Replaces the source and parses it again. Materials of the shader pick the new one up.</summary>
    public void SetSource(string source, string sourcePath = "") => SetSource(source, sourcePath, null);

    private void SetSource(string source, string sourcePath, GShader? built)
    {
        _source = source ?? "";
        _sourcePath = sourcePath ?? "";
        _error = null;
        _gpu = null;
        try
        {
            ShaderDefinition definition = ShaderParser.Parse(_source);
            if (definition.Name is { Length: > 0 } declared)
            {
                Name = declared;
                if (_sourcePath.Length > 0) s_sourcePaths[declared] = _sourcePath;
            }
            _gpu = built ?? Graphics.Renderer?.LoadShader(definition) ?? new GShader(definition, Graphics.Compiler);
        }
        catch (Exception e)
        {
            _error = e.Message;
            Debug.LogError($"Shader '{(_sourcePath.Length > 0 ? _sourcePath : Name)}' failed to parse: {e.Message}");
        }
    }

    private static readonly System.Text.RegularExpressions.Regex s_include = new(@"^[ \t]*#include[ \t]+""([^""]+)""[ \t]*\r?$", System.Text.RegularExpressions.RegexOptions.Multiline);

    /// <summary>
    /// Inlines the includes of a source that are neither Graphite's nor Prowl's library, each once, through
    /// <paramref name="resolve"/>. Library includes stay for the compiler. An include it cannot find stays as written.
    /// </summary>
    public static string ExpandIncludes(string source, Func<string, string?> resolve)
    {
        HashSet<string> seen = new(StringComparer.Ordinal);
        string Expand(string text, int depth) => s_include.Replace(text, match =>
        {
            string name = match.Groups[1].Value;
            if (name.StartsWith("Graphite/", StringComparison.Ordinal) || name.StartsWith(BuiltInShaders.Prefix, StringComparison.Ordinal) || depth > 16)
                return match.Value;
            if (!seen.Add(name)) return "";
            string? included = resolve(name);
            return included == null ? match.Value : Expand(included, depth + 1);
        });
        return Expand(source, 0);
    }

    /// <summary>The source file of a shader by the name it declares, for includes relative to it.</summary>
    internal static string? SourcePathOf(string shaderName) => s_sourcePaths.TryGetValue(shaderName, out string? path) ? path : null;

    /// <summary>Loads a shader from a ShaderDef file.</summary>
    public static Shader LoadFromFile(string filePath)
    {
        if (!System.IO.File.Exists(filePath))
            throw new System.IO.FileNotFoundException($"Shader file not found: {filePath}");
        return FromSource(System.IO.File.ReadAllText(filePath), filePath);
    }

    /// <summary>The shared instance of a built in shader.</summary>
    public static Shader LoadDefault(DefaultShader shader) => BuiltInAssets.Load<Shader>(BuiltInAssets.GuidFor(shader));

    /// <summary>The built-in shader without loading it, for code that runs while another asset loads.</summary>
    internal static Shader GetDefault(DefaultShader shader) => AssetDatabase.Get<Shader>(BuiltInAssets.GuidFor(shader))!;

    /// <summary>Parses a built in shader, called by <see cref="BuiltInAssets"/> on its first use.</summary>
    internal static Shader ParseDefault(DefaultShader shader)
    {
        if (LibraryPathOf(shader) is { } library)
            return FromLibrary(library);
        string path = $"Assets/Defaults/Shaders/{shader}.shader";
        return FromSource(EmbeddedResources.ReadAllText(path), path);
    }

    /// <summary>Raw source text of a built in shader.</summary>
    public static string GetDefaultSource(DefaultShader shader)
        => LibraryPathOf(shader) is { } library ? LibrarySource(library) : EmbeddedResources.ReadAllText($"Assets/Defaults/Shaders/{shader}.shader");

    // The compiler registers Graphite's library on first use
    private static string LibrarySource(string libraryPath)
    {
        _ = Graphics.Compiler;
        return ShaderLibrary.Get(libraryPath) ?? throw new ArgumentException($"Graphite's library has no shader {libraryPath}.");
    }

    private static string? LibraryPathOf(DefaultShader shader) => shader switch
    {
        DefaultShader.Lit => "Graphite/Standard/Lit.shader",
        DefaultShader.Unlit => "Graphite/Standard/Unlit.shader",
        DefaultShader.ProceduralSky => "Graphite/Skies/Procedural.shader",
        DefaultShader.GradientSky => "Graphite/Skies/Gradient.shader",
        DefaultShader.CubemapSky => "Graphite/Skies/Cubemap.shader",
        DefaultShader.PanoramaSky => "Graphite/Skies/Panorama.shader",
        DefaultShader.SixSidedSky => "Graphite/Skies/SixSided.shader",
        _ => null,
    };

    /// <summary>
    /// The shaders of the standard family before Graphite, by the GUID materials stored, and what replaces each: Lit or
    /// Unlit with the surface the old file stood for.
    /// </summary>
    internal static readonly IReadOnlyDictionary<Guid, (DefaultShader Shader, SurfaceSettings Surface, bool Anisotropic)> Legacy = BuildLegacy();

    private static Dictionary<Guid, (DefaultShader, SurfaceSettings, bool)> BuildLegacy()
    {
        Dictionary<Guid, (DefaultShader, SurfaceSettings, bool)> legacy = [];
        foreach (string family in new[] { "Standard", "StandardAnisotropic", "Unlit" })
        {
            foreach ((string mode, SurfaceType type) in new[] { ("", SurfaceType.Opaque), ("Cutout", SurfaceType.Cutout), ("Transparent", SurfaceType.Transparent) })
            {
                foreach ((string sides, SurfaceCull cull) in new[] { ("", SurfaceCull.Back), ("DoubleSided", SurfaceCull.Off) })
                {
                    string name = family + mode + sides;
                    if (name == "Unlit") continue;
                    legacy[BuiltInAssets.DeterministicGuid($"$Default:Shader/{name}")] =
                        (family == "Unlit" ? DefaultShader.Unlit : DefaultShader.Lit, new SurfaceSettings(type, SurfaceBlend.Alpha, cull), family == "StandardAnisotropic");
                }
            }
        }
        return legacy;
    }

    /// <summary>
    /// Pulls the declared name out of the <c>Shader "Some/Path"</c> line at the top of a shader source, without parsing
    /// it. Leading comments and blank lines are skipped. Null when no declaration is found.
    /// </summary>
    public static string? ReadDeclaredPath(string source)
    {
        if (string.IsNullOrEmpty(source)) return null;

        int i = 0;
        const char byteOrderMark = (char)0xFEFF;
        while (i < source.Length && source[i] == byteOrderMark) i++;

        while (i < source.Length)
        {
            while (i < source.Length && char.IsWhiteSpace(source[i])) i++;
            if (i + 1 < source.Length && source[i] == '/' && source[i + 1] == '/')
            {
                while (i < source.Length && source[i] != '\n') i++;
                continue;
            }
            if (i + 1 < source.Length && source[i] == '/' && source[i + 1] == '*')
            {
                int end = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (end < 0) return null;
                i = end + 2;
                continue;
            }
            break;
        }

        const string keyword = "Shader";
        if (i + keyword.Length >= source.Length) return null;
        if (string.CompareOrdinal(source, i, keyword, 0, keyword.Length) != 0) return null;
        i += keyword.Length;

        while (i < source.Length && char.IsWhiteSpace(source[i])) i++;
        if (i >= source.Length || source[i] != '"') return null;

        int start = ++i;
        while (i < source.Length && source[i] != '"') i++;
        return i < source.Length ? source[start..i] : null;
    }

    public void Serialize(ref EchoObject compoundTag, SerializationContext ctx)
    {
        SerializeHeader(compoundTag);
        compoundTag.Add("Source", new(_source));
        compoundTag.Add("SourcePath", new(_sourcePath));
    }

    public void Deserialize(EchoObject value, SerializationContext ctx)
    {
        DeserializeHeader(value);
        SetSource(value.Get("Source")?.StringValue ?? "", value.Get("SourcePath")?.StringValue ?? "");
    }

    protected override void OnUnload() => _gpu = null;
}
