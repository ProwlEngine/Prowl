using System;

using SlangInt = nint;

using Prowl.Slang.Native;

using static Prowl.Slang.Native.SlangNative_Deprecated;
using System.Collections.Generic;


namespace Prowl.Slang;


public unsafe struct TypeLayoutReflection
{
    internal Session _session;
    internal Native.TypeLayoutReflection* _ptr;


    internal TypeLayoutReflection(Native.TypeLayoutReflection* ptr, Session session)
    {
        ArgumentNullException.ThrowIfNull(ptr, nameof(ptr));

        _session = session;
        _ptr = ptr;
    }


    public TypeReflection ReflectionType =>
        new(spReflectionTypeLayout_GetType(_ptr), _session);

    public SlangTypeKind Kind =>
        spReflectionTypeLayout_getKind(_ptr);

    public nuint GetSize(SlangParameterCategory category) =>
        spReflectionTypeLayout_GetSize(_ptr, category);

    public nuint GetStride(SlangParameterCategory category) =>
        spReflectionTypeLayout_GetStride(_ptr, category);

    public int GetAlignment(SlangParameterCategory category) =>
        spReflectionTypeLayout_getAlignment(_ptr, category);

    public uint FieldCount =>
        spReflectionTypeLayout_GetFieldCount(_ptr);

    public VariableLayoutReflection GetFieldByIndex(uint index) =>
        new(spReflectionTypeLayout_GetFieldByIndex(_ptr, index), _session);

    public IEnumerable<VariableLayoutReflection> Fields =>
        Utility.For(FieldCount, GetFieldByIndex);

    public SlangInt FindFieldIndexByName(string nameBegin, string nameEnd)
    {
        using U8Str strA = U8Str.Alloc(nameBegin);
        using U8Str strB = U8Str.Alloc(nameEnd);

        return spReflectionTypeLayout_findFieldIndexByName(_ptr, strA, strB);
    }

    public VariableLayoutReflection ExplicitCounter =>
        new(spReflectionTypeLayout_GetExplicitCounter(_ptr), _session);

    public bool IsArray =>
        ReflectionType.IsArray;

    public TypeLayoutReflection UnwrapArray()
    {
        TypeLayoutReflection typeLayout = this;

        while (typeLayout.IsArray)
            typeLayout = typeLayout.ElementTypeLayout;

        return typeLayout;
    }

    // only useful if `getKind() == Kind::Array`
    public nuint ElementCount =>
        ReflectionType.ElementCount;

    public nuint TotalArrayElementCount =>
        ReflectionType.GetTotalArrayElementCount();

    public nuint GetElementStride(SlangParameterCategory category) =>
        spReflectionTypeLayout_GetElementStride(_ptr, category);

    public TypeLayoutReflection ElementTypeLayout =>
        new(spReflectionTypeLayout_GetElementTypeLayout(_ptr), _session);

    public VariableLayoutReflection ElementVarLayout =>
        new(spReflectionTypeLayout_GetElementVarLayout(_ptr), _session);

    public VariableLayoutReflection ContainerVarLayout =>
        new(spReflectionTypeLayout_getContainerVarLayout(_ptr), _session);

    // How is this type supposed to be bound?
    public SlangParameterCategory ParameterCategory =>
        spReflectionTypeLayout_GetParameterCategory(_ptr);

    public uint CategoryCount =>
        spReflectionTypeLayout_GetCategoryCount(_ptr);

    public SlangParameterCategory GetCategoryByIndex(uint index) =>
        spReflectionTypeLayout_GetCategoryByIndex(_ptr, index);

    public IEnumerable<SlangParameterCategory> Categories =>
        Utility.For(CategoryCount, GetCategoryByIndex);

    public uint RowCount =>
        ReflectionType.RowCount;

    public uint ColumnCount =>
        ReflectionType.ColumnCount;

    public SlangScalarType ScalarType =>
        ReflectionType.ScalarType;

    public TypeReflection ResourceResultType =>
        ReflectionType.ResourceResultType;

    public SlangResourceShape ResourceShape =>
        ReflectionType.ResourceShape;

    public SlangResourceAccess ResourceAccess =>
        ReflectionType.ResourceAccess;

    public string Name =>
        ReflectionType.Name;

    public SlangMatrixLayoutMode MatrixLayoutMode =>
        spReflectionTypeLayout_GetMatrixLayoutMode(_ptr);

    public int GenericParamIndex =>
        spReflectionTypeLayout_getGenericParamIndex(_ptr);

    public TypeLayoutReflection PendingDataTypeLayout =>
        new(spReflectionTypeLayout_getPendingDataTypeLayout(_ptr), _session);

