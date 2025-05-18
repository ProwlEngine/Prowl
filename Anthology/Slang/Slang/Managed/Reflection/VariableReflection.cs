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


    public string Name =>
        spReflectionVariable_GetName(_ptr).String;

    public TypeReflection Type =>
        new(spReflectionVariable_GetType(_ptr), _session);

    public Modifier FindModifier(SlangModifierID id) =>
        new(spReflectionVariable_FindModifier(_ptr, id), _session);

    public uint UserAttributeCount =>
        spReflectionVariable_GetUserAttributeCount(_ptr);

    public Attribute GetUserAttributeByIndex(uint index) =>
        new(spReflectionVariable_GetUserAttribute(_ptr, index), _session);

    public IEnumerable<Attribute> UserAttributes =>
        Utility.For(UserAttributeCount, GetUserAttributeByIndex);

    public Attribute FindAttributeByName(IGlobalSession* globalSession, string name)
    {
        using U8Str str = U8Str.Alloc(name);
        return new(spReflectionVariable_FindUserAttributeByName(_ptr, globalSession, str), _session);
    }

    public Attribute FindUserAttributeByName(IGlobalSession* globalSession, string name)
        => FindAttributeByName(globalSession, name);

    public bool HasDefaultValue =>
        spReflectionVariable_HasDefaultValue(_ptr);

    public long GetDefaultValueInt()
    {
        spReflectionVariable_GetDefaultValueInt(_ptr, out long value).Throw();
        return value;
    }

    public GenericReflection GenericContainer =>
        new(spReflectionVariable_GetGenericContainer(_ptr), _session);

    public VariableReflection ApplySpecializations(GenericReflection generic) =>
        new(spReflectionVariable_applySpecializations(_ptr, generic._ptr), _session);
};
