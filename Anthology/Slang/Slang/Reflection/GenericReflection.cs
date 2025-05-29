// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using static Prowl.Slang.Native.SlangNative_Deprecated;


namespace Prowl.Slang;


/// <summary>
/// Represents reflection information for a generic declaration in a shader source module.
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
    /// Gets the underlying declaration reflection for this generic.
    /// </summary>
    /// <returns>A <see cref="DeclReflection"/> instance representing this generic's declaration.</returns>
    public readonly DeclReflection AsDecl() =>
        new(spReflectionGeneric_asDecl(_ptr), _component);

    /// <summary>
    /// Gets the name of this generic declaration.
    /// </summary>
    public readonly string Name =>
        spReflectionGeneric_GetName(_ptr).String;

    /// <summary>
    /// Gets the number of type parameters defined by this generic declaration.
    /// </summary>
    public readonly uint TypeParameterCount =>
        spReflectionGeneric_GetTypeParameterCount(_ptr);

    /// <summary>
    /// Gets the type parameter at the specified index.
    /// </summary>
    /// <param name="index">The zero-based index of the type parameter to retrieve.</param>
    /// <returns>A <see cref="VariableReflection"/> representing the type parameter.</returns>
    public readonly VariableReflection GetTypeParameter(uint index) =>
        new(spReflectionGeneric_GetTypeParameter(_ptr, index), _component);

    /// <summary>
    /// Gets all type parameters defined by this generic declaration.
    /// </summary>
    public readonly IEnumerable<VariableReflection> TypeParameters =>
        Utility.For(TypeParameterCount, GetTypeParameter);

    /// <summary>
    /// Gets the number of value parameters defined by this generic declaration.
    /// </summary>
    public readonly uint ValueParameterCount =>
        spReflectionGeneric_GetValueParameterCount(_ptr);

    /// <summary>
    /// Gets the value parameter at the specified index.
    /// </summary>
    /// <param name="index">The zero-based index of the value parameter to retrieve.</param>
    /// <returns>A <see cref="VariableReflection"/> representing the value parameter.</returns>
    public readonly VariableReflection GetValueParameter(uint index) =>
        new(spReflectionGeneric_GetValueParameter(_ptr, index), _component);

    /// <summary>
    /// Gets all value parameters defined by this generic declaration.
    /// </summary>
    public readonly IEnumerable<VariableReflection> ValueParameters =>
        Utility.For(ValueParameterCount, GetValueParameter);

    /// <summary>
    /// Gets the number of constraints applied to the specified type parameter.
    /// </summary>
    /// <param name="typeParam">The type parameter to query for constraints.</param>
    /// <returns>The number of constraints on the type parameter.</returns>
    public readonly uint GetTypeParameterConstraintCount(VariableReflection typeParam) =>
        spReflectionGeneric_GetTypeParameterConstraintCount(_ptr, typeParam._ptr);

    /// <summary>
    /// Gets the constraint type at the specified index for a type parameter.
    /// </summary>
    /// <param name="typeParam">The type parameter to query.</param>
    /// <param name="index">The zero-based index of the constraint to retrieve.</param>
    /// <returns>A <see cref="TypeReflection"/> representing the constraint type.</returns>
    public readonly TypeReflection GetTypeParameterConstraintType(VariableReflection typeParam, uint index) =>
        new(spReflectionGeneric_GetTypeParameterConstraintType(_ptr, typeParam._ptr, index), _component);

    /// <summary>
    /// Gets the inner declaration contained within this generic declaration.
    /// </summary>
    public readonly DeclReflection InnerDecl =>
        new(spReflectionGeneric_GetInnerDecl(_ptr), _component);

    /// <summary>
    /// Gets the kind of the inner declaration contained within this generic declaration.
    /// </summary>
    public readonly DeclKind InnerKind =>
        spReflectionGeneric_GetInnerKind(_ptr);

    /// <summary>
    /// Gets the outer generic container that contains this generic declaration, if any.
    /// </summary>
    public readonly GenericReflection OuterGenericContainer =>
        new(spReflectionGeneric_GetOuterGenericContainer(_ptr), _component);

    /// <summary>
    /// Gets the concrete type that a type parameter has been specialized to.
    /// </summary>
    /// <param name="typeParam">The type parameter to query.</param>
    /// <returns>A <see cref="TypeReflection"/> representing the concrete type.</returns>
    public readonly TypeReflection GetConcreteType(VariableReflection typeParam) =>
        new(spReflectionGeneric_GetConcreteType(_ptr, typeParam._ptr), _component);

    /// <summary>
    /// Gets the concrete integer value that a value parameter has been specialized to.
    /// </summary>
    /// <param name="valueParam">The value parameter to query.</param>
    /// <returns>The concrete integer value.</returns>
    public readonly long GetConcreteIntVal(VariableReflection valueParam) =>
        spReflectionGeneric_GetConcreteIntVal(_ptr, valueParam._ptr);

    /// <summary>
    /// Applies the specializations from another generic to this generic declaration.
    /// </summary>
    /// <param name="generic">The generic containing specializations to apply.</param>
    /// <returns>A new <see cref="GenericReflection"/> with the specializations applied.</returns>
    public readonly GenericReflection ApplySpecializations(GenericReflection generic) =>
        new(spReflectionGeneric_applySpecializations(_ptr, generic._ptr), _component);
}
