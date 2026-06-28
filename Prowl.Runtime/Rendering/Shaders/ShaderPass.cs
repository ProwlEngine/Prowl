// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Graphite;
using Prowl.Graphite.Variants;

namespace Prowl.Runtime.Rendering.Shaders;

public sealed class ShaderPass
{
    [SerializeField] private string _name;

    [SerializeField] private Dictionary<string, string> _tags;

    /// <summary>
    /// The compiled variants of this pass: every keyword permutation paired with its per-backend
    /// program descriptions. This is the serialized source of truth the runtime
    /// <see cref="VariantSet{T}"/> is reconstructed from it.
    /// </summary>
    [SerializeField] private ShaderVariant[] _variants;

    [SerializeIgnore] private VariantSet<GraphicsProgram> _variantSet;


    /// <summary>
    /// The name to identify this <see cref="ShaderPass"/>
    /// </summary>
    public string Name => _name;

    /// <summary>
    /// The tags to identify this <see cref="ShaderPass"/>
    /// </summary>
    public IEnumerable<KeyValuePair<string, string>> Tags => _tags;
    /// <summary>
    /// The compiled variants this pass was built with.
    /// </summary>
    public IEnumerable<ShaderVariant> Variants => _variants;

    /// <summary>
    /// The program for the keyword state currently selected via <see cref="SetKeyword"/> /
    /// <see cref="SetKeywords"/>.
    /// </summary>
    public GraphicsProgram ActiveProgram => VariantSet.ActiveVariant;


    private ShaderPass() { }

    public ShaderPass(string name, Dictionary<string, string>? tags, ShaderVariant[] variants)
    {
        _name = name;
        _tags = tags ?? [];
        _variants = variants ?? [];
    }

    /// <summary>
    /// Sets a keyword on the pass's current state, updating <see cref="ActiveProgram"/> to the
    /// closest compiled variant. Returns <c>false</c> if the keyword is not part of this pass.
    /// </summary>
    public bool SetKeyword(Keyword keyword) => VariantSet.SetKeyword(keyword);

    /// <summary>
    /// Sets a batch of keywords on the pass's current state, updating <see cref="ActiveProgram"/> to
    /// the closest compiled variant. Returns <c>false</c> if any keyword is not part of this pass.
    /// </summary>
    public bool SetKeywords(params Keyword[] keywords) => VariantSet.SetKeywords(keywords);

    private VariantSet<GraphicsProgram> VariantSet => _variantSet ??= BuildVariantSet();

    private VariantSet<GraphicsProgram> BuildVariantSet()
    {
        GraphicsBackend backend = Graphics.Device.BackendType;

        var programs = new GraphicsProgram[_variants.Length];
        var keywords = new Keyword[_variants.Length][];

        for (int i = 0; i < _variants.Length; i++)
        {
            ShaderDescription description = PickBackend(_variants[i], backend);

            programs[i] = Graphics.Device.ResourceFactory.CreateGraphicsProgram(description);
            keywords[i] = _variants[i].Keywords;
        }

        return new VariantSet<GraphicsProgram>(programs, keywords);
    }

    private ShaderDescription PickBackend(ShaderVariant variant, GraphicsBackend backend)
    {
        foreach ((ShaderDescription description, GraphicsBackend entryBackend) in variant.Backends)
        {
            if (entryBackend == backend)
                return description;
        }

        throw new NotSupportedException($"Shader pass '{_name}' was not compiled for backend {backend}.");
    }

    public bool HasTag(string tag, string? tagValue = null)
    {
        if (_tags.TryGetValue(tag, out string value))
            return tagValue == null || value == tagValue;

        return false;
    }
}
