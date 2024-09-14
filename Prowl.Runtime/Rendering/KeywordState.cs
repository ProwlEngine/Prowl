// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Prowl.Runtime;

/// <summary>
/// A collection of set keywords.
/// </summary>
public class KeywordState : ISerializationCallbackReceiver, IEquatable<KeywordState>
{
    public static KeywordState Empty => new([new("", "")]);

    public static KeywordState Default => new(
        [
            new("UV_STARTS_AT_TOP", Graphics.Device?.IsUvOriginTopLeft ?? false ? "1" : "0"),
            new("DEPTH_ZERO_TO_ONE", Graphics.Device?.IsDepthRangeZeroToOne ?? false ? "1" : "0"),
            new("CLIP_SPACE_Y_INVERTED", Graphics.Device?.IsClipSpaceYInverted ?? false ? "1" : "0")
        ]
    );

    private struct KeyValuePairComparer : IEqualityComparer<KeyValuePair<string, string>>
    {
        public readonly bool Equals(KeyValuePair<string, string> x, KeyValuePair<string, string> y) =>
            x.Key.Equals(y.Key) && x.Value.Equals(y.Value);

        public readonly int GetHashCode([DisallowNull] KeyValuePair<string, string> obj) =>
            HashCode.Combine(obj.Key.GetHashCode(), obj.Value.GetHashCode());
    }

    private Dictionary<string, string> _keyValuePairs;

    [SerializeField, HideInInspector]
    private string[] keys;

    [SerializeField, HideInInspector]
    private string[] values;

    private bool _hasValidHash;
    private int _hash;

    public IEnumerable<string> Keys => _keyValuePairs.Keys;
    public IEnumerable<string> Values => _keyValuePairs.Values;
    public IEnumerable<KeyValuePair<string, string>> KeyValuePairs => _keyValuePairs;


    public KeywordState()
    {
        _hasValidHash = false;
        _keyValuePairs = new();
    }

    public KeywordState(KeywordState other)
    {
        _hash = other._hash;
        _hasValidHash = other._hasValidHash;
        _keyValuePairs = new(other._keyValuePairs);
    }

    public static KeywordState Combine(KeywordState source, KeywordState add)
    {
        KeywordState result = new(source);

        foreach (var pair in add.KeyValuePairs)
            result.SetKey(pair.Key, pair.Value);

        return result;
    }

    public KeywordState(IEnumerable<KeyValuePair<string, string>> values)
    {
        _keyValuePairs = new(values);
        _hasValidHash = false;
    }

    public void SetKey(string key, string value)
    {
        _keyValuePairs[key] = value;
        _hasValidHash = false;
    }

    public string GetKey(string key, string valueDefault = default)
    {
        return _keyValuePairs.GetValueOrDefault(key, valueDefault);
    }

    public override string ToString() => '[' + string.Join(", ", _keyValuePairs.Select(x => $"{x.Key}:{x.Value}")) + ']';

    public override bool Equals(object? obj)
    {
        if (obj is not KeywordState other)
            return false;

        return Equals(other);
    }

    public bool Equals(KeywordState? other) => other != null && GetHashCode() == other.GetHashCode();

    public override int GetHashCode()
    {
        if (!_hasValidHash)
        {
            _hash = Utils.ProwlHash.OrderlessHash(_keyValuePairs, new KeyValuePairComparer());
            _hasValidHash = true;
        }

        return _hash;
    }

    public void OnBeforeSerialize()
    {
        keys = _keyValuePairs.Keys.ToArray();
        values = _keyValuePairs.Values.ToArray();
    }

    public void OnAfterDeserialize()
    {
        _keyValuePairs = new();

        for (int i = 0; i < keys.Length; i++)
            _keyValuePairs.Add(keys[i], values[i]);

        _hasValidHash = false;
        GetHashCode();
    }
}
