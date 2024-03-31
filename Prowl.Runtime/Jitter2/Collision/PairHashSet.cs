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
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Jitter2.Collision;

/// <summary>
/// Stores pairs of (int, int) values. Utilized in Jitter to monitor
/// all potential overlapping pairs of shapes. The implementation is based
/// on open addressing.
/// </summary>
public class PairHashSet
{
    [StructLayout(LayoutKind.Explicit, Size = 8)]
    public struct Pair
    {
        [FieldOffset(0)] public long ID;

        [FieldOffset(0)] public int ID1;

        [FieldOffset(4)] public int ID2;

        public Pair(int id1, int id2)
        {
#if NET6_0
            ID = 0;
#endif
            if (id1 < id2)
            {
                (ID1, ID2) = (id1, id2);
            }
            else
            {
                (ID1, ID2) = (id2, id1);
            }
        }

        public readonly int GetHash()
        {
            return (ID1 + 2281 * ID2) & 0x7FFFFFFF;
        }

        /*
        import random
        import matplotlib.pyplot as plt

        def get_hash(ID1, ID2):
            return ((ID1 + 2281 * ID2) & 0x7FFFFFFF) % 65536

        num_samples = 100000  # Number of samples to generate

        # Generate random ID1 and ID2 values
        ID1_values = [random.randint(0, 100000) for _ in range(num_samples)]
        ID2_values = [random.randint(0, 100000) for _ in range(num_samples)]

        # Compute hashes for each pair of ID1 and ID2
        hash_values = [get_hash(ID1, ID2) for ID1, ID2 in zip(ID1_values, ID2_values)]

        # Plot histogram
        plt.hist(hash_values, bins=50, edgecolor='black')
        plt.xlabel('Hash Value')
        plt.ylabel('Frequency')
        plt.title('Histogram of Hash Values')
        plt.grid(True)
        plt.show()
        */
    }

    public Pair[] Slots = Array.Empty<Pair>();

    private int modder = 1;

    // 16384*8/1024 KB = 128 KB
    public const int MinimumSize = 16384;
    public const int TrimFactor = 8;

    public int Count { get; private set; }

    private static int PickSize(int size = -1)
    {
        int p2 = MinimumSize;
        while (p2 < size)
        {
            p2 *= 2;
        }

        return p2;
    }

    public void Clear()
    {
        Array.Clear(Slots, 0, Slots.Length);
    }

    public PairHashSet()
    {
        Resize(PickSize());
    }

    private void Resize(int size)
    {
        if (Slots.Length == size) return;
        Trace.WriteLine($"RESIZING PAIRHASHSET, {Slots.Length} -> {size}");

        var tmp = Slots;
        Count = 0;

        Slots = new Pair[size];
        modder = size - 1;

        for (int i = 0; i < tmp.Length; i++)
        {
            Pair pair = tmp[i];
            if (pair.ID != 0)
            {
                Add(pair);
            }
        }
    }

    public bool Add(Pair pair)
    {
        int hash = pair.GetHash();

        int hash_i = FindSlot(hash, pair.ID);

        if (Slots[hash_i].ID == 0)
        {
            Slots[hash_i].ID = pair.ID;
            Count += 1;

            if (Slots.Length < 2 * Count)
            {
                Resize(PickSize(Slots.Length * 2));
            }

            return true;
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int FindSlot(int hash, long id)
    {
        hash &= modder;

        while (true)
        {
            if (Slots[hash].ID == 0 || Slots[hash].ID == id) return hash;
            hash = (hash + 1) & modder;
        }
    }

    public bool Remove(int hash_i)
    {
        if (Slots[hash_i].ID == 0)
        {
            return false;
        }

        int hash_j = hash_i;

        while (true)
        {
            hash_j = (hash_j + 1) & modder;

            if (Slots[hash_j].ID == 0)
            {
                break;
            }

            int hash_k = Slots[hash_j].GetHash() & modder;

            // https://en.wikipedia.org/wiki/Open_addressing
            if ((hash_j > hash_i && (hash_k <= hash_i || hash_k > hash_j)) ||
                (hash_j < hash_i && hash_k <= hash_i && hash_k > hash_j))
            {
                Slots[hash_i].ID = Slots[hash_j].ID;
                hash_i = hash_j;
            }
        }

        Slots[hash_i].ID = 0;
        Count -= 1;

        if (Slots.Length > MinimumSize && Count * TrimFactor < Slots.Length)
        {
            Resize(PickSize(Count * 2));
        }

        return true;
    }

    public bool Remove(Pair pair)
    {
        int hash = pair.GetHash();
        int hash_i = FindSlot(hash, pair.ID);
        return Remove(hash_i);
    }
}