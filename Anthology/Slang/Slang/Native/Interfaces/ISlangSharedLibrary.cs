// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Slang.Native;


/** An interface that can be used to encapsulate access to a shared library. An implementation
does not have to implement the library as a shared library
*/
[UUID(0x70dbc7c4, 0xdc3b, 0x4a07, 0xae, 0x7e, 0x75, 0x2a, 0xf6, 0xa8, 0x15, 0x55)]
internal unsafe interface ISlangSharedLibrary : ISlangCastable
{
    /** Get a symbol by name. If the library is unloaded will only return null.
    @param name The name of the symbol
    @return The pointer related to the name or null if not found
    */
    void* FindSymbolAddressByName(ConstU8Str name);
}
