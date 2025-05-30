// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Slang.Native;

using static Prowl.Slang.Native.SlangNative_Deprecated;


namespace Prowl.Slang;


/// <summary>
/// Provides reflection information about a shader type's memory layout and field structure.
/// This struct exposes detailed information about how types are laid out in memory within
/// a compiled shader, including size, alignment, field information, and binding details.
/// </summary>
public unsafe struct TypeLayoutReflection
{
    internal ComponentType _component;
    internal Native.TypeLayoutReflection* _ptr;


    internal TypeLayoutReflection(Native.TypeLayoutReflection* ptr, ComponentType component)
    {
        ArgumentNullException.ThrowIfNull(ptr, nameof(ptr));

        _component = component;
        _ptr = ptr;
    }

    /// <summary>
    /// Gets the underlying type reflection object for this type layout.
    /// </summary>
    public readonly TypeReflection ReflectionType =>
        new(spReflectionTypeLayout_GetType(_ptr), _component);

    /// <summary>
    /// Gets the kind of the shader type (scalar, vector, matrix, struct, array, etc.).
    /// </summary>
    public readonly TypeKind Kind =>
        spReflectionTypeLayout_getKind(_ptr);

    /// <summary>
    /// Gets the size in bytes of this type for the specified parameter category.
    /// </summary>
    /// <param name="category">The parameter category to query size for.</param>
    /// <returns>The size in bytes.</returns>
    public readonly nuint GetSize(ParameterCategory category) =>
        spReflectionTypeLayout_GetSize(_ptr, category);

    /// <summary>
    /// Gets the stride in bytes between consecutive elements of this type for the specified parameter category.
    /// </summary>
    /// <param name="category">The parameter category to query stride for.</param>
    /// <returns>The stride in bytes.</returns>
    public readonly nuint GetStride(ParameterCategory category) =>
        spReflectionTypeLayout_GetStride(_ptr, category);

    /// <summary>
    /// Gets the alignment requirement in bytes for this type for the specified parameter category.
    /// </summary>
    /// <param name="category">The parameter category to query alignment for.</param>
    /// <returns>The alignment requirement in bytes.</returns>
    public readonly int GetAlignment(ParameterCategory category) =>
        spReflectionTypeLayout_getAlignment(_ptr, category);

    /// <summary>
    /// Gets the number of fields in this type.
    /// </summary>
    public readonly uint FieldCount =>
        spReflectionTypeLayout_GetFieldCount(_ptr);

    /// <summary>
    /// Gets a field's layout information by its index.
    /// </summary>
    /// <param name="index">The zero-based index of the field.</param>
    /// <returns>Layout information for the specified field.</returns>
    public readonly VariableLayoutReflection GetFieldByIndex(uint index) =>
        new(spReflectionTypeLayout_GetFieldByIndex(_ptr, index), _component);

    /// <summary>
    /// Gets an enumerable collection of all fields in this type.
    /// </summary>
    public readonly IEnumerable<VariableLayoutReflection> Fields =>
        Utility.For(FieldCount, GetFieldByIndex);

    /// <summary>
    /// Finds the index of a field by name.
    /// </summary>
    /// <param name="nameBegin">The start of the field name.</param>
    /// <param name="nameEnd">The end of the field name.</param>
    /// <returns>The index of the found field, or a negative value if not found.</returns>
    public readonly nint FindFieldIndexByName(string nameBegin, string nameEnd)
    {
        using U8Str strA = U8Str.Alloc(nameBegin);
        using U8Str strB = U8Str.Alloc(nameEnd);

        return spReflectionTypeLayout_findFieldIndexByName(_ptr, strA, strB);
    }

    /// <summary>
    /// Gets the explicit counter variable layout for this type, if applicable.
    /// Typically used with resource types that have associated counters.
    /// </summary>
    public readonly VariableLayoutReflection ExplicitCounter =>
        new(spReflectionTypeLayout_GetExplicitCounter(_ptr), _component);

    /// <summary>
    /// Gets a value indicating whether this type is an array type.
    /// </summary>
    public readonly bool IsArray =>
        ReflectionType.IsArray;

    /// <summary>
    /// Unwraps nested array types to get the innermost element type layout.
    /// </summary>
    /// <returns>The innermost non-array type layout.</returns>
    public readonly TypeLayoutReflection UnwrapArray()
    {
        TypeLayoutReflection typeLayout = this;

        while (typeLayout.IsArray)
            typeLayout = typeLayout.ElementTypeLayout;

        return typeLayout;
    }

    /// <summary>
    /// Gets the number of elements in this array type.
    /// Only applicable when <see cref="IsArray"/> is true.
    /// </summary>
    public readonly nuint ElementCount =>
        ReflectionType.ElementCount;

