// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using static Prowl.Slang.Native.SlangNative_Deprecated;


namespace Prowl.Slang;


/// <summary>
/// Represents reflection information for a type parameter in a generic shader type.
/// Provides access to the parameter's constraints and metadata.
/// </summary>
public unsafe struct TypeParameterReflection
{
    internal ComponentType _component;
    internal Native.TypeParameterReflection* _ptr;

    internal TypeParameterReflection(Native.TypeParameterReflection* ptr, ComponentType component)
    {
        ArgumentNullException.ThrowIfNull(ptr, nameof(ptr));

        _component = component;
        _ptr = ptr;
    }

    /// <summary>
    /// Gets the name of the type parameter.
    /// </summary>
    public readonly string Name =>
        spReflectionTypeParameter_GetName(_ptr).String;

    /// <summary>
    /// Gets the zero-based index of the type parameter in its parent type's parameter list.
    /// </summary>
    public readonly uint Index =>
        spReflectionTypeParameter_GetIndex(_ptr);

    /// <summary>
    /// Gets the number of constraints applied to this type parameter.
    /// </summary>
    public readonly uint ConstraintCount =>
        spReflectionTypeParameter_GetConstraintCount(_ptr);

    /// <summary>
    /// Gets a constraint type by its index.
    /// </summary>
    /// <param name="index">The zero-based index of the constraint to retrieve.</param>
    /// <returns>A <see cref="TypeReflection"/> representing the constraint type.</returns>
    public readonly TypeReflection GetConstraintByIndex(uint index) =>
        new(spReflectionTypeParameter_GetConstraintByIndex(_ptr, index), _component);

    /// <summary>
    /// Gets all constraints applied to this type parameter.
    /// </summary>
    /// <returns>An enumerable collection of <see cref="TypeReflection"/> objects representing all constraints.</returns>
    public readonly IEnumerable<TypeReflection> GetConstraints() =>
        Utility.For(ConstraintCount, GetConstraintByIndex);


    /// <inheritdoc/>
    public static bool operator ==(TypeParameterReflection a, TypeParameterReflection b)
    {
        return a._ptr == b._ptr;
    }


    /// <inheritdoc/>
    public static bool operator !=(TypeParameterReflection a, TypeParameterReflection b)
    {
        return a._ptr != b._ptr;
    }


    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        if (obj is TypeParameterReflection other)
            return other._ptr == _ptr;

        return false;
    }


    /// <inheritdoc/>
    public override int GetHashCode() => ((nint)_ptr).ToInt32();
}
