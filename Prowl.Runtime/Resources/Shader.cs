// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Runtime.Rendering.Shaders;
using Prowl.Vector;

namespace Prowl.Runtime.Resources;

/// <summary>
/// The Shader class itself doesnt do much, It stores the properties of the shader and the shader code and Keywords.
/// This is used in conjunction with the Material class to create shader variants with the correct keywords and to render things
/// </summary>
public sealed class Shader : Asset, ISerializationCallbackReceiver
{
    [SerializeField]
    private ShaderProperty[] _properties = [];
    public IEnumerable<ShaderProperty> Properties { get { EnsureLoaded(); return _properties; } }

    /// <summary>The properties without loading, empty once unloaded, for the render thread.</summary>
    internal ShaderProperty[] LoadedProperties => _properties ?? [];


    [SerializeField]
    private ShaderPass[] _passes = [];
    public IEnumerable<ShaderPass> Passes { get { EnsureLoaded(); return _passes; } }

    /// <summary>Loads the default textures and creates their handles, so the render thread only binds what exists.</summary>
    internal void PrepareDefaultTextures()
    {
        EnsureLoaded();
        foreach (ShaderProperty property in _properties)
        {
            if (property.PropertyType == ShaderPropertyType.Texture2D && property.Texture2DValue is { IsDisposed: false, HandleIfLoaded: null } texture2D)
                _ = texture2D.Handle;
            else if (property.PropertyType == ShaderPropertyType.Texture3D && property.Texture3DValue is { IsDisposed: false, HandleIfLoaded: null } texture3D)
                _ = texture3D.Handle;
        }
    }


    private Dictionary<string, int> _nameIndexLookup = [];
    private Dictionary<string, List<int>> _tagIndexLookup = [];


    internal Shader() : base("New Shader") { }

    public Shader(string name, ShaderProperty[] properties, ShaderPass[] passes) : base(name)
    {
        _properties = properties;
        _passes = passes;

        OnAfterDeserialize();
    }

