// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Prowl.Echo;
using Prowl.Graphite.ShaderDef;
using Prowl.Vector;

using ShaderPass = Prowl.Graphite.ShaderDef.ShaderPass;
using ShaderProperty = Prowl.Runtime.Rendering.Shaders.ShaderProperty;

namespace Prowl.Runtime.Resources;

/// <summary>
/// The Shader class itself doesnt do much, It stores the properties of the shader and the shader code and Keywords.
/// This is used in conjunction with the Material class to create shader variants with the correct keywords and to render things
/// </summary>
public sealed class Shader : EngineObject, ISerializationCallbackReceiver
{
    /// <summary>Resolved material-facing default values (Range hints, actual default Texture2D/Texture3D
    /// instances). Converted once from <see cref="ShaderDefinition.Properties"/> at import time,
    /// since the ShaderDef library only knows string-named texture defaults.</summary>
    [SerializeField]
    private ShaderProperty[] _properties;
    public IEnumerable<ShaderProperty> Properties { get { EnsureNotDisposed(); return _properties; } }

    [SerializeField]
    private ShaderDefinition _definition;

    [SerializeField]
    private ShaderSnapshot _snapshot;

    public IEnumerable<ShaderPass> Passes { get { EnsureNotDisposed(); EnsureCreated(); return _definition.Passes ?? []; } }

    /// <summary>Set by the editor (Prowl.Editor's CompilationWorker) so a shader bound from a cached
    /// snapshot can still compile a missing variant on demand. Never set outside the editor - builds
    /// carry no Slang compiler, so <see cref="EnsureCreated"/> only reads these when
    /// <see cref="Application.IsEditor"/> is true.</summary>
    public static IShaderCompiler? EditorCompiler;

    /// <summary>Lazily produces the fallback <see cref="Variant"/> every pass falls back to when it
    /// can't resolve its own (required by Graphite whenever a compiler is attached). A provider rather
    /// than a plain field since computing it needs a live <see cref="GraphicsDevice"/>, which doesn't
    /// exist yet when the editor registers this hook.</summary>
    public static Func<Variant>? EditorFallbackProvider;


    internal Shader() : base("New Shader") { }

    /// <summary>
    /// Wraps an already-parsed <see cref="ShaderDefinition"/> plus its baked variant snapshot.
    /// The definition is only re-bound to a device lazily, on first pass access.
    /// </summary>
    public Shader(string name, ShaderProperty[] properties, ShaderDefinition definition, ShaderSnapshot snapshot) : base(name)
    {
        _properties = properties;
        _definition = definition;
        _snapshot = snapshot;
    }

    /// <summary>Binds <see cref="_definition"/> to the current device from the baked snapshot, if not
    /// already bound. In the editor, <see cref="EditorCompiler"/> is attached so a variant missing from
    /// the snapshot can still be compiled the first time it's requested; a shipped build attaches no
    /// compiler and only plays back whatever variants were baked ahead of time.</summary>
    private void EnsureCreated()
    {
        if (_definition.IsCreated)
            return;

        if (Application.IsEditor && EditorCompiler != null && EditorFallbackProvider != null)
            _definition.Create(Graphics.Device, _snapshot, EditorCompiler, EditorFallbackProvider());
        else
            _definition.Create(Graphics.Device, _snapshot);
    }

    public ShaderPass GetPass(int passIndex)
    {
        EnsureNotDisposed();
        EnsureCreated();
        ShaderPass[] passes = _definition.Passes!;
        passIndex = Maths.Clamp(passIndex, 0, passes.Length - 1);
        return passes[passIndex];
    }

    /// <summary>The variants baked for pass <paramref name="passIndex"/> (inspector/diagnostic use -
    /// draw-time variant selection goes through <see cref="GetPass(int)"/> instead).</summary>
    public IReadOnlyList<Variant> GetCompiledVariants(int passIndex)
    {
        EnsureNotDisposed();
        PassSnapshot[] passes = _snapshot.Passes ?? [];
        if (passIndex < 0 || passIndex >= passes.Length)
            return [];
        return passes[passIndex].Variants ?? [];
    }

    public ShaderPass GetPass(string passName)
    {
        EnsureNotDisposed();
        EnsureCreated();
        return _definition.GetPass(passName);
    }

    public int GetPassIndex(string passName)
    {
        EnsureNotDisposed();
        return _definition.GetPassIndex(passName);
    }

    /// <summary>True if <paramref name="pass"/> carries <paramref name="tag"/>, optionally matching a
    /// specific value.</summary>
    public static bool PassHasTag(ShaderPass pass, string tag, string? tagValue = null)
        => ShaderDefinition.PassHasTag(pass, tag, tagValue);

    public int? GetPassWithTag(string tag, string? tagValue = null)
    {
        EnsureNotDisposed();
        EnsureCreated();
        return _definition.GetPassWithTag(tag, tagValue);
    }

    public List<int> GetPassesWithTag(string tag, string? tagValue = null)
    {
        EnsureNotDisposed();
        EnsureCreated();
        return _definition.GetPassesWithTag(tag, tagValue);
    }

    /// <summary>
    /// Load a default embedded shader. Pulls the shared instance from <see cref="BuiltInAssets"/> if
    /// initialized, otherwise falls back to a direct parse.
    /// </summary>
    public static Shader? LoadDefault(DefaultShader shader)
    {
        if (BuiltInAssets.Get(BuiltInAssets.GuidFor(shader)) is Shader cached)
            return cached;

        return ParseDefault(shader);
    }

    /// <summary>
    /// Raw load of a precompiled default shader blob invoked by <see cref="BuiltInAssets"/> on first
    /// cache miss. Public callers should use <see cref="LoadDefault"/>. Returns null if the shader has
    /// no compiled blob yet (most <see cref="DefaultShader"/> entries are still placeholders).
    /// </summary>
    internal static Shader? ParseDefault(DefaultShader shader)
    {
        string resourcePath = $"Assets/Defaults/Compiled/{shader}.shaderblob";
        if (!EmbeddedResources.Exists(resourcePath))
            return null;

        using Stream stream = EmbeddedResources.GetStream(resourcePath);
        using var reader = new BinaryReader(stream);
        EchoObject root = EchoObject.ReadFromBinary(reader);
        DefaultShaderBlobData blob = Serializer.Deserialize<DefaultShaderBlobData>(root);

        ShaderProperty[] properties = [.. (blob.Definition.Properties ?? []).Select(Rendering.Shaders.ShaderPropertyConverter.Convert)];

        return new Shader(blob.Definition.Name ?? shader.ToString(), properties, blob.Definition, blob.Snapshot);
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

    public void OnBeforeSerialize() { }

    public void OnAfterDeserialize() { }
}
