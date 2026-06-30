using System;

namespace Prowl.Slang.NativeAPI;


/* An interface to provide a mechanism to cast, that doesn't require ref counting
and doesn't have to return a pointer to a IUnknown derived class */
[UUID(0x87ede0e1, 0x4852, 0x44b0, 0x8b, 0xf2, 0xcb, 0x31, 0x87, 0x4d, 0xe2, 0x39)]
public unsafe interface ISlangCastable : IUnknown
{
    /// Can be used to cast to interfaces without reference counting.
    /// Also provides access to internal implementations, when they provide a guid
    /// Can simulate a 'generated' interface as long as kept in scope by cast from.
    void* CastAs(ref Guid guid);
}