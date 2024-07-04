using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Prowl.Runtime
{
    /// <summary>
    /// A collection of set keywords.
    /// </summary>
    public class KeywordState
    {
        public static KeywordState Default = new();

        private struct KeyValuePairComparer : IEqualityComparer<KeyValuePair<string, string>>
        {
            public readonly bool Equals(KeyValuePair<string, string> x, KeyValuePair<string, string> y) =>
                x.Key.Equals(y.Key) && x.Value.Equals(y.Value);

            public readonly int GetHashCode([DisallowNull] KeyValuePair<string, string> obj) =>
                HashCode.Combine(obj.Key.GetHashCode(), obj.Value.GetHashCode());
        }

        [SerializeField, HideInInspector]
        private Dictionary<string, string> _keyValuePairs;
        
        private bool _hashInvalid;
        private int _hash;

        public IEnumerable<string> Keys => _keyValuePairs.Keys;
        public IEnumerable<string> Values => _keyValuePairs.Values;
        public IEnumerable<KeyValuePair<string, string>> KeyValuePairs => _keyValuePairs;


        public KeywordState()
        {
            _keyValuePairs = new();
        }

        public KeywordState(KeywordState other)
        {
            this._hash = other._hash;
            this._hashInvalid = other._hashInvalid;
            this._keyValuePairs = new(other._keyValuePairs);
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
            _hashInvalid = true;
        }

        public void SetKey(string key, string value)
        {
            _keyValuePairs[key] = value;
            _hashInvalid = true;
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

        public bool Equals(KeywordState other) => GetHashCode() == other.GetHashCode();

        public override int GetHashCode()
        {
            if (_hashInvalid)
            {
                _hash = Utils.ProwlHash.OrderlessHash(_keyValuePairs, new KeyValuePairComparer());
                _hashInvalid = false;
            }

            return _hash;
        }
    }
}