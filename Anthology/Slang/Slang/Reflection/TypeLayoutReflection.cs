using System;
using System.Collections.Generic;

using Prowl.Slang.Native;

using static Prowl.Slang.Native.SlangNative_Deprecated;

using SlangInt = nint;


namespace Prowl.Slang;


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


    public readonly TypeReflection ReflectionType =>
        new(spReflectionTypeLayout_GetType(_ptr), _component);

    public readonly SlangTypeKind Kind =>
        spReflectionTypeLayout_getKind(_ptr);

    public readonly nuint GetSize(SlangParameterCategory category) =>
        spReflectionTypeLayout_GetSize(_ptr, category);

    public readonly nuint GetStride(SlangParameterCategory category) =>
        spReflectionTypeLayout_GetStride(_ptr, category);

    public readonly int GetAlignment(SlangParameterCategory category) =>
        spReflectionTypeLayout_getAlignment(_ptr, category);

    public readonly uint FieldCount =>
        spReflectionTypeLayout_GetFieldCount(_ptr);

    public readonly VariableLayoutReflection GetFieldByIndex(uint index) =>
        new(spReflectionTypeLayout_GetFieldByIndex(_ptr, index), _component);

    public readonly IEnumerable<VariableLayoutReflection> Fields =>
        Utility.For(FieldCount, GetFieldByIndex);

    public readonly SlangInt FindFieldIndexByName(string nameBegin, string nameEnd)
    {
        using U8Str strA = U8Str.Alloc(nameBegin);
        using U8Str strB = U8Str.Alloc(nameEnd);

        return spReflectionTypeLayout_findFieldIndexByName(_ptr, strA, strB);
    }

    public readonly VariableLayoutReflection ExplicitCounter =>
        new(spReflectionTypeLayout_GetExplicitCounter(_ptr), _component);

    public readonly bool IsArray =>
        ReflectionType.IsArray;

    public readonly TypeLayoutReflection UnwrapArray()
    {
        TypeLayoutReflection typeLayout = this;

        while (typeLayout.IsArray)
            typeLayout = typeLayout.ElementTypeLayout;

        return typeLayout;
    }

    // only useful if `getKind() == Kind::Array`
    public readonly nuint ElementCount =>
        ReflectionType.ElementCount;

    public readonly nuint TotalArrayElementCount =>
        ReflectionType.GetTotalArrayElementCount();

    public readonly nuint GetElementStride(SlangParameterCategory category) =>
        spReflectionTypeLayout_GetElementStride(_ptr, category);

    public readonly TypeLayoutReflection ElementTypeLayout =>
        new(spReflectionTypeLayout_GetElementTypeLayout(_ptr), _component);

    public readonly VariableLayoutReflection ElementVarLayout =>
        new(spReflectionTypeLayout_GetElementVarLayout(_ptr), _component);

    public readonly VariableLayoutReflection ContainerVarLayout =>
        new(spReflectionTypeLayout_getContainerVarLayout(_ptr), _component);

    // How is this type supposed to be bound?
    public readonly SlangParameterCategory ParameterCategory =>
        spReflectionTypeLayout_GetParameterCategory(_ptr);

    public readonly uint CategoryCount =>
        spReflectionTypeLayout_GetCategoryCount(_ptr);

    public readonly SlangParameterCategory GetCategoryByIndex(uint index) =>
        spReflectionTypeLayout_GetCategoryByIndex(_ptr, index);

    public readonly IEnumerable<SlangParameterCategory> Categories =>
        Utility.For(CategoryCount, GetCategoryByIndex);

    public readonly uint RowCount =>
        ReflectionType.RowCount;

    public readonly uint ColumnCount =>
        ReflectionType.ColumnCount;

    public readonly SlangScalarType ScalarType =>
        ReflectionType.ScalarType;

    public readonly TypeReflection ResourceResultType =>
        ReflectionType.ResourceResultType;

    public readonly SlangResourceShape ResourceShape =>
        ReflectionType.ResourceShape;

    public readonly SlangResourceAccess ResourceAccess =>
        ReflectionType.ResourceAccess;

    public readonly string Name =>
        ReflectionType.Name;

    public readonly SlangMatrixLayoutMode MatrixLayoutMode =>
        spReflectionTypeLayout_GetMatrixLayoutMode(_ptr);

    public readonly int GenericParamIndex =>
        spReflectionTypeLayout_getGenericParamIndex(_ptr);

    public readonly TypeLayoutReflection PendingDataTypeLayout =>
        new(spReflectionTypeLayout_getPendingDataTypeLayout(_ptr), _component);

    public readonly VariableLayoutReflection SpecializedTypePendingDataVarLayout =>
        new(spReflectionTypeLayout_getSpecializedTypePendingDataVarLayout(_ptr), _component);

    public readonly SlangInt BindingRangeCount =>
        spReflectionTypeLayout_getBindingRangeCount(_ptr);

    public readonly SlangBindingType GetBindingRangeType(SlangInt index) =>
        spReflectionTypeLayout_getBindingRangeType(_ptr, index);

    public readonly bool IsBindingRangeSpecializable(SlangInt index) =>
        spReflectionTypeLayout_isBindingRangeSpecializable(_ptr, index) == 1;

    public readonly SlangInt GetBindingRangeBindingCount(SlangInt index) =>
        spReflectionTypeLayout_getBindingRangeBindingCount(_ptr, index);

    public readonly SlangInt GetFieldBindingRangeOffset(SlangInt fieldIndex) =>
        spReflectionTypeLayout_getFieldBindingRangeOffset(_ptr, fieldIndex);

    public readonly SlangInt ExplicitCounterBindingRangeOffset =>
        spReflectionTypeLayout_getExplicitCounterBindingRangeOffset(_ptr);

    public readonly TypeLayoutReflection GetBindingRangeLeafTypeLayout(SlangInt index) =>
        new(spReflectionTypeLayout_getBindingRangeLeafTypeLayout(_ptr, index), _component);

    public readonly VariableReflection GetBindingRangeLeafVariable(SlangInt index) =>
        new(spReflectionTypeLayout_getBindingRangeLeafVariable(_ptr, index), _component);

    public readonly SlangImageFormat GetBindingRangeImageFormat(SlangInt index) =>
        spReflectionTypeLayout_getBindingRangeImageFormat(_ptr, index);

    public readonly SlangInt GetBindingRangeDescriptorSetIndex(SlangInt index) =>
        spReflectionTypeLayout_getBindingRangeDescriptorSetIndex(_ptr, index);

    public readonly SlangInt GetBindingRangeFirstDescriptorRangeIndex(SlangInt index) =>
        spReflectionTypeLayout_getBindingRangeFirstDescriptorRangeIndex(_ptr, index);

    public readonly SlangInt GetBindingRangeDescriptorRangeCount(SlangInt index) =>
        spReflectionTypeLayout_getBindingRangeDescriptorRangeCount(_ptr, index);

    public readonly SlangInt DescriptorSetCount =>
        spReflectionTypeLayout_getDescriptorSetCount(_ptr);

    public readonly SlangInt GetDescriptorSetSpaceOffset(SlangInt setIndex) =>
        spReflectionTypeLayout_getDescriptorSetSpaceOffset(_ptr, setIndex);

    public readonly SlangInt GetDescriptorSetDescriptorRangeCount(SlangInt setIndex) =>
        spReflectionTypeLayout_getDescriptorSetDescriptorRangeCount(_ptr, setIndex);

    public readonly SlangInt GetDescriptorSetDescriptorRangeIndexOffset(SlangInt setIndex, SlangInt rangeIndex) =>
        spReflectionTypeLayout_getDescriptorSetDescriptorRangeIndexOffset(_ptr, setIndex, rangeIndex);

    public readonly SlangInt GetDescriptorSetDescriptorRangeDescriptorCount(SlangInt setIndex, SlangInt rangeIndex) =>
        spReflectionTypeLayout_getDescriptorSetDescriptorRangeDescriptorCount(_ptr, setIndex, rangeIndex);

    public readonly SlangBindingType GetDescriptorSetDescriptorRangeType(SlangInt setIndex, SlangInt rangeIndex) =>
        spReflectionTypeLayout_getDescriptorSetDescriptorRangeType(_ptr, setIndex, rangeIndex);

    public readonly SlangParameterCategory GetDescriptorSetDescriptorRangeCategory(SlangInt setIndex, SlangInt rangeIndex) =>
        spReflectionTypeLayout_getDescriptorSetDescriptorRangeCategory(_ptr, setIndex, rangeIndex);

    public readonly SlangInt SubObjectRangeCount =>
        spReflectionTypeLayout_getSubObjectRangeCount(_ptr);

    public readonly SlangInt GetSubObjectRangeBindingRangeIndex(SlangInt subObjectRangeIndex) =>
        spReflectionTypeLayout_getSubObjectRangeBindingRangeIndex(_ptr, subObjectRangeIndex);

    public readonly SlangInt GetSubObjectRangeSpaceOffset(SlangInt subObjectRangeIndex) =>
        spReflectionTypeLayout_getSubObjectRangeSpaceOffset(_ptr, subObjectRangeIndex);

    public readonly VariableLayoutReflection GetSubObjectRangeOffset(SlangInt subObjectRangeIndex) =>
        new(spReflectionTypeLayout_getSubObjectRangeOffset(_ptr, subObjectRangeIndex), _component);
};
