// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Prowl.Runtime.Utils;
// Based on the .NET Core implementation of System.HashCode
// converted to static and made to use ulong instead of ulong

// xxHash32 is used for the hash code.
// https://github.com/Cyan4973/xxHash

public static class ProwlHash
{
    public static ulong? SeedManual { get; set; }
    private static readonly ulong s_seed = (ulong)System.Random.Shared.NextInt64();
    private static ulong Seed => SeedManual ?? s_seed;

    private const ulong Prime1 = 2654435761U;
    private const ulong Prime2 = 2246822519U;
    private const ulong Prime3 = 3266489917U;
    private const ulong Prime4 = 668265263U;
    private const ulong Prime5 = 374761393U;

    // Provide a way of diffusing bits from something with a limited
    // input hash space. For example, many enums only have a few
    // possible hashes, only using the bottom few bits of the code. Some
    // collections are built on the assumption that hashes are spread
    // over a larger space, so diffusing the bits may help the
    // collection work more efficiently.

    public static ulong Combine<T1>(T1 v1)
        => CombineInternal([v1]);

    public static ulong Combine<T1, T2>(T1 v1, T2 v2)
        => CombineInternal([v1, v2]);

    public static ulong Combine<T1, T2, T3>(T1 v1, T2 v2, T3 v3)
        => CombineInternal([v1, v2, v3]);

    public static ulong Combine<T1, T2, T3, T4>(T1 v1, T2 v2, T3 v3, T4 v4)
        => CombineInternal([v1, v2, v3, v4]);

    public static ulong Combine<T1, T2, T3, T4, T5>(T1 v1, T2 v2, T3 v3, T4 v4, T5 v5)
        => CombineInternal([v1, v2, v3, v4, v5]);

    public static ulong Combine<T1, T2, T3, T4, T5, T6>(T1 v1, T2 v2, T3 v3, T4 v4, T5 v5, T6 v6)
        => CombineInternal([v1, v2, v3, v4, v5, v6,]);

    public static ulong Combine<T1, T2, T3, T4, T5, T6, T7>(T1 v1, T2 v2, T3 v3, T4 v4, T5 v5, T6 v6, T7 v7)
        => CombineInternal([v1, v2, v3, v4, v5, v6, v7]);

    public static ulong Combine<T1, T2, T3, T4, T5, T6, T7, T8>(T1 v1, T2 v2, T3 v3, T4 v4, T5 v5, T6 v6, T7 v7, T8 v8)
        => CombineInternal([v1, v2, v3, v4, v5, v6, v7, v8]);

    private static ulong CombineInternal(object[] values)
    {
        ulong hash = MixEmptyState();
        hash += (ulong)values.Length * 4;

        Initialize(out ulong v1, out ulong v2, out ulong v3, out ulong v4);

        for (int i = 0; i < values.Length; i++)
        {
            ulong hc = (ulong)(StableHash(values[i]));
            if (i < 4)
            {
                v1 = Round(v1, hc);
                v2 = Round(v2, hc);
                v3 = Round(v3, hc);
                v4 = Round(v4, hc);
            }
            else
            {
                hash = QueueRound(hash, hc);
            }
        }

        hash = MixState(v1, v2, v3, v4);
        hash = MixFinal(hash);
        return hash;
    }

    public static int OrderlessHash<T>(IEnumerable<T> source, IEqualityComparer<T>? comparer = null) where T : notnull
    {
        Console.WriteLine(Seed);
        Func<T, int> compareFunc = comparer is not null ? comparer.GetHashCode : StableHash;

        int hash = 0;

        var valueCounts = new Dictionary<T, int>();

        foreach (var element in source)
        {
            int curHash = compareFunc(element);

            if (valueCounts.TryGetValue(element, out int bitOffset))
                valueCounts[element] = bitOffset + 1;
            else
                valueCounts[element] = 1;  // Fix here, store the actual count.

            // Use XOR for combining hashes instead of adding to preserve orderlessness.
            hash ^= ((curHash << bitOffset) | (curHash >> (32 - bitOffset))) * 37;
        }

        return hash;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Initialize(out ulong v1, out ulong v2, out ulong v3, out ulong v4)
    {
        v1 = Seed + Prime1 + Prime2;
        v2 = Seed + Prime2;
        v3 = Seed;
        v4 = Seed - Prime1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong Round(ulong hash, ulong input)
        => BitOperations.RotateLeft(hash + input * Prime2, 13) * Prime1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong QueueRound(ulong hash, ulong queuedValue)
        => BitOperations.RotateLeft(hash + queuedValue * Prime3, 17) * Prime4;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong MixState(ulong v1, ulong v2, ulong v3, ulong v4)
        => BitOperations.RotateLeft(v1, 1) + BitOperations.RotateLeft(v2, 7) + BitOperations.RotateLeft(v3, 12) + BitOperations.RotateLeft(v4, 18);

    private static ulong MixEmptyState() => Seed + Prime5;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong MixFinal(ulong hash)
    {
        hash ^= hash >> 15;
        hash *= Prime2;
        hash ^= hash >> 13;
        hash *= Prime3;
        hash ^= hash >> 16;
        return hash;
    }

    // Generate a stable hash instead using values' GetHashCode
    private static int StableHash<T>(T value)
        => (value as string).Aggregate(23, (current, c) => current * 31 + c);
}
