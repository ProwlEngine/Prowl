using System;

namespace Prowl.Slang.Native;


[AttributeUsage(AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
public class UUIDAttribute : System.Attribute
{
    public Guid UUID;

    public UUIDAttribute(uint a, ushort b, ushort c, byte d, byte e, byte f, byte g, byte h, byte i, byte j, byte k)
    {
        UUID = new(a, b, c, d, e, f, g, h, i, j, k);
    }
}