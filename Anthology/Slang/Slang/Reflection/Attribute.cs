using System;

using Prowl.Slang.Native;

using static Prowl.Slang.Native.SlangNative_Deprecated;


namespace Prowl.Slang;


/// <summary>
/// Represents a user-defined attribute in a shader module, typically declared using [attribute] syntax.
/// Provides access to attribute metadata and typed argument values.
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
    /// Gets the name of the attribute.
    /// </summary>
    public readonly string Name =>
        spReflectionUserAttribute_GetName(_ptr).String;

    /// <summary>
    /// Gets the number of arguments provided to the attribute in the shader code.
    /// </summary>
    public readonly uint ArgumentCount =>
        spReflectionUserAttribute_GetArgumentCount(_ptr);

    /// <summary>
    /// Gets the type information for a specific argument of the attribute.
    /// </summary>
    /// <param name="index">The zero-based index of the argument to query.</param>
    /// <returns>A <see cref="TypeReflection"/> instance representing the argument type.</returns>
    public readonly TypeReflection GetArgumentType(uint index) =>
        new(spReflectionUserAttribute_GetArgumentType(_ptr, index), _component);

    /// <summary>
    /// Retrieves the value of an attribute argument as an integer.
    /// </summary>
    /// <param name="index">The zero-based index of the argument to retrieve.</param>
    /// <returns>The integer value of the argument, or null if the argument cannot be interpreted as an integer.</returns>
    public readonly int? GetArgumentValueInt(uint index)
    {
        SlangResult result = spReflectionUserAttribute_GetArgumentValueInt(_ptr, index, out int value);
        if (!result.IsOk())
            return null;
        return value;
    }

    /// <summary>
    /// Retrieves the value of an attribute argument as a floating-point number.
    /// </summary>
    /// <param name="index">The zero-based index of the argument to retrieve.</param>
    /// <returns>The floating-point value of the argument, or null if the argument cannot be interpreted as a float.</returns>
    public readonly float? GetArgumentValueFloat(uint index)
    {
        SlangResult result = spReflectionUserAttribute_GetArgumentValueFloat(_ptr, index, out float value);
        if (!result.IsOk())
            return null;
        return value;
    }

    /// <summary>
    /// Retrieves the value of an attribute argument as a string.
    /// </summary>
    /// <param name="index">The zero-based index of the argument to retrieve.</param>
    /// <returns>The string value of the argument, or null if the argument cannot be interpreted as a string.</returns>
    public readonly string? GetArgumentValueString(uint index)
    {
        ConstU8Str str = spReflectionUserAttribute_GetArgumentValueString(_ptr, index, out nuint outSize);
        if (outSize == 0)
            return null;
        return System.Text.Encoding.UTF8.GetString(str.Data, (int)outSize);
    }
}