    /// <summary>
    /// Gets the total number of elements across all dimensions for a multi-dimensional array.
    /// </summary>
    public readonly nuint TotalArrayElementCount =>
        ReflectionType.GetTotalArrayElementCount();

    /// <summary>
    /// Gets the stride between consecutive elements in this array for the specified parameter category.
    /// </summary>
    /// <param name="category">The parameter category to query stride for.</param>
    /// <returns>The element stride in bytes.</returns>
    public readonly nuint GetElementStride(ParameterCategory category) =>
        spReflectionTypeLayout_GetElementStride(_ptr, category);

    /// <summary>
    /// Gets the type layout of elements in this array type.
    /// </summary>
    public readonly TypeLayoutReflection ElementTypeLayout =>
        new(spReflectionTypeLayout_GetElementTypeLayout(_ptr), _component);

    /// <summary>
    /// Gets the variable layout of elements in this array type.
    /// </summary>
    public readonly VariableLayoutReflection ElementVarLayout =>
        new(spReflectionTypeLayout_GetElementVarLayout(_ptr), _component);

    /// <summary>
    /// Gets the container variable layout that holds this type.
    /// </summary>
    public readonly VariableLayoutReflection ContainerVarLayout =>
        new(spReflectionTypeLayout_getContainerVarLayout(_ptr), _component);

    /// <summary>
    /// Gets the parameter category that determines how this type is bound in the shader.
    /// </summary>
    public readonly ParameterCategory ParameterCategory =>
        spReflectionTypeLayout_GetParameterCategory(_ptr);

    /// <summary>
    /// Gets the number of parameter categories applicable to this type.
    /// </summary>
    public readonly uint CategoryCount =>
        spReflectionTypeLayout_GetCategoryCount(_ptr);

    /// <summary>
    /// Gets a parameter category by its index.
    /// </summary>
    /// <param name="index">The zero-based index of the category.</param>
    /// <returns>The parameter category at the specified index.</returns>
    public readonly ParameterCategory GetCategoryByIndex(uint index) =>
        spReflectionTypeLayout_GetCategoryByIndex(_ptr, index);

    /// <summary>
    /// Gets an enumerable collection of all parameter categories applicable to this type.
    /// </summary>
    public readonly IEnumerable<ParameterCategory> Categories =>
        Utility.For(CategoryCount, GetCategoryByIndex);

    /// <summary>
    /// For matrix types, gets the number of rows.
    /// </summary>
    public readonly uint RowCount =>
        ReflectionType.RowCount;

    /// <summary>
    /// For matrix or vector types, gets the number of columns.
    /// </summary>
    public readonly uint ColumnCount =>
        ReflectionType.ColumnCount;

    /// <summary>
    /// Gets the scalar type (float, int, etc.) for this type.
    /// </summary>
    public readonly ScalarType ScalarType =>
        ReflectionType.ScalarType;

    /// <summary>
    /// For resource types, gets the result type of accessing the resource.
    /// </summary>
    public readonly TypeReflection ResourceResultType =>
        ReflectionType.ResourceResultType;

    /// <summary>
    /// For resource types, gets the shape of the resource (buffer, texture1D, texture2D, etc.).
    /// </summary>
    public readonly ResourceShape ResourceShape =>
        ReflectionType.ResourceShape;

    /// <summary>
    /// For resource types, gets the access permissions (read, write, read-write).
    /// </summary>
    public readonly ResourceAccess ResourceAccess =>
        ReflectionType.ResourceAccess;

    /// <summary>
    /// Gets the name of this type.
    /// </summary>
    public readonly string Name =>
        ReflectionType.Name;

    /// <summary>
    /// For matrix types, gets the matrix layout mode (row-major or column-major).
    /// </summary>
    public readonly MatrixLayoutMode MatrixLayoutMode =>
        spReflectionTypeLayout_GetMatrixLayoutMode(_ptr);

    /// <summary>
    /// For generic types, gets the index of the generic parameter.
    /// </summary>
    public readonly int GenericParamIndex =>
        spReflectionTypeLayout_getGenericParamIndex(_ptr);

    /// <summary>
    /// Gets the type layout for pending data associated with this type.
    /// </summary>
    public readonly TypeLayoutReflection PendingDataTypeLayout =>
        new(spReflectionTypeLayout_getPendingDataTypeLayout(_ptr), _component);

    /// <summary>
    /// Gets the variable layout for pending data in a specialized type.
    /// </summary>
    public readonly VariableLayoutReflection SpecializedTypePendingDataVarLayout =>
        new(spReflectionTypeLayout_getSpecializedTypePendingDataVarLayout(_ptr), _component);

