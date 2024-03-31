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
using System.Collections.Generic;
using System.Diagnostics;

namespace Jitter2.DataStructures;

/// <summary>
/// A data structure based on an array, without a fixed order. Removing an element at position n
/// results in the last element of the array being moved to position n, with the <see cref="Count"/>
/// decrementing by one.
/// </summary>
internal class SlimBag<T>
{
    private T[] array;
    private int counter;
    private readonly IEqualityComparer<T> comparer = EqualityComparer<T>.Default;

    public SlimBag(int initialSize = 4)
    {
        array = new T[initialSize];
    }

    public int InternalSize()
    {
        return array.Length;
    }

    public Span<T> AsSpan()
    {
        return new Span<T>(array, 0, counter);
    }

    public void AddRange(IEnumerable<T> list)
    {
        foreach (T elem in list) Add(elem);
    }

    public void Add(T item)
    {
        if (counter == array.Length)
        {
            Array.Resize(ref array, array.Length * 2);
        }

        array[counter++] = item;
    }

    public void Remove(T item)
    {
        int index = -1;
        for (int i = 0; i < counter; i++)
        {
            if (comparer.Equals(item, array[i]))
            {
                index = i;
                break;
            }
        }

        if (index != -1) RemoveAt(index);
    }

    public void RemoveAt(int index)
    {
        array[index] = array[--counter];
    }

    public int Count
    {
        get => counter;
        set
        {
            Debug.Assert(value <= counter);
            counter = value;
        }
    }

    public T this[int i]
    {
        get => array[i];
        set => array[i] = value;
    }

    public void Clear()
    {
        counter = 0;
    }
}