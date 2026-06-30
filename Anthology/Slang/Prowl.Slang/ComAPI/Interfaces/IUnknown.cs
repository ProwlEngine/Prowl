using System;

namespace Prowl.Slang.NativeAPI;


[UUID(0x00000000, 0x0000, 0x0000, 0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46)]
public interface IUnknown
{
    public unsafe SlangResult QueryInterface(ref Guid uuid, out IntPtr obj);

    public unsafe uint AddRef();

    public unsafe uint Release();
}
