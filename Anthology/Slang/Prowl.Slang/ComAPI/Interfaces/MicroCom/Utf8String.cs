using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Prowl.Slang.Native;


public readonly unsafe struct U8Str
{
    public readonly byte* Pointer;
    public readonly int Length;

    public static U8Str Alloc(string text)
    {
        fixed (char* chars = text)
        {
            int byteCount = Encoding.UTF8.GetByteCount(chars, text.Length);

            byte* bytes = (byte*)NativeMemory.Alloc((nuint)byteCount);

            Encoding.UTF8.GetBytes(chars, text.Length, bytes, byteCount);

            return new U8Str(bytes, byteCount);
        }
    }


    public static void Free(U8Str val)
    {
        NativeMemory.Free(val.Pointer);
    }


    public U8Str(byte* utf8, int len)
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

    public string String => Marshal.PtrToStringUTF8((nint)Pointer) ?? "";
}


[StructLayout(LayoutKind.Sequential)]
public unsafe struct ConstU8Str
{
    public byte* Data;


    public static implicit operator ConstU8Str(U8Str str)
    {
        return new ConstU8Str { Data = str.Pointer };
    }


    public string String => Marshal.PtrToStringUTF8((nint)Data) ?? "";
}
