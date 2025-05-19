using System;
using System.Collections.Generic;

using Prowl.Slang.Native;

using static Prowl.Slang.Native.SlangNative_Deprecated;


namespace Prowl.Slang;


/// <summary>
/// Reflection information for a function defined in a module.
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
    /// The name of this function.
    /// </summary>
    public readonly string Name =>
        spReflectionFunction_GetName(_ptr).String;

    /// <summary>
    /// Type information for the return parameter of this function.
    /// </summary>
    public readonly TypeReflection ReturnType =>
        new(spReflectionFunction_GetResultType(_ptr), _component);

    /// <summary>
    /// Gets the total number of parameters defined in this function.
    /// </summary>
    public readonly uint ParameterCount =>
        spReflectionFunction_GetParameterCount(_ptr);

    /// <summary>
    /// Returns the <see cref="VariableReflection"/> in this function's parameter list at the given index.
    /// </summary>
    public readonly VariableReflection GetParameterByIndex(uint index) =>
        new(spReflectionFunction_GetParameter(_ptr, index), _component);

    /// <summary>
    /// Variable information of the parameters in this function.
    /// </summary>
    public readonly IEnumerable<VariableReflection> Parameters =>
        Utility.For(ParameterCount, GetParameterByIndex);

    /// <summary>
    /// Gets the total number of user attributes applied to this function.
    /// </summary>
    public readonly uint UserAttributeCount =>
        spReflectionFunction_GetUserAttributeCount(_ptr);

    /// <summary>
    /// Returns the <see cref="Attribute"/> applied on this function at the given index.
    /// </summary>
    public readonly Attribute GetUserAttributeByIndex(uint index) =>
        new(spReflectionFunction_GetUserAttribute(_ptr, index), _component);

    /// <summary>
    /// User attributes applied to this function.
    /// </summary>
    public readonly IEnumerable<Attribute> UserAttributes =>
        Utility.For(UserAttributeCount, GetUserAttributeByIndex);

    /// <summary>
    /// Finds an <see cref="Attribute"/> applied on this function by name.
    /// </summary>
    public readonly Attribute FindAttributeByName(string name)
    {
        using U8Str str = U8Str.Alloc(name);
        return new(spReflectionFunction_FindUserAttributeByName(_ptr, (IGlobalSession*)((NativeComProxy)GlobalSession.s_session).ComPtr, str), _component);
    }

    /// <summary>
    /// The generic container for this function.
    /// </summary>
    public readonly GenericReflection GenericContainer =>
        new(spReflectionFunction_GetGenericContainer(_ptr), _component);

    /// <summary>
    /// Create a concrete function definition for a generic function from generic reflection information.
    /// </summary>
    public readonly FunctionReflection ApplySpecializations(GenericReflection generic) =>
        new(spReflectionFunction_applySpecializations(_ptr, generic._ptr), _component);

    /// <summary>
    /// Create a concrete function defintion for a generic function from type information.
    /// </summary>
    public readonly FunctionReflection SpecializeWithArgTypes(TypeReflection[] types)
    {
        Native.TypeReflection** typesPtr = stackalloc Native.TypeReflection*[types.Length];

        for (int i = 0; i < types.Length; i++)
            typesPtr[i] = types[i]._ptr;

        return new(spReflectionFunction_specializeWithArgTypes(_ptr, types.Length, typesPtr), _component);
    }

    /// <summary>
    /// Indicates if this function has any defined overloads.
    /// </summary>
    public readonly bool IsOverloaded =>
        spReflectionFunction_isOverloaded(_ptr);

    /// <summary>
    /// Gets the total number of overloads defined for this function.
    /// </summary>
    public readonly uint OverloadCount =>
        spReflectionFunction_getOverloadCount(_ptr);

    /// <summary>
    /// Returns an overload from this function's overload list at the given index.
    /// </summary>
    public readonly FunctionReflection GetOverload(uint index) =>
        new(spReflectionFunction_getOverload(_ptr, index), _component);

    /// <summary>
    /// Overloads defined for this function.
    /// </summary>
    public readonly IEnumerable<FunctionReflection> Overloads =>
        Utility.For(OverloadCount, GetOverload);
}
