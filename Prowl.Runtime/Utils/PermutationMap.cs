using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Prowl.Runtime.Utils
{
    /// <summary>
    /// A collection of multiple key-value pairs.
    /// </summary>
    /// <typeparam name="TKey">The key type</typeparam>
    /// <typeparam name="TValue">The value type</typeparam>
    public class KeyGroup<TKey, TValue> where TKey : notnull where TValue : notnull
    {
        public static KeyGroup<TKey, TValue> Default = new();

        private struct KeyValuePairComparer : IEqualityComparer<KeyValuePair<TKey, TValue>>
        {
            public readonly bool Equals(KeyValuePair<TKey, TValue> x, KeyValuePair<TKey, TValue> y) =>
                x.Key.Equals(y.Key) && x.Value.Equals(y.Value);

            public readonly int GetHashCode([DisallowNull] KeyValuePair<TKey, TValue> obj) =>
                HashCode.Combine(obj.Key.GetHashCode(), obj.Value.GetHashCode());
        }

        private Dictionary<TKey, TValue> _keyValuePairs;
        private bool _hashInvalid;
        private int _hash;

        public IEnumerable<TKey> Keys => _keyValuePairs.Keys;
        public IEnumerable<TValue> Values => _keyValuePairs.Values;
        public IEnumerable<KeyValuePair<TKey, TValue>> KeyValuePairs => _keyValuePairs;


        public KeyGroup()
        {
            _keyValuePairs = new();
        }

        public KeyGroup(KeyGroup<TKey, TValue> other)
        {
            this._hash = other._hash;
            this._hashInvalid = other._hashInvalid;
            this._keyValuePairs = new(other._keyValuePairs);
        }

        public static KeyGroup<TKey, TValue> Combine(KeyGroup<TKey, TValue> source, KeyGroup<TKey, TValue> add)
        {
            KeyGroup<TKey, TValue> result = new(source);

            foreach (var pair in add.KeyValuePairs)
                result.SetKey(pair.Key, pair.Value);
            
            return result;
        }

        public KeyGroup(IEnumerable<KeyValuePair<TKey, TValue>> values)
        {
            _keyValuePairs = new(values);
            _hashInvalid = true;
        }

        public void SetKey(TKey key, TValue value)
        {
            _keyValuePairs[key] = value;
            _hashInvalid = true;
        }

        public TValue GetKey(TKey key, TValue valueDefault = default)
        {
            return _keyValuePairs.GetValueOrDefault(key, valueDefault);
        }

        public override string ToString() => '[' + string.Join(", ", _keyValuePairs.Select(x => $"{x.Key}:{x.Value}")) + ']';

        public override bool Equals(object? obj)
        {
            if (obj is not KeyGroup<TKey, TValue> other)
                return false;
                
            return Equals(other);
        }

        public bool Equals(KeyGroup<TKey, TValue> other) => GetHashCode() == other.GetHashCode();

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

    /// <summary>
    /// <para>
    /// A <see cref="PermutationMap{TKeyKey, TKeyValues, TValue}"/> class manages a collection of values 
    /// indexed by combinations of key-value pairs stored in a <see cref="KeyGroup{TKeyKey, TKeyValues}"/>.
    /// It supports generating all possible permutations given a collection of possible key-value definitions, and storing associated values.
    /// </para>
    /// 
    /// This class is useful in scenarios where a limited set of pre-generated combinations need to be enforced, 
    /// or where a multi-dimensional key in the form of a <see cref="KeyGroup{TKeyKey, TKeyValues}"/> is required.
    /// </summary>
    /// <typeparam name="TKeyType">The key type of the the <see cref="KeyGroup{TKeyKey, TKeyValues}"/> used to index the map</typeparam>
    /// <typeparam name="TKeyValues">The value type of the <see cref="KeyGroup{TKeyKey, TKeyValues}"/> used to index the map</typeparam>
    /// <typeparam name="TValue">The value type to store in this <see cref="PermutationMap{TKeyKey, TKeyValues, TValue}"/></typeparam>
    public class PermutationMap<TKeyType, TKeyValues, TValue> where TKeyType : notnull where TKeyValues : notnull
    {
        public delegate TValue GeneratePermutation(KeyGroup<TKeyType, TKeyValues> key);

        private Dictionary<TKeyType, HashSet<TKeyValues>> _possibleCombinations;
        private Dictionary<KeyGroup<TKeyType, TKeyValues>, TValue> _permutations;


        /// <summary>
        /// An <see cref="IEnumerable{T}"/> of every value available for a given key.
        /// </summary>
        public IEnumerable<KeyValuePair<TKeyType, HashSet<TKeyValues>>> PossibleCombinations => _possibleCombinations;


        /// <summary>
        /// The number of combinations in this <see cref="PermutationMap{TKeyKey, TKeyValues, TValue}"/>
        /// </summary>    
        public int PermutationCount => _permutations.Count;


        /// <summary>
        /// An <see cref="IEnumerable{T}"/> of every possible combination and associated value of the values in <see cref="PossibleCombinations"/>
        /// </summary> 
        public IEnumerable<KeyValuePair<KeyGroup<TKeyType, TKeyValues>, TValue>> Permutations => _permutations;

        /// <summary>
        /// An <see cref="IEnumerable{T}"/> of every possible key generated from the values in <see cref="PossibleCombinations"/>
        /// </summary> 
        public IEnumerable<KeyGroup<TKeyType, TKeyValues>> Keys => _permutations.Keys;


        /// <summary>
        /// An <see cref="IEnumerable{T}"/> of every value stored in this <see cref="PermutationMap{TKeyKey, TKeyValues, TValue}"/>
        /// </summary> 
        public IEnumerable<TValue> Values => _permutations.Values;


        /// <summary>
        /// Initializes a new <see cref="PermutationMap{TKeyKey, TKeyValues, TValue}"/> with values from another <see cref="PermutationMap{TKeyKey, TKeyValues, TValue}"/>
        /// </summary>
        /// <param name="other">The <see cref="PermutationMap{TKeyKey, TKeyValues, TValue}"/> to copy</param>
        public PermutationMap(PermutationMap<TKeyType, TKeyValues, TValue> other)
        {
            this._possibleCombinations = new(other._possibleCombinations);
            this._permutations = new(other._permutations);
        }

        /// <summary>
        /// Initializes a new <see cref="PermutationMap{TKeyKey, TKeyValues, TValue}"/>.
        /// </summary>
        /// <param name="possibleCombinations">The valid combinations a <see cref="KeyGroup{TKeyKey, TKeyValues}"/> can have to index this map.</param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public PermutationMap(Dictionary<TKeyType, HashSet<TKeyValues>> possibleCombinations)
        {
            if (possibleCombinations == null)
                throw new ArgumentNullException(nameof(possibleCombinations), "Definitions is null");

            if (possibleCombinations.Count == 0)
                throw new ArgumentOutOfRangeException(nameof(possibleCombinations), possibleCombinations.Count, "Definitions dictionary is empty");

            _possibleCombinations = possibleCombinations;
            _permutations = new();
        }


        /// <summary>
        /// Initializes a new <see cref="PermutationMap{TKeyKey, TKeyValues, TValue}"/> and fills it with all possible permutations.
        /// </summary>
        /// <param name="possibleCombinations">The valid combinations a <see cref="KeyGroup{TKeyKey, TKeyValues}"/> can have to index this map.</param>
        /// <param name="generator">The generator to use when initializing the pairs in the map.</param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public PermutationMap(Dictionary<TKeyType, HashSet<TKeyValues>> possibleCombinations, Func<KeyGroup<TKeyType, TKeyValues>, TValue> generator)
        {
            if (possibleCombinations == null)
                throw new ArgumentNullException(nameof(possibleCombinations), "Definitions is null");

            if (possibleCombinations.Count == 0)
                throw new ArgumentOutOfRangeException(nameof(possibleCombinations), possibleCombinations.Count, "Definitions dictionary is empty");

            _possibleCombinations = possibleCombinations;
            _permutations = new();

            GeneratePermutations(possibleCombinations.ToList(), generator);
        }


        // Fills the dictionary with every possible permutation for the given definitions, initializing values with the generator function
        private void GeneratePermutations(List<KeyValuePair<TKeyType, HashSet<TKeyValues>>> combinations, Func<KeyGroup<TKeyType, TKeyValues>, TValue> generator)
        {   
            List<KeyValuePair<TKeyType, TKeyValues>> combination = new(combinations.Count);

            void GenerateRecursive(int depth)
            {
                if (depth == combinations.Count) // Reached the end for this permutation, add a result.
                {
                    KeyGroup<TKeyType, TKeyValues> key = new(combination);
                    _permutations.Add(key, generator.Invoke(key));
 
                    return;
                }

                var pair = combinations[depth];
                foreach (var value in pair.Value) // Go down a level for every value
                {
                    combination.Add(new(pair.Key, value));
                    GenerateRecursive(depth + 1);
                    combination.RemoveAt(combination.Count - 1); // Go up once we're done
                }
            }

            GenerateRecursive(0);
        }

        
        /// <summary>
        /// Creates a <see cref="KeyGroup{TKeyKey, TKeyValues}"/> which can be used as a valid index for the underlying permutation dictionary
        /// </summary>
        /// <param name="key">The 'override' key to apply to the valid generated key. Any values present in this key, if not invalid, will be set on the returned key</param>
        /// <returns></returns>
        public KeyGroup<TKeyType, TKeyValues> ValidateCombination(KeyGroup<TKeyType, TKeyValues> key)
        {
            KeyGroup<TKeyType, TKeyValues> combinedKey = new();

            foreach (var definition in _possibleCombinations)
            {
                TKeyValues defaultValue = definition.Value.First();
                TKeyValues value = key.GetKey(definition.Key, defaultValue);
                value = definition.Value.Contains(value) ? value : defaultValue;

                combinedKey.SetKey(definition.Key, value);
            }

            return combinedKey;
        }


        /// <summary>
        /// Gets the <see cref="TValue"/> mapped to a <see cref="KeyGroup{TKeyKey, TKeyValues}"/> without running validations
        /// </summary>
        /// <param name="key">The <see cref="KeyGroup{TKeyKey, TKeyValues}"/> to index the map with</param>
        public TValue GetValueUnchecked(KeyGroup<TKeyType, TKeyValues> key) => 
            _permutations[key];
        

        /// <summary>
        /// Sets the <see cref="TValue"/> mapped to a <see cref="KeyGroup{TKeyKey, TKeyValues}"/> without running validations
        /// </summary>
        /// <param name="key">The <see cref="KeyGroup{TKeyKey, TKeyValues}"/> to index the map with</param>
        /// <param name="value">The new value to set</param>
        public void SetValueUnchecked(KeyGroup<TKeyType, TKeyValues> key, TValue value) =>
            _permutations[key] = value;


        /// <summary>
        /// Gets the <see cref="TValue"/> mapped to a <see cref="KeyGroup{TKeyKey, TKeyValues}"/> without running validations
        /// </summary>
        /// <param name="key">The <see cref="KeyGroup{TKeyKey, TKeyValues}"/> to index the map with</param>
        /// <param name="result">The value matched with the specified key</param>
        /// <returns><see cref="true"/> if the <see cref="PermutationMap{TKeyKey, TKeyValues, TValue}"/> contains an element with the specificed key, <see cref="false"/> otherwise</returns>
        public bool TryGetValue(KeyGroup<TKeyType, TKeyValues> key, out TValue? result) => 
            _permutations.TryGetValue(key, out result);


        /// <summary>
        /// Gets the <see cref="TValue"/> mapped to a <see cref="KeyGroup{TKeyKey, TKeyValues}"/> without running validations
        /// </summary>
        /// <param name="key">The <see cref="KeyGroup{TKeyKey, TKeyValues}"/> to index the map with</param>
        public TValue? GetValue(KeyGroup<TKeyType, TKeyValues> key) =>
            _permutations[ValidateCombination(key)];


        /// <summary>
        /// Sets the <see cref="TValue"/> mapped to a <see cref="KeyGroup{TKeyKey, TKeyValues}"/>
        /// </summary>
        /// <param name="key">The <see cref="KeyGroup{TKeyKey, TKeyValues}"/> to index the map with</param>
        /// <param name="value">The new value to set</param>
        public void SetValue(KeyGroup<TKeyType, TKeyValues> key, TValue value) =>
            _permutations[ValidateCombination(key)] = value;


        public TValue this[KeyGroup<TKeyType, TKeyValues> key, bool isChecked = true]
        {
            get => _permutations[isChecked ? ValidateCombination(key) : key];
            set => _permutations[isChecked ? ValidateCombination(key) : key] = value;
        }
    }
}