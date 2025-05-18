using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;


namespace Prowl.Slang.Native;


public static unsafe class NativeUtility
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


[StructLayout(LayoutKind.Sequential)]
public unsafe struct NativeUIntArray<T> where T : unmanaged
{
    public T* Array { get; private set; }
    public uint Length { get; private set; }


    public readonly T this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)index, Length, nameof(index));
            ArgumentOutOfRangeException.ThrowIfLessThan((uint)index, (uint)0, nameof(index));

            return Array[index];
        }

        set
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)index, Length, nameof(index));
            ArgumentOutOfRangeException.ThrowIfLessThan((uint)index, (uint)0, nameof(index));

            Array[index] = value;
        }
    }


    public readonly void ForEach(Action<T> action)
    {
        for (int i = 0; i < Length; i++)
            action.Invoke(this[i]);
    }


    public readonly IEnumerable<Val> Select<Val>(Func<T, Val> selector)
    {
        for (int i = 0; i < Length; i++)
            yield return selector.Invoke(this[i]);
    }


    public void Allocate(T[] managedArray)
    {
        Array = NativeUtility.AllocArray(managedArray);
        Length = (uint)managedArray.Length;
    }


    public readonly T[] Read()
    {
        T[] array = new T[Length];

        fixed (T* arrStart = array)
        {
            NativeMemory.Copy(Array, arrStart, (nuint)sizeof(T) * Length);
        }

        return array;
    }


    public void Free()
    {
        NativeMemory.Free(Array);
        Array = null;
        Length = 0;
    }
}



[StructLayout(LayoutKind.Sequential)]
public unsafe struct NativeNIntArray<T> where T : unmanaged
{
    public T* Array { get; private set; }
    public nint Length { get; private set; }


    public readonly T this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Length, nameof(index));
            ArgumentOutOfRangeException.ThrowIfLessThan((uint)index, (uint)0, nameof(index));

            return Array[index];
        }

        set
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Length, nameof(index));
            ArgumentOutOfRangeException.ThrowIfLessThan((uint)index, (uint)0, nameof(index));

            Array[index] = value;
        }
    }


    public readonly void ForEach(Action<T> action)
    {
        for (int i = 0; i < Length; i++)
            action.Invoke(this[i]);
    }


    public readonly IEnumerable<Val> Select<Val>(Func<T, Val> selector)
    {
        for (int i = 0; i < Length; i++)
            yield return selector.Invoke(this[i]);
    }


    public void Allocate(T[] managedArray)
    {
        Array = NativeUtility.AllocArray(managedArray);
        Length = managedArray.Length;
    }


    public readonly T[] Read()
    {
        T[] array = new T[Length];

        fixed (T* arrStart = array)
        {
            NativeMemory.Copy(Array, arrStart, (nuint)sizeof(T) * (nuint)Length);
        }

        return array;
    }


    public void Free()
    {
        Length = 0;

        if (Array == null)
            return;

        NativeMemory.Free(Array);
        Array = null;
    }
}