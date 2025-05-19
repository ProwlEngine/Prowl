using System;
using System.Collections.Generic;

using Prowl.Slang.Native;

using static Prowl.Slang.Native.SlangNative_Deprecated;


namespace Prowl.Slang;


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


    public readonly string Name =>
        spReflectionFunction_GetName(_ptr).String;

    public readonly TypeReflection ReturnType =>
        new(spReflectionFunction_GetResultType(_ptr), _component);

    public readonly uint ParameterCount =>
        spReflectionFunction_GetParameterCount(_ptr);

    public readonly VariableReflection GetParameterByIndex(uint index) =>
        new(spReflectionFunction_GetParameter(_ptr, index), _component);

    public readonly IEnumerable<VariableReflection> Parameters =>
        Utility.For(ParameterCount, GetParameterByIndex);

    public readonly uint UserAttributeCount =>
        spReflectionFunction_GetUserAttributeCount(_ptr);

    public readonly Attribute GetUserAttributeByIndex(uint index) =>
        new(spReflectionFunction_GetUserAttribute(_ptr, index), _component);

    public readonly IEnumerable<Attribute> UserAttributes =>
    Utility.For(UserAttributeCount, GetUserAttributeByIndex);

    public readonly Attribute FindAttributeByName(string name)
    {
        using U8Str str = U8Str.Alloc(name);
        return new(spReflectionFunction_FindUserAttributeByName(_ptr, (IGlobalSession*)((NativeComProxy)GlobalSession.s_session).ComPtr, str), _component);
    }

    public readonly Attribute FindUserAttributeByName(string name) =>
        FindAttributeByName(name);

    public readonly Modifier FindModifier(SlangModifierID id) =>
        new(spReflectionFunction_FindModifier(_ptr, id), _component);

    public readonly GenericReflection GenericContainer =>
        new(spReflectionFunction_GetGenericContainer(_ptr), _component);

    public readonly FunctionReflection ApplySpecializations(GenericReflection generic) =>
        new(spReflectionFunction_applySpecializations(_ptr, generic._ptr), _component);

    public readonly FunctionReflection SpecializeWithArgTypes(TypeReflection[] types)
    {
        Native.TypeReflection** typesPtr = stackalloc Native.TypeReflection*[types.Length];

        for (int i = 0; i < types.Length; i++)
            typesPtr[i] = types[i]._ptr;

        return new(spReflectionFunction_specializeWithArgTypes(_ptr, types.Length, typesPtr), _component);
    }

    public readonly bool IsOverloaded =>
        spReflectionFunction_isOverloaded(_ptr);

    public readonly uint OverloadCount =>
        spReflectionFunction_getOverloadCount(_ptr);

    public readonly FunctionReflection GetOverload(uint index) =>
        new(spReflectionFunction_getOverload(_ptr, index), _component);

    public readonly IEnumerable<FunctionReflection> Overloads =>
        Utility.For(OverloadCount, GetOverload);
};