    /// <summary>
    /// Gets the number of binding ranges in this type.
    /// </summary>
    public readonly nint BindingRangeCount =>
        spReflectionTypeLayout_getBindingRangeCount(_ptr);

    /// <summary>
    /// Gets the binding type for a specified binding range index.
    /// </summary>
    /// <param name="index">The zero-based index of the binding range.</param>
    /// <returns>The binding type at the specified index.</returns>
    public readonly BindingType GetBindingRangeType(nint index) =>
        spReflectionTypeLayout_getBindingRangeType(_ptr, index);

    /// <summary>
    /// Determines whether a binding range is specializable.
    /// </summary>
    /// <param name="index">The zero-based index of the binding range.</param>
    /// <returns>True if the binding range is specializable; otherwise, false.</returns>
    public readonly bool IsBindingRangeSpecializable(nint index) =>
        spReflectionTypeLayout_isBindingRangeSpecializable(_ptr, index) == 1;

    /// <summary>
    /// Gets the number of bindings in a binding range.
    /// </summary>
    /// <param name="index">The zero-based index of the binding range.</param>
    /// <returns>The number of bindings in the range.</returns>
    public readonly nint GetBindingRangeBindingCount(nint index) =>
        spReflectionTypeLayout_getBindingRangeBindingCount(_ptr, index);

    /// <summary>
    /// Gets the binding range offset for a field.
    /// </summary>
    /// <param name="fieldIndex">The zero-based index of the field.</param>
    /// <returns>The binding range offset for the field.</returns>
    public readonly nint GetFieldBindingRangeOffset(nint fieldIndex) =>
        spReflectionTypeLayout_getFieldBindingRangeOffset(_ptr, fieldIndex);

    /// <summary>
    /// Gets the binding range offset for the explicit counter.
    /// </summary>
    public readonly nint ExplicitCounterBindingRangeOffset =>
        spReflectionTypeLayout_getExplicitCounterBindingRangeOffset(_ptr);

    /// <summary>
    /// Gets the leaf type layout for a binding range.
    /// </summary>
    /// <param name="index">The zero-based index of the binding range.</param>
    /// <returns>The leaf type layout for the binding range.</returns>
    public readonly TypeLayoutReflection GetBindingRangeLeafTypeLayout(nint index) =>
        new(spReflectionTypeLayout_getBindingRangeLeafTypeLayout(_ptr, index), _component);

    /// <summary>
    /// Gets the leaf variable for a binding range.
    /// </summary>
    /// <param name="index">The zero-based index of the binding range.</param>
    /// <returns>The leaf variable for the binding range.</returns>
    public readonly VariableReflection GetBindingRangeLeafVariable(nint index) =>
        new(spReflectionTypeLayout_getBindingRangeLeafVariable(_ptr, index), _component);

    /// <summary>
    /// Gets the image format for a binding range.
    /// </summary>
    /// <param name="index">The zero-based index of the binding range.</param>
    /// <returns>The image format for the binding range.</returns>
    public readonly ImageFormat GetBindingRangeImageFormat(nint index) =>
        spReflectionTypeLayout_getBindingRangeImageFormat(_ptr, index);

    /// <summary>
    /// Gets the descriptor set index for a binding range.
    /// </summary>
    /// <param name="index">The zero-based index of the binding range.</param>
    /// <returns>The descriptor set index for the binding range.</returns>
    public readonly nint GetBindingRangeDescriptorSetIndex(nint index) =>
        spReflectionTypeLayout_getBindingRangeDescriptorSetIndex(_ptr, index);

    /// <summary>
    /// Gets the first descriptor range index for a binding range.
    /// </summary>
    /// <param name="index">The zero-based index of the binding range.</param>
    /// <returns>The first descriptor range index for the binding range.</returns>
    public readonly nint GetBindingRangeFirstDescriptorRangeIndex(nint index) =>
        spReflectionTypeLayout_getBindingRangeFirstDescriptorRangeIndex(_ptr, index);

    /// <summary>
    /// Gets the descriptor range count for a binding range.
    /// </summary>
    /// <param name="index">The zero-based index of the binding range.</param>
    /// <returns>The descriptor range count for the binding range.</returns>
    public readonly nint GetBindingRangeDescriptorRangeCount(nint index) =>
        spReflectionTypeLayout_getBindingRangeDescriptorRangeCount(_ptr, index);

    /// <summary>
    /// Gets the number of descriptor sets in this type.
    /// </summary>
    public readonly nint DescriptorSetCount =>
        spReflectionTypeLayout_getDescriptorSetCount(_ptr);

