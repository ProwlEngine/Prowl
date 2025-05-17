using System;
using System.Runtime.InteropServices;

namespace Prowl.Slang.Native;


/** A "blob" of binary data.

This interface definition is compatible with the `ID3DBlob` and `ID3D10Blob` interfaces.
*/
[UUID(0x8BA5FB08, 0x5195, 0x40e2, 0xAC, 0x58, 0x0D, 0x98, 0x9C, 0x3A, 0x01, 0x02)]
public unsafe interface ISlangBlob : IUnknown
{
    void* GetBufferPointer();
    nuint GetBufferSize();
}


public static class BlobExtensions
{
    public static unsafe string GetString(this ISlangBlob blob)
    {
        return System.Text.Encoding.UTF8.GetString((byte*)blob.GetBufferPointer(), (int)blob.GetBufferSize());
    }


    public static unsafe Memory<byte> ReadBytes(this ISlangBlob blob)
    {
        byte[] bytes = new byte[blob.GetBufferSize()];

        fixed (byte* bytePtr = bytes)
            NativeMemory.Copy(blob.GetBufferPointer(), bytePtr, (nuint)bytes.Length);

        return bytes;
    }
}