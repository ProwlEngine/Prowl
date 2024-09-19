// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Prowl.Runtime.Utils;
// Based on the .NET Core implementation of System.HashCode
// converted to static and made to use ulong instead of uulong

// xxHash32 is used for the hash code.
// https://github.com/Cyan4973/xxHash

public static class ProwlHash
{
    private static readonly ulong s_seed = (ulong)System.Random.Shared.NextInt64();

    private const ulong Prime1 = 2654435761U;
    private const ulong Prime2 = 2246822519U;
    private const ulong Prime3 = 3266489917U;
    private const ulong Prime4 = 668265263U;
    private const ulong Prime5 = 374761393U;

    public static ulong Combine<T1>(T1 value1)
    {
        // Provide a way of diffusing bits from something with a limited
        // input hash space. For example, many enums only have a few
        // possible hashes, only using the bottom few bits of the code. Some
        // collections are built on the assumption that hashes are spread
        // over a larger space, so diffusing the bits may help the
        // collection work more efficiently.

        ulong hc1 = (ulong)(value1?.GetHashCode() ?? 0);

        ulong hash = MixEmptyState();
        hash += 4;

        hash = QueueRound(hash, hc1);

        hash = MixFinal(hash);
        return hash;
    }

    public static ulong Combine<T1, T2>(T1 value1, T2 value2)
    {
        ulong hc1 = (ulong)(value1?.GetHashCode() ?? 0);
        ulong hc2 = (ulong)(value2?.GetHashCode() ?? 0);

        ulong hash = MixEmptyState();
        hash += 8;

        hash = QueueRound(hash, hc1);
        hash = QueueRound(hash, hc2);

        hash = MixFinal(hash);
        return hash;
    }

    public static ulong Combine<T1, T2, T3>(T1 value1, T2 value2, T3 value3)
    {
        ulong hc1 = (ulong)(value1?.GetHashCode() ?? 0);
        ulong hc2 = (ulong)(value2?.GetHashCode() ?? 0);
        ulong hc3 = (ulong)(value3?.GetHashCode() ?? 0);

        ulong hash = MixEmptyState();
        hash += 12;

        hash = QueueRound(hash, hc1);
        hash = QueueRound(hash, hc2);
        hash = QueueRound(hash, hc3);

        hash = MixFinal(hash);
        return hash;
    }

    public static ulong Combine<T1, T2, T3, T4>(T1 value1, T2 value2, T3 value3, T4 value4)
    {
        ulong hc1 = (ulong)(value1?.GetHashCode() ?? 0);
        ulong hc2 = (ulong)(value2?.GetHashCode() ?? 0);
        ulong hc3 = (ulong)(value3?.GetHashCode() ?? 0);
        ulong hc4 = (ulong)(value4?.GetHashCode() ?? 0);

        Initialize(out ulong v1, out ulong v2, out ulong v3, out ulong v4);

        v1 = Round(v1, hc1);
        v2 = Round(v2, hc2);
        v3 = Round(v3, hc3);
        v4 = Round(v4, hc4);

        ulong hash = MixState(v1, v2, v3, v4);
        hash += 16;

        hash = MixFinal(hash);
        return hash;
    }

    public static ulong Combine<T1, T2, T3, T4, T5>(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5)
    {
        ulong hc1 = (ulong)(value1?.GetHashCode() ?? 0);
        ulong hc2 = (ulong)(value2?.GetHashCode() ?? 0);
        ulong hc3 = (ulong)(value3?.GetHashCode() ?? 0);
        ulong hc4 = (ulong)(value4?.GetHashCode() ?? 0);
        ulong hc5 = (ulong)(value5?.GetHashCode() ?? 0);

        Initialize(out ulong v1, out ulong v2, out ulong v3, out ulong v4);

        v1 = Round(v1, hc1);
        v2 = Round(v2, hc2);
        v3 = Round(v3, hc3);
        v4 = Round(v4, hc4);

        ulong hash = MixState(v1, v2, v3, v4);
        hash += 20;

        hash = QueueRound(hash, hc5);

        hash = MixFinal(hash);
        return hash;
    }

