using System;

using Prowl.Slang.Native;

using static Prowl.Slang.Native.SlangNative_Deprecated;


namespace Prowl.Slang;


public unsafe struct Attribute
{
    internal Session _session;
    internal Native.Attribute* _ptr;


    internal Attribute(Native.Attribute* ptr, Session session)
    {
        ArgumentNullException.ThrowIfNull(ptr, nameof(ptr));

        _session = session;
        _ptr = ptr;
    }


    public readonly string Name =>
        spReflectionUserAttribute_GetName(_ptr).String;

    public readonly uint ArgumentCount =>
        spReflectionUserAttribute_GetArgumentCount(_ptr);

    public readonly TypeReflection GetArgumentType(uint index) =>
        new(spReflectionUserAttribute_GetArgumentType(_ptr, index), _session);

    public readonly int? GetArgumentValueInt(uint index)
    {
        SlangResult result = spReflectionUserAttribute_GetArgumentValueInt(_ptr, index, out int value);

        if (!result.IsOk())
            return null;

        return value;
    }

    public readonly float? GetArgumentValueFloat(uint index)
    {
        SlangResult result = spReflectionUserAttribute_GetArgumentValueFloat(_ptr, index, out float value);

        if (!result.IsOk())
            return null;

        return value;
    }

    public readonly string? GetArgumentValueString(uint index)
    {
        ConstU8Str str = spReflectionUserAttribute_GetArgumentValueString(_ptr, index, out nuint outSize);

        if (outSize == 0)
            return null;

        return System.Text.Encoding.UTF8.GetString(str.Data, (int)outSize);
    }
}