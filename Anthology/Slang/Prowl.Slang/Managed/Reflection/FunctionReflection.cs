using System;
using System.Collections.Generic;

using Prowl.Slang.Native;

using static Prowl.Slang.Native.SlangNative_Deprecated;


namespace Prowl.Slang;


public unsafe struct FunctionReflection
{
    internal Session _session;
    internal Native.FunctionReflection* _ptr;


    internal FunctionReflection(Native.FunctionReflection* ptr, Session session)
    {
        ArgumentNullException.ThrowIfNull(ptr, nameof(ptr));

        _session = session;
        _ptr = ptr;
    }


    public string Name =>
        spReflectionFunction_GetName(_ptr).String;

    public TypeReflection ReturnType =>
        new(spReflectionFunction_GetResultType(_ptr), _session);

    public uint ParameterCount =>
        spReflectionFunction_GetParameterCount(_ptr);

    public VariableReflection GetParameterByIndex(uint index) =>
        new(spReflectionFunction_GetParameter(_ptr, index), _session);

    public IEnumerable<VariableReflection> Parameters =>
        Utility.For(ParameterCount, GetParameterByIndex);

    public uint UserAttributeCount =>
        spReflectionFunction_GetUserAttributeCount(_ptr);

    public Attribute GetUserAttributeByIndex(uint index) =>
        new(spReflectionFunction_GetUserAttribute(_ptr, index), _session);

    public IEnumerable<Attribute> UserAttributes =>
    Utility.For(UserAttributeCount, GetUserAttributeByIndex);

    public Attribute FindAttributeByName(string name)
    {
        using U8Str str = U8Str.Alloc(name);
        return new(spReflectionFunction_FindUserAttributeByName(_ptr, (IGlobalSession*)((NativeComProxy)GlobalSession.s_session).ComPtr, str), _session);
    }

    public Attribute FindUserAttributeByName(string name) =>
        FindAttributeByName(name);

    public Modifier FindModifier(SlangModifierID id) =>
        new(spReflectionFunction_FindModifier(_ptr, id), _session);

    public GenericReflection GenericContainer =>
        new(spReflectionFunction_GetGenericContainer(_ptr), _session);

    public FunctionReflection ApplySpecializations(GenericReflection generic) =>
        new(spReflectionFunction_applySpecializations(_ptr, generic._ptr), _session);

    public FunctionReflection SpecializeWithArgTypes(TypeReflection[] types)
    {
        Native.TypeReflection** typesPtr = stackalloc Native.TypeReflection*[types.Length];

        for (int i = 0; i < types.Length; i++)
            typesPtr[i] = types[i]._ptr;

        return new(spReflectionFunction_specializeWithArgTypes(_ptr, types.Length, typesPtr), _session);
    }

    public bool IsOverloaded =>
        spReflectionFunction_isOverloaded(_ptr);

    public uint OverloadCount =>
        spReflectionFunction_getOverloadCount(_ptr);

    public FunctionReflection GetOverload(uint index) =>
        new(spReflectionFunction_getOverload(_ptr, index), _session);

    public IEnumerable<FunctionReflection> Overloads =>
        Utility.For(OverloadCount, GetOverload);
};
