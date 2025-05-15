using System;

namespace Prowl.Slang.NativeAPI;


[UUID(0x1ec36168, 0xe9f4, 0x430d, 0xbb, 0x17, 0x4, 0x8a, 0x80, 0x46, 0xb3, 0x1f)]
public unsafe interface ISlangClonable : ISlangCastable
{
    /// Note the use of guid is for the desired interface/object.
    /// The object is returned *not* ref counted. Any type that can implements the interface,
    /// derives from ICastable, and so (not withstanding some other issue) will always return
    /// an ICastable interface which other interfaces/types are accessible from via castAs
    void* Clone(ref Guid guid);
}
