// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Prowl.Slang.Native;

using static Prowl.Slang.Native.SlangNative_Deprecated;


namespace Prowl.Slang;


/// <summary>
/// Provides reflection information for a user-defined shader type in a shader source module.
/// This struct allows access to type metadata, including kind, fields, array properties,
/// resource information, and attributes.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct TypeReflection
{
    internal ComponentType _component;
    internal Native.TypeReflection* _ptr;

    internal TypeReflection(Native.TypeReflection* ptr, ComponentType component)
    {
        ArgumentNullException.ThrowIfNull(ptr, nameof(ptr));

        _component = component;
        _ptr = ptr;
    }

    /// <summary>
    /// Gets the kind of shader type represented by this reflection.
    /// </summary>
    public readonly TypeKind Kind =>
        spReflectionType_GetKind(_ptr);

    /// <summary>
    /// Gets the number of fields in this type.
    /// Only meaningful when <see cref="Kind"/> is <see cref="TypeKind.Struct"/>.
    /// </summary>
    public readonly uint FieldCount =>
        spReflectionType_GetFieldCount(_ptr);

    /// <summary>
    /// Gets reflection information for a field by its index.
    /// </summary>
    /// <param name="index">Zero-based index of the field to retrieve.</param>
    /// <returns>Reflection information for the specified field.</returns>
    public readonly VariableReflection GetFieldByIndex(uint index) =>
        new(spReflectionType_GetFieldByIndex(_ptr, index), _component);

    /// <summary>
    /// Gets all fields in this type as an enumerable collection.
    /// </summary>
    public readonly IEnumerable<VariableReflection> Fields =>
        Utility.For(FieldCount, GetFieldByIndex);

    /// <summary>
    /// Gets a value indicating whether this type is an array.
    /// </summary>
    public readonly bool IsArray =>
        Kind == TypeKind.Array;

    /// <summary>
    /// Unwraps nested array types to get the innermost element type.
    /// </summary>
    /// <returns>The innermost element type of this array, or this type if not an array.</returns>
    public readonly TypeReflection UnwrapArray()
    {
        TypeReflection type = this;

        while (type.IsArray)
            type = type.ElementType;

        return type;
    }

    /// <summary>
    /// Gets the number of elements in this array type.
    /// Only meaningful when <see cref="IsArray"/> is true.
    /// </summary>
    public readonly uint ElementCount =>
        (uint)spReflectionType_GetElementCount(_ptr);

    /// <summary>
    /// Calculates the total number of elements across all dimensions for multi-dimensional arrays.
    /// </summary>
    /// <returns>The total element count, or 0 if this is not an array type.</returns>
    public readonly uint GetTotalArrayElementCount()
    {
        if (!IsArray)
            return 0;

        uint result = 1;
        TypeReflection type = this;
        for (; ; )
        {
            if (!type.IsArray)
                return result;

            result *= type.ElementCount;
            type = type.ElementType;
        }
    }

    /// <summary>
    /// Gets the element type of this array or resource type.
    /// </summary>
    public readonly TypeReflection ElementType =>
        new(spReflectionType_GetElementType(_ptr), _component);

    /// <summary>
    /// Gets the number of rows in this matrix type.
    /// </summary>
    public readonly uint RowCount =>
        spReflectionType_GetRowCount(_ptr);

    /// <summary>
    /// Gets the number of columns in this matrix type.
    /// </summary>
    public readonly uint ColumnCount =>
        spReflectionType_GetColumnCount(_ptr);

    /// <summary>
    /// Gets the scalar type for vector, matrix, or scalar types.
    /// </summary>
    public readonly ScalarType ScalarType =>
        spReflectionType_GetScalarType(_ptr);

    /// <summary>
    /// Gets the result type of this resource type.
    /// </summary>
    public readonly TypeReflection ResourceResultType =>
        new(spReflectionType_GetResourceResultType(_ptr), _component);

    /// <summary>
    /// Gets the shape of this resource type.
    /// </summary>
    public readonly ResourceShape ResourceShape =>
        spReflectionType_GetResourceShape(_ptr);

    /// <summary>
    /// Gets the access level of this resource type.
    /// </summary>
    public readonly ResourceAccess ResourceAccess =>
        spReflectionType_GetResourceAccess(_ptr);

    /// <summary>
    /// Gets the simple name of this type.
    /// </summary>
    public readonly string Name =>
        spReflectionType_GetName(_ptr).String;

    /// <summary>
    /// Gets the fully-qualified name of this type.
    /// </summary>
    public readonly string FullName
    {
        get
        {
            spReflectionType_GetFullName(_ptr, out ISlangBlob* namePtr)
                .Throw("Failed to get full name of type");

            return NativeComProxy.Create(namePtr).GetString();
        }
    }

    /// <summary>
    /// Gets the number of user attributes applied to this type.
    /// </summary>
    public readonly uint UserAttributeCount =>
        spReflectionType_GetUserAttributeCount(_ptr);

    /// <summary>
    /// Gets a user attribute by its index.
    /// </summary>
    /// <param name="index">Zero-based index of the attribute to retrieve.</param>
    /// <returns>The specified user attribute.</returns>
    public readonly Attribute GetUserAttributeByIndex(uint index) =>
        new(spReflectionType_GetUserAttribute(_ptr, index), _component);

    /// <summary>
    /// Gets all user attributes applied to this type as an enumerable collection.
    /// </summary>
    public readonly IEnumerable<Attribute> UserAttributes =>
        Utility.For(UserAttributeCount, GetUserAttributeByIndex);

    /// <summary>
    /// Finds an attribute by its name.
    /// </summary>
    /// <param name="name">The name of the attribute to find.</param>
    /// <returns>The attribute with the specified name, if found.</returns>
    public readonly Attribute FindAttributeByName(string name)
    {
        using U8Str str = U8Str.Alloc(name);
        return new(spReflectionType_FindUserAttributeByName(_ptr, str), _component);
    }

    /// <summary>
    /// Finds a user attribute by its name. Alias for <see cref="FindAttributeByName"/>.
    /// </summary>
    /// <param name="name">The name of the user attribute to find.</param>
    /// <returns>The user attribute with the specified name, if found.</returns>
    public readonly Attribute FindUserAttributeByName(string name) =>
        FindAttributeByName(name);

    /// <summary>
    /// Applies generic specializations to this type.
    /// </summary>
    /// <param name="generic">The generic reflection to apply.</param>
    /// <returns>A new type reflection with the specializations applied.</returns>
    public readonly TypeReflection ApplySpecializations(GenericReflection generic) =>
        new(spReflectionType_applySpecializations(_ptr, generic._ptr), _component);

    /// <summary>
    /// Gets the generic container for this type, if it is a generic type.
    /// </summary>
    public readonly GenericReflection GenericContainer =>
        new(spReflectionType_GetGenericContainer(_ptr), _component);
}