    private void RegisterPass(ShaderPass pass, int index)
    {
        if (!string.IsNullOrWhiteSpace(pass.Name))
        {
            if (!_nameIndexLookup.TryAdd(pass.Name, index))
                throw new InvalidOperationException($"Pass with name {pass.Name} conflicts with existing pass at index {_nameIndexLookup[pass.Name]}. Ensure no two passes have equal names.");
        }

        foreach (KeyValuePair<string, string> pair in pass.Tags)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
                continue;

            if (!_tagIndexLookup.TryGetValue(pair.Key, out _))
                _tagIndexLookup.Add(pair.Key, []);

            _tagIndexLookup[pair.Key].Add(index);
        }
    }

    public ShaderPass GetPass(int passIndex)
    {
        EnsureLoaded();
        passIndex = Maths.Clamp(passIndex, 0, _passes.Length - 1);
        return _passes[passIndex];
    }

    public ShaderPass GetPass(string passName)
    {
        EnsureLoaded();
        return _passes[GetPassIndex(passName)];
    }

    public int GetPassIndex(string passName)
    {
        EnsureLoaded();
        return _nameIndexLookup.GetValueOrDefault(passName, -1);
    }

    public int? GetPassWithTag(string tag, string? tagValue = null)
    {
        EnsureLoaded();
        List<int> passes = GetPassesWithTag(tag, tagValue);
        return passes.Count > 0 ? passes[0] : null;
    }

    public List<int> GetPassesWithTag(string tag, string? tagValue = null)
    {
        EnsureLoaded();
        List<int> passes = [];

        if (_tagIndexLookup.TryGetValue(tag, out List<int> passesWithTag))
        {
            foreach (int index in passesWithTag)
            {
                ShaderPass pass = _passes[index];

                if (pass.HasTag(tag, tagValue))
                    passes.Add(index);
            }
        }

        return passes;
    }

    /// <summary>
    /// Loads a shader from a file path
    /// </summary>
    public static Shader LoadFromFile(string filePath)
    {
        if (!System.IO.File.Exists(filePath))
            throw new System.IO.FileNotFoundException($"Shader file not found: {filePath}");

        string shaderCode = System.IO.File.ReadAllText(filePath);

        if (!AssetImporting.ShaderParser.ParseShader(filePath, shaderCode, path =>
        {
            // Include resolver for #include directives
            string? absolutePath = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(filePath)!, path));
            if (System.IO.File.Exists(absolutePath))
                return System.IO.File.ReadAllText(absolutePath);

            // Then try embedded resources (for default includes like VertexAttributes, Fragment, etc.)
            try
            {
                return EmbeddedResources.ReadAllText(path);
            }
            catch
            {
                // Also try with Assets/Defaults/ prefix
                try
                {
                    return EmbeddedResources.ReadAllText($"Assets/Defaults/{path}");
                }
                catch
                {
                    return null;
                }
            }
        }, out Shader? shader))
        {
            throw new System.Exception($"Failed to parse shader: {filePath}");
        }

        if (shader.IsNotValid())
            throw new System.Exception($"Shader parsing returned null: {filePath}");

        return shader;
    }

    /// <summary>
    /// Get the shared instance of a default embedded shader. Returns the same instance
    /// across the whole app so ShaderPass variant caches aren't defeated by repeated
    /// re-parsing the parse happens exactly once per shader enum value.
    /// </summary>
    public static Shader LoadDefault(DefaultShader shader) => BuiltInAssets.Load<Shader>(BuiltInAssets.GuidFor(shader));

    /// <summary>The built-in shader without loading it, for code that runs while another asset loads.</summary>
    internal static Shader GetDefault(DefaultShader shader) => AssetDatabase.Get<Shader>(BuiltInAssets.GuidFor(shader))!;

    /// <summary>
    /// Raw parse of a default embedded shader invoked by <see cref="BuiltInAssets"/>
    /// on the first cache miss. Public callers should use <see cref="LoadDefault"/>.
    /// </summary>
    internal static Shader ParseDefault(DefaultShader shader)
    {
        string fileName = shader.ToString();

        string resourcePath = $"Assets/Defaults/{fileName}.shader";
        string shaderCode = EmbeddedResources.ReadAllText(resourcePath);

        if (!AssetImporting.ShaderParser.ParseShader(resourcePath, shaderCode, path =>
        {
            // Include resolver for embedded resources
            try
            {
                return EmbeddedResources.ReadAllText(path);
            }
            catch
            {
                return null;
            }
        }, out Shader? result))
        {
            throw new System.Exception($"Failed to parse default shader: {shader}");
        }

        if (result.IsNotValid())
            throw new System.Exception($"Default shader parsing returned null: {shader}");

        return result;
    }

    /// <summary>
    /// Raw source text of an embedded default shader. Tooling that only needs the declared
    /// <c>Shader "path"</c> can read it from here without paying for a full parse.
    /// </summary>
    public static string GetDefaultSource(DefaultShader shader) =>
        EmbeddedResources.ReadAllText($"Assets/Defaults/{shader}.shader");

    /// <summary>
    /// Pulls the declared path out of the <c>Shader "Some/Path"</c> line at the top of a shader
    /// source, without running the parser. Leading comments and blank lines are skipped, so this
    /// works on files that open with a header comment. Returns <c>null</c> when no declaration is
    /// found in the first few lines.
    /// </summary>
    public static string? ReadDeclaredPath(string source)
    {
        if (string.IsNullOrEmpty(source)) return null;

        int i = 0;
        const char byteOrderMark = (char)0xFEFF; // shader files are often BOM-prefixed
        while (i < source.Length && source[i] == byteOrderMark) i++;

        while (i < source.Length)
        {
            // Skip whitespace and any line comment ahead of the declaration.
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

    /// <summary>
    /// Loads a default shader include file (for use by shader parser)
    /// </summary>
    internal static string LoadDefaultInclude(DefaultShaderInclude include)
    {
        string fileName = include.ToString();

        return EmbeddedResources.ReadAllText($"Assets/Defaults/{fileName}.glsl");
    }

    public void OnBeforeSerialize() { }

    public void OnAfterDeserialize()
    {
        for (int i = 0; i < _passes.Length; i++)
            RegisterPass(_passes[i], i);
    }

    protected override void OnUnload()
    {
        foreach (var pass in _passes ?? [])
            pass.Dispose();
    }

}
