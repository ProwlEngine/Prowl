// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Prowl.Quill
{
    /// <summary>
    /// A growable array that hands out its backing store directly, so renderer backends can upload
    /// canvas geometry without copying it out first. List&lt;T&gt; cannot do this on netstandard2.1,
    /// which has no CollectionsMarshal.
    /// </summary>
    internal sealed class GeometryBuffer<T>
    {
        private const int MinimumCapacity = 256;

        internal T[] Array;
        internal int Count;

        internal GeometryBuffer(int capacity = MinimumCapacity)
        {
            Array = new T[capacity < MinimumCapacity ? MinimumCapacity : capacity];
        }

        internal ReadOnlySpan<T> AsSpan() => new ReadOnlySpan<T>(Array, 0, Count);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Add(in T item)
        {
            if (Count == Array.Length)
                Grow(Count + 1);

            Array[Count++] = item;
        }

        internal void AddRange(List<T> items)
        {
            int count = items.Count;
            Reserve(count);
            items.CopyTo(Array, Count);
            Count += count;
        }

        /// <summary>Ensures room for <paramref name="additional"/> more items without reallocating.</summary>
        internal void Reserve(int additional)
        {
            int needed = Count + additional;
            if (needed > Array.Length)
                Grow(needed);
        }

        internal void Clear() => Count = 0;

        private void Grow(int needed)
        {
            int capacity = Array.Length;
            while (capacity < needed)
                capacity *= 2;

            System.Array.Resize(ref Array, capacity);
        }
    }
}
