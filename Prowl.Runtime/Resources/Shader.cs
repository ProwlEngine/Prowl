// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

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


    private Dictionary<string, int> _nameIndexLookup = [];
    private Dictionary<string, List<int>> _tagIndexLookup = [];


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

        OnAfterDeserialize();
    }

    /// <summary>Binds <see cref="_definition"/> to the current device from the baked snapshot, if not
    /// already bound. No compiler is attached: this is the shipped-runtime path, playing back whatever
    /// variants were baked ahead of time.</summary>
    private void EnsureCreated()
    {
        if (_definition.IsCreated)
            return;

        _definition.Create(Graphics.Device, _snapshot);
    }

    private void RegisterPass(ShaderPass pass, int index)
    {
        if (!string.IsNullOrWhiteSpace(pass.Name))
        {
            if (!_nameIndexLookup.TryAdd(pass.Name, index))
                throw new InvalidOperationException($"Pass with name {pass.Name} conflicts with existing pass at index {_nameIndexLookup[pass.Name]}. Ensure no two passes have equal names.");
        }

        if (pass.Tags == null)
            return;

        foreach (KeyValuePair<string, string> pair in pass.Tags)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
                continue;

            if (!_tagIndexLookup.TryGetValue(pair.Key, out _))
                _tagIndexLookup.Add(pair.Key, []);

            _tagIndexLookup[pair.Key].Add(index);
        }
    }

    /// <summary>True if <paramref name="pass"/> carries <paramref name="tag"/>, optionally matching a
    /// specific value. <see cref="ShaderPass"/> has no such helper itself, Prowl only needs it for
    /// pass lookup by tag.</summary>
    public static bool PassHasTag(ShaderPass pass, string tag, string? tagValue = null)
    {
        if (pass.Tags != null && pass.Tags.TryGetValue(tag, out string value))
            return tagValue == null || value == tagValue;

        return false;
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
        return _definition.Passes![GetPassIndex(passName)];
    }

    public int GetPassIndex(string passName)
    {
        EnsureNotDisposed();
        return _nameIndexLookup.GetValueOrDefault(passName, -1);
    }

    public int? GetPassWithTag(string tag, string? tagValue = null)
    {
        EnsureNotDisposed();
        List<int> passes = GetPassesWithTag(tag, tagValue);
        return passes.Count > 0 ? passes[0] : null;
    }

    public List<int> GetPassesWithTag(string tag, string? tagValue = null)
    {
        EnsureNotDisposed();
        EnsureCreated();
        List<int> passes = [];

        if (_tagIndexLookup.TryGetValue(tag, out List<int> passesWithTag))
        {
            ShaderPass[] all = _definition.Passes!;

            foreach (int index in passesWithTag)
            {
                if (PassHasTag(all[index], tag, tagValue))
                    passes.Add(index);
            }
        }

        return passes;
    }

    /// <summary>
    /// Resolves a default shader from the asset database by its deterministic GUID. Shaders are
    /// compiled by the editor build pipeline into the asset database there is no runtime parser,
    /// so this returns null until the compiled default has been registered.
    /// </summary>
    public static Shader? LoadDefault(DefaultShader shader)
        => AssetDatabase.Get(BuiltInAssets.GuidFor(shader)) as Shader;

    public void OnBeforeSerialize() { }

    public void OnAfterDeserialize()
    {
        _nameIndexLookup = [];
        _tagIndexLookup = [];

        ShaderPass[] passes = _definition.Passes ?? [];
        for (int i = 0; i < passes.Length; i++)
            RegisterPass(passes[i], i);
    }
}
