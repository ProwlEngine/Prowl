// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Slang.Native;


namespace Prowl.Slang;


internal unsafe class SharedLibrary
{
    internal ISlangSharedLibrary _library;


    internal SharedLibrary(ISlangSharedLibrary library)
    {
        _library = library;
    }
}
