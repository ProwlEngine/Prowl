using System;
using System.Collections.Generic;
using System.Reflection;

namespace Prowl.Slang.Native;


[AttributeUsage(AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
public class UUIDAttribute : System.Attribute
{
    private static Dictionary<Type, Guid> s_typeCache = [];


    public Guid UUID;

    public UUIDAttribute(uint a, ushort b, ushort c, byte d, byte e, byte f, byte g, byte h, byte i, byte j, byte k)
    {
        UUID = new(a, b, c, d, e, f, g, h, i, j, k);
    }


    public static Guid GetGuid<T>()
    {
        return GetGuid(typeof(T));
    }


    public static Guid GetGuid(Type type)
    {
        if (!s_typeCache.TryGetValue(type, out Guid guid))
            s_typeCache[type] = guid = type.GetCustomAttribute<UUIDAttribute>()?.UUID ?? Guid.Empty;

        return guid;
    }
}
