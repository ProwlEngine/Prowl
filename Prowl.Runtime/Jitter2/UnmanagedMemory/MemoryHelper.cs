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

using System.Runtime.InteropServices;

namespace Jitter2.UnmanagedMemory;

public static unsafe class MemoryHelper
{
    /// <summary>
    /// A block of 32 bytes of memory.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 16)]
    public struct MemBlock16
    {
    }

    /// <summary>
    /// A block of 32 bytes of memory.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 32)]
    public struct MemBlock32
    {
    }

    /// <summary>
    /// A block of 48 bytes of memory.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 48)]
    public struct MemBlock48
    {
    }

    /// <summary>
    /// A block of 64 bytes of memory.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 64)]
    public struct MemBlock64
    {
    }

    public static T* AllocateHeap<T>(int num) where T : unmanaged
    {
        return (T*)AllocateHeap(num * sizeof(T));
    }

    public static void Free<T>(T* ptr) where T : unmanaged
    {
        Free((void*)ptr);
    }

    public static void* AllocateHeap(int len) => NativeMemory.Alloc((nuint)len);
    public static void Free(void* ptr) => NativeMemory.Free(ptr);

    /// <summary>
    /// Zeros out unmanaged memory.
    /// </summary>
    public static void Memset(void* buffer, int len)
    {
        for (int i = 0; i < len; i++)
        {
            *((byte*)buffer + i) = 0;
        }
    }
}