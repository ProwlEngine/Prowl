// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Slang.Native;

using static Prowl.Slang.Native.SlangNative_Deprecated;


namespace Prowl.Slang;


/// <summary>
/// Represents reflection information for a function defined in a shader source module.
/// Provides access to function metadata including name, parameters, return type, and attributes.
/// </summary>
public unsafe struct FunctionReflection
{
    internal ComponentType _component;
    internal Native.FunctionReflection* _ptr;


    internal FunctionReflection(Native.FunctionReflection* ptr, ComponentType component)
    {
        ArgumentNullException.ThrowIfNull(ptr, nameof(ptr));

        _component = component;
        _ptr = ptr;
    }

    /// <summary>
    /// Gets the name of the function.
    /// </summary>
    public readonly string Name =>
        spReflectionFunction_GetName(_ptr).String;

    /// <summary>
    /// Gets the return type of the function.
    /// </summary>
    public readonly TypeReflection ReturnType =>
        new(spReflectionFunction_GetResultType(_ptr), _component);

    /// <summary>
    /// Gets the number of parameters this function accepts.
    /// </summary>
    public readonly uint ParameterCount =>
        spReflectionFunction_GetParameterCount(_ptr);

    /// <summary>
    /// Gets a parameter by its index.
    /// </summary>
    /// <param name="index">The zero-based index of the parameter to retrieve.</param>
    /// <returns>Reflection information for the specified parameter.</returns>
    public readonly VariableReflection GetParameterByIndex(uint index) =>
        new(spReflectionFunction_GetParameter(_ptr, index), _component);

    /// <summary>
    /// Gets an enumerable collection of all parameters for this function.
    /// </summary>
    public readonly IEnumerable<VariableReflection> Parameters =>
        Utility.For(ParameterCount, GetParameterByIndex);

    /// <summary>
    /// Gets the number of user attributes defined on this function.
    /// </summary>
    public readonly uint UserAttributeCount =>
        spReflectionFunction_GetUserAttributeCount(_ptr);

    /// <summary>
    /// Gets a user attribute by its index.
    /// </summary>
    /// <param name="index">The zero-based index of the attribute to retrieve.</param>
    /// <returns>The specified attribute.</returns>
    public readonly Attribute GetUserAttributeByIndex(uint index) =>
        new(spReflectionFunction_GetUserAttribute(_ptr, index), _component);

    /// <summary>
    /// Gets an enumerable collection of all user attributes defined on this function.
    /// </summary>
    public readonly IEnumerable<Attribute> UserAttributes =>
        Utility.For(UserAttributeCount, GetUserAttributeByIndex);

    /// <summary>
    /// Finds a user attribute by name.
    /// </summary>
    /// <param name="name">The name of the attribute to find.</param>
    /// <returns>The attribute with the specified name if found.</returns>
    public readonly Attribute FindAttributeByName(string name)
    {
        using U8Str str = U8Str.Alloc(name);
        return new(spReflectionFunction_FindUserAttributeByName(_ptr, (IGlobalSession*)((NativeComProxy)GlobalSession.s_session).ComPtr, str), _component);
    }

    /// <summary>
    /// Gets the generic container for this function, if it is a generic function.
    /// </summary>
    public readonly GenericReflection GenericContainer =>
        new(spReflectionFunction_GetGenericContainer(_ptr), _component);

    /// <summary>
    /// Applies specializations to a generic function.
    /// </summary>
    /// <param name="generic">The generic specialization to apply.</param>
    /// <returns>A specialized version of this function.</returns>
    public readonly FunctionReflection ApplySpecializations(GenericReflection generic) =>
        new(spReflectionFunction_applySpecializations(_ptr, generic._ptr), _component);

    /// <summary>
    /// Specializes this function with the specified argument types.
    /// </summary>
    /// <param name="types">An array of types to use for specialization.</param>
    /// <returns>A specialized version of this function.</returns>
    public readonly FunctionReflection SpecializeWithArgTypes(TypeReflection[] types)
    {
        Native.TypeReflection** typesPtr = stackalloc Native.TypeReflection*[types.Length];

        for (int i = 0; i < types.Length; i++)
            typesPtr[i] = types[i]._ptr;

        return new(spReflectionFunction_specializeWithArgTypes(_ptr, types.Length, typesPtr), _component);
    }

    /// <summary>
    /// Gets a value indicating whether this function is overloaded.
    /// </summary>
    public readonly bool IsOverloaded =>
        spReflectionFunction_isOverloaded(_ptr);

    /// <summary>
    /// Gets the number of overloads available for this function.
    /// </summary>
    public readonly uint OverloadCount =>
        spReflectionFunction_getOverloadCount(_ptr);

    /// <summary>
    /// Gets a specific overload of this function by index.
    /// </summary>
    /// <param name="index">The zero-based index of the overload to retrieve.</param>
    /// <returns>The specified function overload.</returns>
    public readonly FunctionReflection GetOverload(uint index) =>
        new(spReflectionFunction_getOverload(_ptr, index), _component);

    /// <summary>
    /// Gets an enumerable collection of all overloads for this function.
    /// </summary>
    public readonly IEnumerable<FunctionReflection> Overloads =>
        Utility.For(OverloadCount, GetOverload);
}
