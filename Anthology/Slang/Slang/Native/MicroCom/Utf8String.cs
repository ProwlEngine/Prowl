using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Prowl.Slang.Native;


internal readonly unsafe struct U8Str : IDisposable
{
    public readonly byte* Pointer;
    public readonly int Length;


    public static U8Str Alloc(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        int byteCount = Encoding.UTF8.GetByteCount(text);

        byte* bytes = (byte*)NativeMemory.Alloc((nuint)byteCount + 1);
        if (bytes == null)
        {
            throw new OutOfMemoryException("Failed to allocate native memory for UTF-8 string.");
        }

        fixed (char* chars = text)
        {
            Encoding.UTF8.GetBytes(chars, text.Length, bytes, byteCount);
        }

        bytes[byteCount] = 0;

        return new U8Str(bytes, byteCount);
    }


    public static void Free(U8Str val)
    {
        if (val.Pointer != null)
        {
            NativeMemory.Free(val.Pointer);
        }
    }


    public void Dispose()
    {
        Free(this);
    }


    internal U8Str(byte* utf8, int len)
    {
        Pointer = utf8;
        Length = len;
    }


    public U8Str(ReadOnlySpan<byte> utf8)
    {
        Pointer = (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(utf8));
        Length = utf8.Length;
    }


    public static implicit operator byte*(U8Str str)
    {
        return str.Pointer;
    }


    public string String => Pointer == null ? "" : (Marshal.PtrToStringUTF8((IntPtr)Pointer) ?? "");
}


[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ConstU8Str
{
    public byte* Data;


    public static implicit operator ConstU8Str(U8Str str)
    {
        return new ConstU8Str { Data = str.Pointer };
    }


    public string String => Data == null ? "" : (Marshal.PtrToStringUTF8((IntPtr)Data) ?? "");
}
