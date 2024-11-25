// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Veldrid;
using Prowl.Echo;

namespace Prowl.Runtime.Rendering;


public sealed class ComputeKernel : ISerializationCallbackReceiver
{
    [SerializeField, HideInInspector]
    private readonly string _name;

    [NonSerialized]
    private Dictionary<string, HashSet<string>> _keywords;

    [NonSerialized]
    private Dictionary<KeywordState, ComputeVariant> _variants;


    /// <summary>
    /// The name to identify this <see cref="ShaderPass"/>
    /// </summary>
    public string Name => _name;

    public IEnumerable<KeyValuePair<string, HashSet<string>>> Keywords => _keywords;
    public IEnumerable<KeyValuePair<KeywordState, ComputeVariant>> Variants => _variants;


    private ComputeKernel() { }

    public ComputeKernel(string name, Dictionary<string, HashSet<string>>? keywords, ComputeVariant[] variants)
    {
        _name = name;

        _keywords = keywords ?? new() { { string.Empty, [string.Empty] } };

        _variants = new();

        foreach (var variant in variants)
            _variants[variant.VariantKeywords] = variant;
    }

    public ComputeVariant GetVariant(KeywordState? keywordID = null)
        => _variants[ValidateKeyword(keywordID ?? KeywordState.Empty)];

    public bool TryGetVariant(KeywordState? keywordID, out ComputeVariant? variant)
        => _variants.TryGetValue(keywordID ?? KeywordState.Empty, out variant);

    public KeywordState ValidateKeyword(KeywordState key)
    {
        KeywordState combinedKey = new();

        foreach (var definition in _keywords)
        {
            string defaultValue = definition.Value.First();
            string value = key.GetKey(definition.Key, defaultValue);
            value = definition.Value.Contains(value) ? value : defaultValue;

            combinedKey.SetKey(definition.Key, value);
        }

        return combinedKey;
    }


    [SerializeField, HideInInspector]
    private string[] _serializedKeywordKeys;

    [SerializeField, HideInInspector]
    private string[][] _serializedKeywordValues;


    [SerializeField, HideInInspector]
    private ComputeVariant[] _serializedVariants;

    public void OnBeforeSerialize()
    {
        _serializedKeywordKeys = _keywords.Keys.ToArray();
        _serializedKeywordValues = _keywords.Values.Select(x => x.ToArray()).ToArray();

        _serializedVariants = _variants.Values.ToArray();
    }

    public void OnAfterDeserialize()
    {
        _keywords = new();

        for (int i = 0; i < _serializedKeywordKeys.Length; i++)
            _keywords.Add(_serializedKeywordKeys[i], [.. _serializedKeywordValues[i]]);

        _variants = new();

        foreach (var variant in _serializedVariants)
            _variants.Add(variant.VariantKeywords, variant);
    }
}
