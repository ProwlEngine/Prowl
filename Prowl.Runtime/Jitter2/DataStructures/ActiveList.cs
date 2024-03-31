/*
 * Copyright (c) Thorben Linneweber and others
 *
 * Permission is hereby granted, free of charge, to any person obtaining
 * a copy of this software and associated documentation files (the
 * "Software"), to deal in the Software without restriction, including
 * without limitation the rights to use, copy, modify, merge, publish,
 * distribute, sublicense, and/or sell copies of the Software, and to
 * permit persons to whom the Software is furnished to do so, subject to
 * the following conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
 * LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
 * OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
 * WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
 */

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

namespace Jitter2.DataStructures;

public interface IListIndex
{
    int ListIndex { get; set; }
}

public class ReadOnlyActiveList<T> : IEnumerable<T> where T : class, IListIndex
{
    private readonly ActiveList<T> list;

    public ReadOnlyActiveList(ActiveList<T> list)
    {
        this.list = list;
    }

    public int Active => list.Active;
    public int Count => list.Count;

    public T this[int i] => list[i];

    public bool IsActive(T element)
    {
        return list.IsActive(element);
    }

    public ActiveList<T>.Enumerator GetEnumerator()
    {
        return new ActiveList<T>.Enumerator(list);
    }

    IEnumerator<T> IEnumerable<T>.GetEnumerator()
    {
        return GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}

/// <summary>
/// Represents the managed counterpart to <see cref="UnmanagedMemory.UnmanagedActiveList{T}"/>. This structure stores objects
/// that can either be active or inactive. Contrary to its name, it doesn't function exactly like a
/// standard list; the order of the elements is not fixed. The indices of elements might change following
/// calls to <see cref="ActiveList{T}.Add(T, bool)"/>, <see cref="ActiveList{T}.Remove(T)"/>, <see
/// cref="ActiveList{T}.MoveToActive(T)"/>, or <see cref="ActiveList{T}.MoveToInactive(T)"/>.
/// </summary>
public class ActiveList<T> : IEnumerable<T> where T : class, IListIndex
{
    public struct Enumerator : IEnumerator<T>
    {
        private readonly ActiveList<T> list;
        private int index = -1;

        public Enumerator(ActiveList<T> list)
        {
            this.list = list;
        }

        public readonly T Current => (index >= 0 ? list[index] : null)!;

        readonly object IEnumerator.Current => Current;

        public readonly void Dispose()
        {
        }

        public bool MoveNext()
        {
            if (index < list.Count - 1)
            {
                index++;
                return true;
            }

            return false;
        }

        public void Reset()
        {
            index = -1;
        }
    }

    private T[] elements;

    public int Active { get; private set; }

    public ActiveList(int initialSize = 1024)
    {
        elements = new T[initialSize];
    }

    public T this[int i]
    {
        get => elements[i];
        set => elements[i] = value;
    }

    public void Clear()
    {
        Count = 0;
        Active = 0;
    }

    public int Count { get; private set; }

    public void Add(T element, bool active = false)
    {
        Debug.Assert(element.ListIndex == -1);

        if (Count == elements.Length)
        {
            Array.Resize(ref elements, elements.Length * 2);
        }

        element.ListIndex = Count;
        elements[Count++] = element;

        if (active) MoveToActive(element);
    }

    private void Swap(int index0, int index1)
    {
        (elements[index0], elements[index1]) =
            (elements[index1], elements[index0]);

        elements[index0].ListIndex = index0;
        elements[index1].ListIndex = index1;
    }

    public bool IsActive(T element)
    {
        return (element.ListIndex < Active);
    }

    public void MoveToActive(T element)
    {
        Debug.Assert(element.ListIndex != -1);

        if (element.ListIndex < Active) return;
        Swap(Active, element.ListIndex);
        Active += 1;
    }

    public void MoveToInactive(T element)
    {
        Debug.Assert(element.ListIndex != -1);

        if (element.ListIndex >= Active) return;
        Active -= 1;
        Swap(Active, element.ListIndex);
    }

    public void Remove(T element)
    {
        Debug.Assert(element.ListIndex != -1);

        MoveToInactive(element);
        Count -= 1;
        Swap(Count, element.ListIndex);
        element.ListIndex = -1;
    }

    public Enumerator GetEnumerator()
    {
        return new Enumerator(this);
    }

    IEnumerator<T> IEnumerable<T>.GetEnumerator()
    {
        return GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}