    /// <summary>
    /// Gets the space offset for a descriptor set.
    /// </summary>
    /// <param name="setIndex">The zero-based index of the descriptor set.</param>
    /// <returns>The space offset for the descriptor set.</returns>
    public readonly nint GetDescriptorSetSpaceOffset(nint setIndex) =>
        spReflectionTypeLayout_getDescriptorSetSpaceOffset(_ptr, setIndex);

    /// <summary>
    /// Gets the number of descriptor ranges in a descriptor set.
    /// </summary>
    /// <param name="setIndex">The zero-based index of the descriptor set.</param>
    /// <returns>The number of descriptor ranges in the descriptor set.</returns>
    public readonly nint GetDescriptorSetDescriptorRangeCount(nint setIndex) =>
        spReflectionTypeLayout_getDescriptorSetDescriptorRangeCount(_ptr, setIndex);

    /// <summary>
    /// Gets the descriptor range index offset for a descriptor set and range.
    /// </summary>
    /// <param name="setIndex">The zero-based index of the descriptor set.</param>
    /// <param name="rangeIndex">The zero-based index of the range.</param>
    /// <returns>The descriptor range index offset.</returns>
    public readonly nint GetDescriptorSetDescriptorRangeIndexOffset(nint setIndex, nint rangeIndex) =>
        spReflectionTypeLayout_getDescriptorSetDescriptorRangeIndexOffset(_ptr, setIndex, rangeIndex);

    /// <summary>
    /// Gets the descriptor count in a descriptor range of a descriptor set.
    /// </summary>
    /// <param name="setIndex">The zero-based index of the descriptor set.</param>
    /// <param name="rangeIndex">The zero-based index of the range.</param>
    /// <returns>The descriptor count in the range.</returns>
    public readonly nint GetDescriptorSetDescriptorRangeDescriptorCount(nint setIndex, nint rangeIndex) =>
        spReflectionTypeLayout_getDescriptorSetDescriptorRangeDescriptorCount(_ptr, setIndex, rangeIndex);

    /// <summary>
    /// Gets the binding type for a descriptor range in a descriptor set.
    /// </summary>
    /// <param name="setIndex">The zero-based index of the descriptor set.</param>
    /// <param name="rangeIndex">The zero-based index of the range.</param>
    /// <returns>The binding type for the descriptor range.</returns>
    public readonly BindingType GetDescriptorSetDescriptorRangeType(nint setIndex, nint rangeIndex) =>
        spReflectionTypeLayout_getDescriptorSetDescriptorRangeType(_ptr, setIndex, rangeIndex);

    /// <summary>
    /// Gets the parameter category for a descriptor range in a descriptor set.
    /// </summary>
    /// <param name="setIndex">The zero-based index of the descriptor set.</param>
    /// <param name="rangeIndex">The zero-based index of the range.</param>
    /// <returns>The parameter category for the descriptor range.</returns>
    public readonly ParameterCategory GetDescriptorSetDescriptorRangeCategory(nint setIndex, nint rangeIndex) =>
        spReflectionTypeLayout_getDescriptorSetDescriptorRangeCategory(_ptr, setIndex, rangeIndex);

    /// <summary>
    /// Gets the number of sub-object ranges in this type.
    /// </summary>
    public readonly nint SubObjectRangeCount =>
        spReflectionTypeLayout_getSubObjectRangeCount(_ptr);

    /// <summary>
    /// Gets the binding range index for a sub-object range.
    /// </summary>
    /// <param name="subObjectRangeIndex">The zero-based index of the sub-object range.</param>
    /// <returns>The binding range index for the sub-object range.</returns>
    public readonly nint GetSubObjectRangeBindingRangeIndex(nint subObjectRangeIndex) =>
        spReflectionTypeLayout_getSubObjectRangeBindingRangeIndex(_ptr, subObjectRangeIndex);

    /// <summary>
    /// Gets the space offset for a sub-object range.
    /// </summary>
    /// <param name="subObjectRangeIndex">The zero-based index of the sub-object range.</param>
    /// <returns>The space offset for the sub-object range.</returns>
    public readonly nint GetSubObjectRangeSpaceOffset(nint subObjectRangeIndex) =>
        spReflectionTypeLayout_getSubObjectRangeSpaceOffset(_ptr, subObjectRangeIndex);

    /// <summary>
    /// Gets the variable layout offset for a sub-object range.
    /// </summary>
    /// <param name="subObjectRangeIndex">The zero-based index of the sub-object range.</param>
    /// <returns>The variable layout offset for the sub-object range.</returns>
    public readonly VariableLayoutReflection GetSubObjectRangeOffset(nint subObjectRangeIndex) =>
        new(spReflectionTypeLayout_getSubObjectRangeOffset(_ptr, subObjectRangeIndex), _component);
}
