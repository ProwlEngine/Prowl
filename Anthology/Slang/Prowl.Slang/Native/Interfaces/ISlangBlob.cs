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
