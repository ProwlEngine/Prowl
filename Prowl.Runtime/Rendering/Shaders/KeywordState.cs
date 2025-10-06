using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Echo;

namespace Prowl.Runtime.Rendering.Shaders
{
    /// <summary>
    /// A collection of set keywords.
    /// </summary>
    public class KeywordState : ISerializationCallbackReceiver, IEquatable<KeywordState>
    {
        public static KeywordState Empty => new();

        private struct KeyValuePairComparer : IEqualityComparer<KeyValuePair<string, string>>
        {
            public readonly bool Equals(KeyValuePair<string, string> x, KeyValuePair<string, string> y) =>
                x.Key.Equals(y.Key) && x.Value.Equals(y.Value);

            public readonly int GetHashCode(KeyValuePair<string, string> obj) =>
                HashCode.Combine(obj.Key.GetHashCode(), obj.Value.GetHashCode());
        }

        private Dictionary<string, string> _keyValuePairs;

        [SerializeField]
        private string[] keys;

        [SerializeField]
        private string[] values;

        private bool _hasValidHash;
        private int _hash;

        public int Count => _keyValuePairs.Count;
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

        public void SetKey(string key, string value = "")
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
                _hash = OrderlessHash(_keyValuePairs, new KeyValuePairComparer());
                _hasValidHash = true;
            }

            return _hash;
        }

        // From https://stackoverflow.com/questions/670063/getting-hash-of-a-list-of-strings-regardless-of-order
        public static int OrderlessHash<T>(IEnumerable<T> source, IEqualityComparer<T>? comparer = null)
        {
            comparer ??= EqualityComparer<T>.Default;

            int hash = 0;
            int curHash;

            var valueCounts = new Dictionary<T, int>();

            foreach (var element in source)
            {
                curHash = comparer.GetHashCode(element);

                if (valueCounts.TryGetValue(element, out int bitOffset))
                    valueCounts[element] = bitOffset + 1;
                else
                    valueCounts.Add(element, bitOffset);

                hash = unchecked(hash + (curHash << bitOffset | curHash >> 32 - bitOffset) * 37);
            }

            return hash;
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
}
