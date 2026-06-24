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
    [SerializeField] private Dictionary<string, int> _tagSortOffsets;

    [SerializeField] private string _grabTextureName; // If not empty, captures screen before rendering
    [SerializeField] private string _grabDepthTextureName; // If not empty, also captures the depth buffer

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
    /// The sort offsets for tags (e.g., "Transparent+1000" has offset 1000)
    /// </summary>
    public IReadOnlyDictionary<string, int> TagSortOffsets => _tagSortOffsets;

    /// <summary>
    /// The name of the texture uniform to bind the grabbed colour texture to. Empty if this pass doesn't grab.
    /// </summary>
    public string GrabTextureName => _grabTextureName;

    /// <summary>
    /// The name of the texture uniform to bind the grabbed depth texture to. Empty if depth isn't grabbed.
    /// </summary>
    public string GrabDepthTextureName => _grabDepthTextureName;

    /// <summary>
    /// Whether this pass captures the screen colour before rendering
    /// </summary>
    public bool HasGrabTexture => !string.IsNullOrEmpty(_grabTextureName);

    /// <summary>
    /// Whether this pass also captures the depth buffer alongside colour. Implies <see cref="HasGrabTexture"/>.
    /// </summary>
    public bool HasGrabDepth => !string.IsNullOrEmpty(_grabDepthTextureName);

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

    public ShaderPass(string name, Dictionary<string, string>? tags, Dictionary<string, int>? tagSortOffsets, ShaderVariant[] variants, string grabTextureName = "", string grabDepthTextureName = "")
    {
        _name = name;

        _tags = tags ?? [];
        _tagSortOffsets = tagSortOffsets ?? [];

        _variants = variants ?? [];

        _grabTextureName = grabTextureName;
        _grabDepthTextureName = grabDepthTextureName;
    }

    /// <summary>
    /// Sets a keyword on the pass's current state, updating <see cref="ActiveProgram"/>.
    /// </summary>
    public void SetKeyword(Keyword keyword) => VariantSet.SetKeyword(keyword);

    /// <summary>
    /// Sets a batch of keywords on the pass's current state, updating <see cref="ActiveProgram"/>.
    /// </summary>
    public void SetKeywords(params Keyword[] keywords) => VariantSet.SetKeywords(keywords);

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
        foreach (ShaderVariantBackend entry in variant.Backends)
        {
            if (entry.Backend == backend)
                return entry.Description;
        }

        throw new NotSupportedException($"Shader pass '{_name}' was not compiled for backend {backend}.");
    }

    public bool HasTag(string tag, string? tagValue = null)
    {
        if (_tags.TryGetValue(tag, out string value))
            return tagValue == null || value == tagValue;

        return false;
    }

    /// <summary>
    /// Gets the sort offset for a given tag, or 0 if no offset is specified
    /// </summary>
    public int GetTagSortOffset(string tag)
    {
        return _tagSortOffsets.TryGetValue(tag, out int offset) ? offset : 0;
    }
}
