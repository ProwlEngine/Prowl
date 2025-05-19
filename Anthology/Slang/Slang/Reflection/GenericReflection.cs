using System;
using System.Collections.Generic;

using static Prowl.Slang.Native.SlangNative_Deprecated;


namespace Prowl.Slang;


/// <summary>
/// Reflection information for a generic type.
/// </summary>
public unsafe struct GenericReflection
{
    internal ComponentType _component;
    internal Native.GenericReflection* _ptr;


    internal GenericReflection(Native.GenericReflection* ptr, ComponentType component)
    {
        ArgumentNullException.ThrowIfNull(ptr, nameof(ptr));

        _component = component;
        _ptr = ptr;
    }


    /// <summary>
    /// Gets this <see cref="GenericReflection"/> instance as a <see cref="DeclReflection"/> instance.
    /// </summary>
    public readonly DeclReflection AsDecl() =>
        new(spReflectionGeneric_asDecl(_ptr), _component);

    /// <summary>
    /// The name of this <see cref="GenericReflection"/>.
    /// </summary>
    public readonly string Name =>
        spReflectionGeneric_GetName(_ptr).String;

    /// <summary>
    /// Gets the total number of type parameters in this generic type.
    /// </summary>
    public readonly uint TypeParameterCount =>
        spReflectionGeneric_GetTypeParameterCount(_ptr);

    /// <summary>
    /// Returns the <see cref="VariableReflection"/> in the generic type parameter list at the given index.
    /// </summary>
    public readonly VariableReflection GetTypeParameter(uint index) =>
        new(spReflectionGeneric_GetTypeParameter(_ptr, index), _component);

    /// <summary>
    /// Type parameters of this generic.
    /// </summary>
    public readonly IEnumerable<VariableReflection> TypeParameters =>
        Utility.For(TypeParameterCount, GetTypeParameter);

    public readonly uint ValueParameterCount =>
        spReflectionGeneric_GetValueParameterCount(_ptr);

    public readonly VariableReflection GetValueParameter(uint index) =>
        new(spReflectionGeneric_GetValueParameter(_ptr, index), _component);

    public readonly IEnumerable<VariableReflection> ValueParameters =>
        Utility.For(ValueParameterCount, GetValueParameter);

    public readonly uint GetTypeParameterConstraintCount(VariableReflection typeParam) =>
        spReflectionGeneric_GetTypeParameterConstraintCount(_ptr, typeParam._ptr);

    public readonly TypeReflection GetTypeParameterConstraintType(VariableReflection typeParam, uint index) =>
        new(spReflectionGeneric_GetTypeParameterConstraintType(_ptr, typeParam._ptr, index), _component);

    public readonly DeclReflection InnerDecl =>
        new(spReflectionGeneric_GetInnerDecl(_ptr), _component);

    public readonly DeclKind InnerKind =>
        spReflectionGeneric_GetInnerKind(_ptr);

    public readonly GenericReflection OuterGenericContainer =>
        new(spReflectionGeneric_GetOuterGenericContainer(_ptr), _component);

    public readonly TypeReflection GetConcreteType(VariableReflection typeParam) =>
        new(spReflectionGeneric_GetConcreteType(_ptr, typeParam._ptr), _component);

    public readonly long GetConcreteIntVal(VariableReflection valueParam) =>
        spReflectionGeneric_GetConcreteIntVal(_ptr, valueParam._ptr);

    public readonly GenericReflection ApplySpecializations(GenericReflection generic) =>
        new(spReflectionGeneric_applySpecializations(_ptr, generic._ptr), _component);
};
