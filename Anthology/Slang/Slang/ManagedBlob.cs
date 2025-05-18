using System;
using System.Runtime.InteropServices;

using Prowl.Slang.Native;


namespace Prowl.Slang;


internal unsafe class ManagedBlob : ManagedComProxy<ISlangBlob>, ISlangBlob
{
    public static ManagedBlob FromMemory(Memory<byte> source)
    {
        ManagedBlob blob = new()
        {
            Bytes = NativeUtility.Alloc<byte>(source.Length),
            Length = (nuint)source.Length,
            NeedsFree = true,
        };

        fixed (byte* memoryPtr = source.Span)
            NativeMemory.Copy(memoryPtr, blob.Bytes, (nuint)source.Length);

        return blob;
    }


    public byte* Bytes;
    public nuint Length;
    public bool NeedsFree = false;

    public void* GetBufferPointer()
    {
        return Bytes;
    }

    public nuint GetBufferSize()
    {
        return Length;
    }

    ~ManagedBlob()
    {
        if (NeedsFree)
            NativeMemory.Free(Bytes);
    }
}