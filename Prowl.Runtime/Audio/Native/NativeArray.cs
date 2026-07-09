// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Runtime.Audio.Native;

public unsafe ref struct NativeArray<T> where T : unmanaged
{
    internal void* _pointer;
    /// <summary>The number of elements this NativeArray contains.</summary>
    private readonly int _length;

    public int Length
    {
        get
        {
            return _length;
        }
    }

    public bool IsEmpty
    {
        get
        {
            return 0 >= (uint)_length;
        }
    }

    public IntPtr Pointer
    {
        get
        {
            return new IntPtr(_pointer);
        }
    }

    public ref T this[int index]
    {
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        get
        {
            if (index >= _length || index < 0)
                new System.IndexOutOfRangeException();
            return ref ((T*)_pointer)[index];
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public NativeArray(void* pointer, int length)
    {
        _pointer = pointer;
        _length = length;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public NativeArray(System.IntPtr pointer, int length)
    {
        _pointer = pointer.ToPointer();
        _length = length;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void CopyTo(NativeArray<T> destination)
    {
        if ((uint)_length <= (uint)destination.Length)
        {
            long byteCount = _length * System.Runtime.InteropServices.Marshal.SizeOf<T>();
            Buffer.MemoryCopy(_pointer, (void*)destination.Pointer, byteCount, byteCount);
        }
        else
        {
            throw new ArgumentException("Destination is too short.", "destination");
        }
    }
}
