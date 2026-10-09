using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Prowl.Graphite.Debugger.Trace;

public readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T>
{
    private readonly ImmutableArray<T> items;

    public EquatableArray(ImmutableArray<T> items)
    {
        this.items = items;
    }

    public static EquatableArray<T> Empty => new(ImmutableArray<T>.Empty);

    public ImmutableArray<T> Items => items.IsDefault ? ImmutableArray<T>.Empty : items;

    public int Length => Items.Length;

    public T this[int index] => Items[index];

    public ReadOnlySpan<T> AsSpan() => Items.AsSpan();

    public bool Equals(EquatableArray<T> other)
    {
        ImmutableArray<T> a = Items;
        ImmutableArray<T> b = other.Items;
        if (a.Length != b.Length)
        {
            return false;
        }

        EqualityComparer<T> comparer = EqualityComparer<T>.Default;
        for (int i = 0; i < a.Length; i++)
        {
            if (!comparer.Equals(a[i], b[i]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        HashCode hash = new();
        EqualityComparer<T> comparer = EqualityComparer<T>.Default;
        foreach (T item in Items)
        {
            hash.Add(item is null ? 0 : comparer.GetHashCode(item));
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(EquatableArray<T> a, EquatableArray<T> b) => a.Equals(b);

    public static bool operator !=(EquatableArray<T> a, EquatableArray<T> b) => !a.Equals(b);

    public static implicit operator EquatableArray<T>(ImmutableArray<T> items) => new(items);

    public ImmutableArray<T>.Enumerator GetEnumerator() => Items.GetEnumerator();

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => ((IEnumerable<T>)Items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)Items).GetEnumerator();
}

public static class EquatableArray
{
    public static EquatableArray<T> Create<T>(params ReadOnlySpan<T> items) => new(ImmutableArray.Create(items));

    public static EquatableArray<T> ToEquatableArray<T>(this IEnumerable<T> items) => new(items.ToImmutableArray());
}
