using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Prowl.Slang.Native;


public unsafe readonly ref struct U8Str
{
    private readonly ReadOnlySpan<byte> _span;
    public readonly byte* Pointer;

    public U8Str(ReadOnlySpan<byte> utf8)
    {
        Pointer = (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(utf8));
        _span = utf8;
    }

    public static implicit operator byte*(U8Str str)
    {
        return str.Pointer;
    }

    public ReadOnlySpan<byte> Span => _span;
}