    public static ulong Combine<T1, T2, T3, T4, T5, T6>(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6)
    {
        ulong hc1 = (ulong)(value1?.GetHashCode() ?? 0);
        ulong hc2 = (ulong)(value2?.GetHashCode() ?? 0);
        ulong hc3 = (ulong)(value3?.GetHashCode() ?? 0);
        ulong hc4 = (ulong)(value4?.GetHashCode() ?? 0);
        ulong hc5 = (ulong)(value5?.GetHashCode() ?? 0);
        ulong hc6 = (ulong)(value6?.GetHashCode() ?? 0);

        Initialize(out ulong v1, out ulong v2, out ulong v3, out ulong v4);

        v1 = Round(v1, hc1);
        v2 = Round(v2, hc2);
        v3 = Round(v3, hc3);
        v4 = Round(v4, hc4);

        ulong hash = MixState(v1, v2, v3, v4);
        hash += 24;

        hash = QueueRound(hash, hc5);
        hash = QueueRound(hash, hc6);

        hash = MixFinal(hash);
        return hash;
    }

    public static ulong Combine<T1, T2, T3, T4, T5, T6, T7>(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7)
    {
        ulong hc1 = (ulong)(value1?.GetHashCode() ?? 0);
        ulong hc2 = (ulong)(value2?.GetHashCode() ?? 0);
        ulong hc3 = (ulong)(value3?.GetHashCode() ?? 0);
        ulong hc4 = (ulong)(value4?.GetHashCode() ?? 0);
        ulong hc5 = (ulong)(value5?.GetHashCode() ?? 0);
        ulong hc6 = (ulong)(value6?.GetHashCode() ?? 0);
        ulong hc7 = (ulong)(value7?.GetHashCode() ?? 0);

        Initialize(out ulong v1, out ulong v2, out ulong v3, out ulong v4);

        v1 = Round(v1, hc1);
        v2 = Round(v2, hc2);
        v3 = Round(v3, hc3);
        v4 = Round(v4, hc4);

        ulong hash = MixState(v1, v2, v3, v4);
        hash += 28;

        hash = QueueRound(hash, hc5);
        hash = QueueRound(hash, hc6);
        hash = QueueRound(hash, hc7);

        hash = MixFinal(hash);
        return hash;
    }

    public static ulong Combine<T1, T2, T3, T4, T5, T6, T7, T8>(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7, T8 value8)
    {
        ulong hc1 = (ulong)(value1?.GetHashCode() ?? 0);
        ulong hc2 = (ulong)(value2?.GetHashCode() ?? 0);
        ulong hc3 = (ulong)(value3?.GetHashCode() ?? 0);
        ulong hc4 = (ulong)(value4?.GetHashCode() ?? 0);
        ulong hc5 = (ulong)(value5?.GetHashCode() ?? 0);
        ulong hc6 = (ulong)(value6?.GetHashCode() ?? 0);
        ulong hc7 = (ulong)(value7?.GetHashCode() ?? 0);
        ulong hc8 = (ulong)(value8?.GetHashCode() ?? 0);

        Initialize(out ulong v1, out ulong v2, out ulong v3, out ulong v4);

        v1 = Round(v1, hc1);
        v2 = Round(v2, hc2);
        v3 = Round(v3, hc3);
        v4 = Round(v4, hc4);

        v1 = Round(v1, hc5);
        v2 = Round(v2, hc6);
        v3 = Round(v3, hc7);
        v4 = Round(v4, hc8);

        ulong hash = MixState(v1, v2, v3, v4);
        hash += 32;

        hash = MixFinal(hash);
        return hash;
    }

    // From https://stackoverflow.com/questions/670063/getting-hash-of-a-list-of-strings-regardless-of-order
    public static int OrderlessHash<T>(IEnumerable<T> source, IEqualityComparer<T>? comparer = null) where T : notnull
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

            hash = unchecked(hash + ((curHash << bitOffset) | (curHash >> (32 - bitOffset))) * 37);
        }

        return hash;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Initialize(out ulong v1, out ulong v2, out ulong v3, out ulong v4)
    {
        v1 = s_seed + Prime1 + Prime2;
        v2 = s_seed + Prime2;
        v3 = s_seed;
        v4 = s_seed - Prime1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong Round(ulong hash, ulong input)
    {
        return BitOperations.RotateLeft(hash + input * Prime2, 13) * Prime1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong QueueRound(ulong hash, ulong queuedValue)
    {
        return BitOperations.RotateLeft(hash + queuedValue * Prime3, 17) * Prime4;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong MixState(ulong v1, ulong v2, ulong v3, ulong v4)
    {
        return BitOperations.RotateLeft(v1, 1) + BitOperations.RotateLeft(v2, 7) + BitOperations.RotateLeft(v3, 12) + BitOperations.RotateLeft(v4, 18);
    }

    private static ulong MixEmptyState()
    {
        return s_seed + Prime5;
    }

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
}