    public VariableLayoutReflection SpecializedTypePendingDataVarLayout =>
        new(spReflectionTypeLayout_getSpecializedTypePendingDataVarLayout(_ptr), _session);

    public SlangInt BindingRangeCount =>
        spReflectionTypeLayout_getBindingRangeCount(_ptr);

    public SlangBindingType GetBindingRangeType(SlangInt index) =>
        spReflectionTypeLayout_getBindingRangeType(_ptr, index);

    public bool IsBindingRangeSpecializable(SlangInt index) =>
        spReflectionTypeLayout_isBindingRangeSpecializable(_ptr, index) == 1;

    public SlangInt GetBindingRangeBindingCount(SlangInt index) =>
        spReflectionTypeLayout_getBindingRangeBindingCount(_ptr, index);

    public SlangInt GetFieldBindingRangeOffset(SlangInt fieldIndex) =>
        spReflectionTypeLayout_getFieldBindingRangeOffset(_ptr, fieldIndex);

    public SlangInt ExplicitCounterBindingRangeOffset =>
        spReflectionTypeLayout_getExplicitCounterBindingRangeOffset(_ptr);

    public TypeLayoutReflection GetBindingRangeLeafTypeLayout(SlangInt index) =>
        new(spReflectionTypeLayout_getBindingRangeLeafTypeLayout(_ptr, index), _session);

    public VariableReflection GetBindingRangeLeafVariable(SlangInt index) =>
        new(spReflectionTypeLayout_getBindingRangeLeafVariable(_ptr, index), _session);

    public SlangImageFormat GetBindingRangeImageFormat(SlangInt index) =>
        spReflectionTypeLayout_getBindingRangeImageFormat(_ptr, index);

    public SlangInt GetBindingRangeDescriptorSetIndex(SlangInt index) =>
        spReflectionTypeLayout_getBindingRangeDescriptorSetIndex(_ptr, index);

    public SlangInt GetBindingRangeFirstDescriptorRangeIndex(SlangInt index) =>
        spReflectionTypeLayout_getBindingRangeFirstDescriptorRangeIndex(_ptr, index);

    public SlangInt GetBindingRangeDescriptorRangeCount(SlangInt index) =>
        spReflectionTypeLayout_getBindingRangeDescriptorRangeCount(_ptr, index);

    public SlangInt DescriptorSetCount =>
        spReflectionTypeLayout_getDescriptorSetCount(_ptr);

    public SlangInt GetDescriptorSetSpaceOffset(SlangInt setIndex) =>
        spReflectionTypeLayout_getDescriptorSetSpaceOffset(_ptr, setIndex);

    public SlangInt GetDescriptorSetDescriptorRangeCount(SlangInt setIndex) =>
        spReflectionTypeLayout_getDescriptorSetDescriptorRangeCount(_ptr, setIndex);

    public SlangInt GetDescriptorSetDescriptorRangeIndexOffset(SlangInt setIndex, SlangInt rangeIndex) =>
        spReflectionTypeLayout_getDescriptorSetDescriptorRangeIndexOffset(_ptr, setIndex, rangeIndex);

    public SlangInt GetDescriptorSetDescriptorRangeDescriptorCount(SlangInt setIndex, SlangInt rangeIndex) =>
        spReflectionTypeLayout_getDescriptorSetDescriptorRangeDescriptorCount(_ptr, setIndex, rangeIndex);

    public SlangBindingType GetDescriptorSetDescriptorRangeType(SlangInt setIndex, SlangInt rangeIndex) =>
        spReflectionTypeLayout_getDescriptorSetDescriptorRangeType(_ptr, setIndex, rangeIndex);

    public SlangParameterCategory GetDescriptorSetDescriptorRangeCategory(SlangInt setIndex, SlangInt rangeIndex) =>
        spReflectionTypeLayout_getDescriptorSetDescriptorRangeCategory(_ptr, setIndex, rangeIndex);

    public SlangInt SubObjectRangeCount =>
        spReflectionTypeLayout_getSubObjectRangeCount(_ptr);

    public SlangInt GetSubObjectRangeBindingRangeIndex(SlangInt subObjectRangeIndex) =>
        spReflectionTypeLayout_getSubObjectRangeBindingRangeIndex(_ptr, subObjectRangeIndex);

    public SlangInt GetSubObjectRangeSpaceOffset(SlangInt subObjectRangeIndex) =>
        spReflectionTypeLayout_getSubObjectRangeSpaceOffset(_ptr, subObjectRangeIndex);

    public VariableLayoutReflection GetSubObjectRangeOffset(SlangInt subObjectRangeIndex) =>
        new(spReflectionTypeLayout_getSubObjectRangeOffset(_ptr, subObjectRangeIndex), _session);
};
