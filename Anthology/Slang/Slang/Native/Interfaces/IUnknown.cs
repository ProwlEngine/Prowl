// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Slang.Native;


[UUID(0x00000000, 0x0000, 0x0000, 0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46)]
internal interface IUnknown
{
    public unsafe SlangResult QueryInterface(ref Guid uuid, out IntPtr obj);

    public unsafe uint AddRef();

    public unsafe uint Release();
}
