// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Slang.Native;


[AttributeUsage(AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
internal class UUIDAttribute : System.Attribute
{
    public Guid UUID;


    public UUIDAttribute(uint a, ushort b, ushort c, byte d, byte e, byte f, byte g, byte h, byte i, byte j, byte k)
    {
        UUID = new(a, b, c, d, e, f, g, h, i, j, k);
    }
}
