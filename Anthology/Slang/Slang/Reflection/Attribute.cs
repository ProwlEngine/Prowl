using System;

using Prowl.Slang.Native;

using static Prowl.Slang.Native.SlangNative_Deprecated;


namespace Prowl.Slang;


/// <summary>
/// Reflection information for an attribute defined in source code.
/// </summary>
public unsafe struct Attribute
{
    internal ComponentType _component;
    internal Native.Attribute* _ptr;


    internal Attribute(Native.Attribute* ptr, ComponentType component)
    {
        ArgumentNullException.ThrowIfNull(ptr, nameof(ptr));

        _component = component;
        _ptr = ptr;
    }


    /// <summary>
    /// The name of this <see cref="Attribute"/>.
    /// </summary>
    public readonly string Name =>
        spReflectionUserAttribute_GetName(_ptr).String;

    /// <summary>
    /// The number of arguments in this <see cref="Attribute"/>.
    /// </summary>
    public readonly uint ArgumentCount =>
        spReflectionUserAttribute_GetArgumentCount(_ptr);

    /// <summary>
    /// Gets the type reflection info of the argument at the given index.
    /// </summary>
    public readonly TypeReflection GetArgumentType(uint index) =>
        new(spReflectionUserAttribute_GetArgumentType(_ptr, index), _component);


    /// <summary>
    /// Gets the constant int value of the argument at the given index.
    /// </summary>
    public readonly int? GetArgumentValueInt(uint index)
    {
        SlangResult result = spReflectionUserAttribute_GetArgumentValueInt(_ptr, index, out int value);

        if (!result.IsOk())
            return null;

        return value;
    }


    /// <summary>
    /// Gets the constant float value of the argument at the given index.
    /// </summary>
    public readonly float? GetArgumentValueFloat(uint index)
    {
        SlangResult result = spReflectionUserAttribute_GetArgumentValueFloat(_ptr, index, out float value);

        if (!result.IsOk())
            return null;

        return value;
    }

    /// <summary>
    /// Gets the constant string value of the argument at the given index.
    /// </summary>
    public readonly string? GetArgumentValueString(uint index)
    {
        ConstU8Str str = spReflectionUserAttribute_GetArgumentValueString(_ptr, index, out nuint outSize);

        if (outSize == 0)
            return null;

        return System.Text.Encoding.UTF8.GetString(str.Data, (int)outSize);
    }
}
