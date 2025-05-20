using System;
using System.Collections.Generic;

using Prowl.Slang.Native;

using static Prowl.Slang.Native.SlangNative_Deprecated;


namespace Prowl.Slang;


/// <summary>
/// Provides reflection information for variables defined in a shader source module.
/// This struct acts as a managed wrapper around the native Slang variable reflection API.
/// </summary>
public unsafe struct VariableReflection
{
    internal ComponentType _component;
    internal Native.VariableReflection* _ptr;


    internal VariableReflection(Native.VariableReflection* ptr, ComponentType component)
    {
        ArgumentNullException.ThrowIfNull(ptr, nameof(ptr));

        _component = component;
        _ptr = ptr;
    }


    /// <summary>
    /// Gets the name of the variable.
    /// </summary>
    public readonly string Name =>
        spReflectionVariable_GetName(_ptr).String;

    /// <summary>
    /// Gets the type information for this variable.
    /// </summary>
    public readonly TypeReflection Type =>
        new(spReflectionVariable_GetType(_ptr), _component);

    /// <summary>
    /// Gets the number of user attributes attached to this variable.
    /// </summary>
    public readonly uint UserAttributeCount =>
        spReflectionVariable_GetUserAttributeCount(_ptr);

    /// <summary>
    /// Gets a user attribute by its index.
    /// </summary>
    /// <param name="index">The zero-based index of the attribute to retrieve.</param>
    /// <returns>The attribute at the specified index.</returns>
    public readonly Attribute GetUserAttributeByIndex(uint index) =>
        new(spReflectionVariable_GetUserAttribute(_ptr, index), _component);

    /// <summary>
    /// Gets all user attributes attached to this variable.
    /// </summary>
    public readonly IEnumerable<Attribute> UserAttributes =>
        Utility.For(UserAttributeCount, GetUserAttributeByIndex);

    /// <summary>
    /// Finds an attribute by its name.
    /// </summary>
    /// <param name="name">The name of the attribute to find.</param>
    /// <returns>The attribute with the specified name if found.</returns>
    public readonly Attribute FindAttributeByName(string name)
    {
        using U8Str str = U8Str.Alloc(name);
        return new(spReflectionVariable_FindUserAttributeByName(_ptr, (IGlobalSession*)((NativeComProxy)GlobalSession.s_session).ComPtr, str), _component);
    }

    /// <summary>
    /// Finds a user attribute by its name. This is an alias for <see cref="FindAttributeByName"/>.
    /// </summary>
    /// <param name="name">The name of the attribute to find.</param>
    /// <returns>The attribute with the specified name if found.</returns>
    public readonly Attribute FindUserAttributeByName(string name)
        => FindAttributeByName(name);

    /// <summary>
    /// Gets a value indicating whether this variable has a default value.
    /// </summary>
    public readonly bool HasDefaultValue =>
        spReflectionVariable_HasDefaultValue(_ptr);

    /// <summary>
    /// Gets the default value of this variable as an integer.
    /// </summary>
    /// <returns>The default integer value of this variable.</returns>
    public readonly long GetDefaultValueInt()
    {
        spReflectionVariable_GetDefaultValueInt(_ptr, out long value).Throw();
        return value;
    }

    /// <summary>
    /// Gets the generic container for this variable, if it is part of a generic context.
    /// </summary>
    public readonly GenericReflection GenericContainer =>
        new(spReflectionVariable_GetGenericContainer(_ptr), _component);

    /// <summary>
    /// Applies specializations from a generic reflection object to this variable.
    /// </summary>
    /// <param name="generic">The generic reflection containing specializations to apply.</param>
    /// <returns>A new variable reflection with the applied specializations.</returns>
    public readonly VariableReflection ApplySpecializations(GenericReflection generic) =>
        new(spReflectionVariable_applySpecializations(_ptr, generic._ptr), _component);
}
