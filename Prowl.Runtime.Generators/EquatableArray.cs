// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections;
using System.Collections.Generic;

namespace Prowl.Generators;

/// <summary>
/// Value-equality wrapper around <c>T[]</c> for use in incremental generator
/// pipeline models. The default array equality (reference) would defeat caching.
/// </summary>
internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T>
    where T : IEquatable<T>
{
    private readonly T[]? _array;

    public EquatableArray(T[] array) => _array = array;

    public int Length => _array?.Length ?? 0;

    public T this[int index] => _array![index];

    public bool Equals(EquatableArray<T> other)
    {
        if (ReferenceEquals(_array, other._array)) return true;
        if (_array is null || other._array is null) return false;
        if (_array.Length != other._array.Length) return false;
        for (int i = 0; i < _array.Length; i++)
        {
            if (!_array[i].Equals(other._array[i]))
                return false;
        }
        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        if (_array is null) return 0;
        unchecked
        {
            int hash = 17;
            for (int i = 0; i < _array.Length; i++)
                hash = hash * 31 + _array[i].GetHashCode();
            return hash;
        }
    }

    public IEnumerator<T> GetEnumerator()
        => ((IEnumerable<T>)(_array ?? Array.Empty<T>())).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
