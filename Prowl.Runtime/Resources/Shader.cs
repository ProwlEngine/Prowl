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
    /// already bound. No compiler is attached: this is the shipped-runtime path, playing back whatever
    /// variants were baked ahead of time.</summary>
    private void EnsureCreated()
    {
        if (_definition.IsCreated)
            return;

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
    /// Resolves a default shader from the asset database by its deterministic GUID. Shaders are
    /// compiled by the editor build pipeline into the asset database there is no runtime parser,
    /// so this returns null until the compiled default has been registered.
    /// </summary>
    public static Shader? LoadDefault(DefaultShader shader)
        => AssetDatabase.Get(BuiltInAssets.GuidFor(shader)) as Shader;

    public void OnBeforeSerialize() { }

    public void OnAfterDeserialize() { }
}
