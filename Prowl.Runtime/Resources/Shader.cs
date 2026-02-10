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
public sealed class Shader : EngineObject, ISerializationCallbackReceiver
{
    [SerializeField]
    private ShaderProperty[] _properties;
    public IEnumerable<ShaderProperty> Properties => _properties;


    [SerializeField]
    private ShaderPass[] _passes;
    public IEnumerable<ShaderPass> Passes => _passes;


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
        passIndex = Maths.Clamp(passIndex, 0, _passes.Length - 1);
        return _passes[passIndex];
    }

    public ShaderPass GetPass(string passName)
    {
        return _passes[GetPassIndex(passName)];
    }

    public int GetPassIndex(string passName)
    {
        return _nameIndexLookup.GetValueOrDefault(passName, -1);
    }

    public int? GetPassWithTag(string tag, string? tagValue = null)
    {
        List<int> passes = GetPassesWithTag(tag, tagValue);
        return passes.Count > 0 ? passes[0] : null;
    }

    public List<int> GetPassesWithTag(string tag, string? tagValue = null)
    {
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

        shader.AssetPath = filePath;
        return shader;
    }

    /// <summary>
    /// Loads a default embedded shader
    /// </summary>
    public static Shader LoadDefault(DefaultShader shader)
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

        result.AssetPath = $"$Default:{shader}";
        return result;
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
}
