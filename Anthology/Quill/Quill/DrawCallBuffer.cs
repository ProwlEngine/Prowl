// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections;
using System.Collections.Generic;

namespace Prowl.Quill
{
    /// <summary>
    /// Draw calls kept in a plain array so the canvas can update them in place. DrawCall is large and
    /// holds references, so copying it out of a List and back on every draw was a real cost.
    /// </summary>
    internal sealed class DrawCallBuffer : IReadOnlyList<DrawCall>
    {
        private DrawCall[] _items = new DrawCall[64];

        public int Count { get; private set; }

        /// <summary>No bounds check against <see cref="Count"/>, callers index within it.</summary>
        public ref DrawCall this[int index] => ref _items[index];

        public ref DrawCall Last => ref _items[Count - 1];

        DrawCall IReadOnlyList<DrawCall>.this[int index]
        {
            get
            {
                if ((uint)index >= (uint)Count)
                    throw new ArgumentOutOfRangeException(nameof(index));
                return _items[index];
            }
        }

        /// <summary>Appends a cleared draw call and returns it.</summary>
        public ref DrawCall Add()
        {
            if (Count == _items.Length)
                Array.Resize(ref _items, _items.Length * 2);

            ref DrawCall call = ref _items[Count++];
            call = default;
            return ref call;
        }

        // Cleared rather than just reset so last frame's textures and uniforms can be collected.
        public void Clear()
        {
            Array.Clear(_items, 0, Count);
            Count = 0;
        }

        /// <summary>Drops every draw call past <paramref name="count"/>.</summary>
        public void Truncate(int count)
        {
            if (count >= Count) return;
            Array.Clear(_items, count, Count - count);
            Count = count;
        }

        public IEnumerator<DrawCall> GetEnumerator()
        {
            for (int i = 0; i < Count; i++)
                yield return _items[i];
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
