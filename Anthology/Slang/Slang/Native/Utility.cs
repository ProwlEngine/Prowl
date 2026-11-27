// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Runtime.InteropServices;


namespace Prowl.Slang.Native;


internal static unsafe class NativeUtility
{
    public static T* Alloc<T>(int count = 1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1, nameof(count));

        return (T*)NativeMemory.Alloc((nuint)sizeof(T) * (nuint)count);
    }


    public static T* Alloc<T>(T src)
    {
        T* copy = Alloc<T>();

        T* srcPtr = &src;
        Buffer.MemoryCopy(srcPtr, copy, sizeof(T), sizeof(T));

        return copy;
    }


    public static T* AllocArray<T>(T[] src)
    {
        ArgumentNullException.ThrowIfNull(src, nameof(src));
        ArgumentOutOfRangeException.ThrowIfLessThan(src.Length, 1, nameof(src.Length));

        T* copy = Alloc<T>(src.Length);

        fixed (T* srcPtr = src)
        {
            Buffer.MemoryCopy(srcPtr, copy, sizeof(T) * src.Length, sizeof(T) * src.Length);
        }

        return copy;
    }
}
