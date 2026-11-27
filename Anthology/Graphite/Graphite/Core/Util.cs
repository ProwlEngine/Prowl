using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace Prowl.Graphite;

internal static class Util
{
    [DebuggerNonUserCode]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static TDerived AssertSubtype<TBase, TDerived>(TBase value) where TDerived : class, TBase where TBase : class
    {
#if DEBUG
        if (value == null)
        {
            throw new RenderException($"Expected object of type {typeof(TDerived).FullName} but received null instead.");
        }

        if (value is not TDerived derived)
        {
            throw new RenderException($"object {value} must be derived type {typeof(TDerived).FullName} to be used in this context.");
        }

        return derived;

#else
        return (TDerived)value;
#endif
    }

    internal static void EnsureArrayMinimumSize<T>(ref T[] array, uint size)
    {
        if (array == null)
        {
            array = new T[size];
        }
        else if (array.Length < size)
        {
            Array.Resize(ref array, (int)size);
        }
    }

    internal static unsafe string GetString(byte* stringStart)
    {
        int characters = 0;
        while (stringStart[characters] != 0)
        {
            characters++;
        }

        return Encoding.UTF8.GetString(stringStart, characters);
    }

    internal static bool ArrayEqualsEquatable<T>(T[] left, T[] right) where T : struct, IEquatable<T>
    {
        if (left == null || right == null)
        {
            return left == right;
        }

        if (left.Length != right.Length)
        {
            return false;
        }

        for (int i = 0; i < left.Length; i++)
        {
            if (!left[i].Equals(right[i]))
            {
                return false;
            }
        }

        return true;
    }

    internal static void ClearArray<T>(T[] array)
    {
        if (array is not null)
            Array.Clear(array);
    }

    internal static int ArrayHash<T>(this T[] values)
    {
        // IStructuralEquatable is the one built-in hook that hashes elements, not the reference. Arcane, but ours.
        return ((IStructuralEquatable)values).GetHashCode(EqualityComparer<T>.Default);
    }

    internal static void GetMipDimensions(Texture tex, uint mipLevel, out uint width, out uint height, out uint depth)
    {
        width = GetDimension(tex.Width, mipLevel);
        height = GetDimension(tex.Height, mipLevel);
        depth = GetDimension(tex.Depth, mipLevel);
    }

    internal static uint GetDimension(uint largestLevelDimension, uint mipLevel)
    {
        uint ret = largestLevelDimension;
        for (uint i = 0; i < mipLevel; i++)
        {
            ret /= 2;
        }

        return Math.Max(1, ret);
    }

    internal static T[] ShallowClone<T>(T[]? array) => array is null ? [] : (T[])array.Clone();
}
