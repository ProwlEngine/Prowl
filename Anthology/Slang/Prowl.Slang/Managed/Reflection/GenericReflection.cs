using System;
using System.Collections.Generic;

using Prowl.Slang.Native;

using static Prowl.Slang.Native.SlangNative_Deprecated;


namespace Prowl.Slang;


public unsafe struct GenericReflection
{
    internal Session _session;
    internal Native.GenericReflection* _ptr;


    internal GenericReflection(Native.GenericReflection* ptr, Session session)
    {
        ArgumentNullException.ThrowIfNull(ptr, nameof(ptr));

        _session = session;
        _ptr = ptr;
    }


    public DeclReflection AsDecl() =>
        new(spReflectionGeneric_asDecl(_ptr), _session);

    public string Name =>
        spReflectionGeneric_GetName(_ptr).String;

    public uint TypeParameterCount =>
        spReflectionGeneric_GetTypeParameterCount(_ptr);

    public VariableReflection GetTypeParameter(uint index) =>
        new(spReflectionGeneric_GetTypeParameter(_ptr, index), _session);

    public IEnumerable<VariableReflection> TypeParameters =>
        Utility.For(TypeParameterCount, GetTypeParameter);

    public uint ValueParameterCount =>
        spReflectionGeneric_GetValueParameterCount(_ptr);

    public VariableReflection GetValueParameter(uint index) =>
        new(spReflectionGeneric_GetValueParameter(_ptr, index), _session);

    public IEnumerable<VariableReflection> ValueParameters =>
        Utility.For(ValueParameterCount, GetValueParameter);

    public uint GetTypeParameterConstraintCount(VariableReflection typeParam) =>
        spReflectionGeneric_GetTypeParameterConstraintCount(_ptr, typeParam._ptr);

    public TypeReflection GetTypeParameterConstraintType(VariableReflection typeParam, uint index) =>
        new(spReflectionGeneric_GetTypeParameterConstraintType(_ptr, typeParam._ptr, index), _session);

    public DeclReflection InnerDecl =>
        new(spReflectionGeneric_GetInnerDecl(_ptr), _session);

    public SlangDeclKind InnerKind =>
        spReflectionGeneric_GetInnerKind(_ptr);

    public GenericReflection OuterGenericContainer =>
        new(spReflectionGeneric_GetOuterGenericContainer(_ptr), _session);

    public TypeReflection GetConcreteType(VariableReflection typeParam) =>
        new(spReflectionGeneric_GetConcreteType(_ptr, typeParam._ptr), _session);

    public long GetConcreteIntVal(VariableReflection valueParam) =>
        spReflectionGeneric_GetConcreteIntVal(_ptr, valueParam._ptr);

    public GenericReflection ApplySpecializations(GenericReflection generic) =>
        new(spReflectionGeneric_applySpecializations(_ptr, generic._ptr), _session);
};
