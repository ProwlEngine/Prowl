using System;
using System.Collections.Generic;

using Prowl.Slang.Native;

using static Prowl.Slang.Native.SlangNative_Deprecated;


namespace Prowl.Slang;


public unsafe struct VariableReflection
{
    internal Session _session;
    internal Native.VariableReflection* _ptr;


    internal VariableReflection(Native.VariableReflection* ptr, Session session)
    {
        ArgumentNullException.ThrowIfNull(ptr, nameof(ptr));

        _session = session;
        _ptr = ptr;
    }


    public readonly string Name =>
        spReflectionVariable_GetName(_ptr).String;

    public readonly TypeReflection Type =>
        new(spReflectionVariable_GetType(_ptr), _session);

    public readonly Modifier FindModifier(SlangModifierID id) =>
        new(spReflectionVariable_FindModifier(_ptr, id), _session);

    public readonly uint UserAttributeCount =>
        spReflectionVariable_GetUserAttributeCount(_ptr);

    public readonly Attribute GetUserAttributeByIndex(uint index) =>
        new(spReflectionVariable_GetUserAttribute(_ptr, index), _session);

    public readonly IEnumerable<Attribute> UserAttributes =>
        Utility.For(UserAttributeCount, GetUserAttributeByIndex);

    public readonly Attribute FindAttributeByName(string name)
    {
        using U8Str str = U8Str.Alloc(name);
        return new(spReflectionVariable_FindUserAttributeByName(_ptr, (IGlobalSession*)((NativeComProxy)GlobalSession.s_session).ComPtr, str), _session);
    }

    public readonly Attribute FindUserAttributeByName(string name)
        => FindAttributeByName(name);

    public readonly bool HasDefaultValue =>
        spReflectionVariable_HasDefaultValue(_ptr);

    public readonly long GetDefaultValueInt()
    {
        spReflectionVariable_GetDefaultValueInt(_ptr, out long value).Throw();
        return value;
    }

    public readonly GenericReflection GenericContainer =>
        new(spReflectionVariable_GetGenericContainer(_ptr), _session);

    public readonly VariableReflection ApplySpecializations(GenericReflection generic) =>
        new(spReflectionVariable_applySpecializations(_ptr, generic._ptr), _session);
};
