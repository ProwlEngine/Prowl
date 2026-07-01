// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Slang.Native;


[UUID(0x1ec36168, 0xe9f4, 0x430d, 0xbb, 0x17, 0x4, 0x8a, 0x80, 0x46, 0xb3, 0x1f)]
internal unsafe interface ISlangClonable : ISlangCastable
{
    void* Clone(ref Guid guid);
}
