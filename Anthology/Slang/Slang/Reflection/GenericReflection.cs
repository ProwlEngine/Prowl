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


    public readonly DeclReflection AsDecl() =>
        new(spReflectionGeneric_asDecl(_ptr), _session);

    public readonly string Name =>
        spReflectionGeneric_GetName(_ptr).String;

    public readonly uint TypeParameterCount =>
        spReflectionGeneric_GetTypeParameterCount(_ptr);

    public readonly VariableReflection GetTypeParameter(uint index) =>
        new(spReflectionGeneric_GetTypeParameter(_ptr, index), _session);

    public IEnumerable<VariableReflection> TypeParameters =>
        Utility.For(TypeParameterCount, GetTypeParameter);

    public readonly uint ValueParameterCount =>
        spReflectionGeneric_GetValueParameterCount(_ptr);

    public readonly VariableReflection GetValueParameter(uint index) =>
        new(spReflectionGeneric_GetValueParameter(_ptr, index), _session);

    public IEnumerable<VariableReflection> ValueParameters =>
        Utility.For(ValueParameterCount, GetValueParameter);

    public readonly uint GetTypeParameterConstraintCount(VariableReflection typeParam) =>
        spReflectionGeneric_GetTypeParameterConstraintCount(_ptr, typeParam._ptr);

    public readonly TypeReflection GetTypeParameterConstraintType(VariableReflection typeParam, uint index) =>
        new(spReflectionGeneric_GetTypeParameterConstraintType(_ptr, typeParam._ptr, index), _session);

    public readonly DeclReflection InnerDecl =>
        new(spReflectionGeneric_GetInnerDecl(_ptr), _session);

    public readonly SlangDeclKind InnerKind =>
        spReflectionGeneric_GetInnerKind(_ptr);

    public readonly GenericReflection OuterGenericContainer =>
        new(spReflectionGeneric_GetOuterGenericContainer(_ptr), _session);

    public readonly TypeReflection GetConcreteType(VariableReflection typeParam) =>
        new(spReflectionGeneric_GetConcreteType(_ptr, typeParam._ptr), _session);

    public readonly long GetConcreteIntVal(VariableReflection valueParam) =>
        spReflectionGeneric_GetConcreteIntVal(_ptr, valueParam._ptr);

    public readonly GenericReflection ApplySpecializations(GenericReflection generic) =>
        new(spReflectionGeneric_applySpecializations(_ptr, generic._ptr), _session);
};
