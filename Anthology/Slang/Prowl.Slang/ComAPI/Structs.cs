using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Collections.Generic;

using SlangInt = nint;
using SlangUInt = nuint;

using static Prowl.Slang.NativeAPI.SlangNative_Dep;

namespace Prowl.Slang.NativeAPI;


[StructLayout(LayoutKind.Sequential)]
public unsafe struct CompilerOptionValue()
{
    public CompilerOptionValueKind kind = CompilerOptionValueKind.Int;
    public int intValue0 = 0;
    public int intValue1 = 0;
    public ConstU8Str stringValue0;
    public ConstU8Str stringValue1;
}


[StructLayout(LayoutKind.Sequential)]
public struct CompilerOptionEntry
{
    CompilerOptionName name;
    CompilerOptionValue value;
}


[StructLayout(LayoutKind.Explicit)]
public unsafe struct GenericArgReflection
{
    [FieldOffset(0)]
    public TypeReflection* typeVal;

    [FieldOffset(0)]
    public long intVal;

    [FieldOffset(0)]
    public CBool boolVal;
}


[StructLayout(LayoutKind.Explicit)]
public unsafe struct Attribute
{
    Attribute* ptr => (Attribute*)Unsafe.AsPointer(ref this);

    public ConstU8Str getName()
    {
        return spReflectionUserAttribute_GetName(ptr);
    }

    public uint getArgumentCount()
    {
        return spReflectionUserAttribute_GetArgumentCount(
            ptr);
    }

    public TypeReflection* getArgumentType(uint index)
    {
        return spReflectionUserAttribute_GetArgumentType(
            ptr,
            index);
    }

    public SlangResult getArgumentValueInt(uint index, int* value)
    {
        return spReflectionUserAttribute_GetArgumentValueInt(
            ptr,
            index,
            value);
    }

    public SlangResult getArgumentValueFloat(uint index, float* value)
    {
        return spReflectionUserAttribute_GetArgumentValueFloat(
            ptr,
            index,
            value);
    }

    public ConstU8Str getArgumentValueString(uint index, out nuint outSize)
    {
        return spReflectionUserAttribute_GetArgumentValueString(
            ptr,
            index,
            out outSize);
    }
}


[StructLayout(LayoutKind.Sequential)]
public unsafe struct TypeReflection
{
    TypeReflection* ptr => (TypeReflection*)Unsafe.AsPointer(ref this);

    public SlangTypeKind getKind() { return spReflectionType_GetKind(ptr); }

    // only useful if `getKind() == Kind::Struct`
    public uint getFieldCount()
    {
        return spReflectionType_GetFieldCount(ptr);
    }

    public VariableReflection* getFieldByIndex(uint index)
    {
        return spReflectionType_GetFieldByIndex(ptr, index);
    }

    public CBool isArray() { return getKind() == SlangTypeKind.ARRAY; }

    public TypeReflection* unwrapArray()
    {
        TypeReflection* type = ptr;
        while (type->isArray())
        {
            type = type->getElementType();
        }
        return type;
    }

    // only useful if `getKind() == Kind::Array`
    public nuint getElementCount()
    {
        return spReflectionType_GetElementCount(ptr);
    }

    public nuint getTotalArrayElementCount()
    {
        if (!isArray())
            return 0;

        nuint result = 1;
        TypeReflection* type = ptr;
        for (; ; )
        {
            if (!type->isArray())
                return result;

            result *= type->getElementCount();
            type = type->getElementType();
        }
    }

    public TypeReflection* getElementType()
    {
        return spReflectionType_GetElementType(ptr);
    }

    public uint getRowCount() { return spReflectionType_GetRowCount(ptr); }

    public uint getColumnCount()
    {
        return spReflectionType_GetColumnCount(ptr);
    }

    public SlangScalarType getScalarType()
    {
        return spReflectionType_GetScalarType(ptr);
    }

    public TypeReflection* getResourceResultType()
    {
        return spReflectionType_GetResourceResultType(ptr);
    }

    public SlangResourceShape getResourceShape()
    {
        return spReflectionType_GetResourceShape(ptr);
    }

    public SlangResourceAccess getResourceAccess()
    {
        return spReflectionType_GetResourceAccess(ptr);
    }

    public ConstU8Str getName()
    {
        return spReflectionType_GetName(ptr);
    }

    public SlangResult getFullName(ISlangBlob** outNameBlob)
    {
        return spReflectionType_GetFullName(ptr, outNameBlob);
    }

    public uint getUserAttributeCount()
    {
        return spReflectionType_GetUserAttributeCount(ptr);
    }

    public Attribute* getUserAttributeByIndex(uint index)
    {
        return spReflectionType_GetUserAttribute(ptr, index);
    }

    public Attribute* findAttributeByName(ConstU8Str name)
    {
        return spReflectionType_FindUserAttributeByName(
            ptr,
            name);
    }

    public Attribute* findUserAttributeByName(ConstU8Str name) { return findAttributeByName(name); }

    public TypeReflection* applySpecializations(GenericReflection* generic)
    {
        return spReflectionType_applySpecializations(
            ptr,
            generic);
    }

    public GenericReflection* getGenericContainer()
    {
        return spReflectionType_GetGenericContainer(ptr);
    }
};


public unsafe struct TypeLayoutReflection
{
    TypeLayoutReflection* ptr => (TypeLayoutReflection*)Unsafe.AsPointer(ref this);

    public TypeReflection* getType()
    {
        return spReflectionTypeLayout_GetType(ptr);
    }

    public SlangTypeKind getKind()
    {
        return spReflectionTypeLayout_getKind(ptr);
    }

    public nuint getSize(SlangParameterCategory category)
    {
        return spReflectionTypeLayout_GetSize(ptr, category);
    }

    public nuint getStride(SlangParameterCategory category)
    {
        return spReflectionTypeLayout_GetStride(ptr, category);
    }

    public int getAlignment(SlangParameterCategory category)
    {
        return spReflectionTypeLayout_getAlignment(ptr, category);
    }

    public uint getFieldCount()
    {
        return spReflectionTypeLayout_GetFieldCount(ptr);
    }

    public VariableLayoutReflection* getFieldByIndex(uint index)
    {
        return spReflectionTypeLayout_GetFieldByIndex(ptr, index);
    }

    public SlangInt findFieldIndexByName(ConstU8Str nameBegin, ConstU8Str nameEnd)
    {
        return spReflectionTypeLayout_findFieldIndexByName(
            ptr,
            nameBegin,
            nameEnd);
    }

    public VariableLayoutReflection* getExplicitCounter()
    {
        return spReflectionTypeLayout_GetExplicitCounter(ptr);
    }

    public CBool isArray() { return getType()->isArray(); }

    public TypeLayoutReflection* unwrapArray()
    {
        TypeLayoutReflection* typeLayout = ptr;

        while (typeLayout->isArray())
            typeLayout = typeLayout->getElementTypeLayout();

        return typeLayout;
    }

    // only useful if `getKind() == Kind::Array`
    public nuint getElementCount() { return getType()->getElementCount(); }

    public nuint getTotalArrayElementCount() { return getType()->getTotalArrayElementCount(); }

    public nuint getElementStride(SlangParameterCategory category)
    {
        return spReflectionTypeLayout_GetElementStride(ptr, category);
    }

    public TypeLayoutReflection* getElementTypeLayout()
    {
        return spReflectionTypeLayout_GetElementTypeLayout(
            ptr);
    }

    public VariableLayoutReflection* getElementVarLayout()
    {
        return spReflectionTypeLayout_GetElementVarLayout(
            ptr);
    }

    public VariableLayoutReflection* getContainerVarLayout()
    {
        return spReflectionTypeLayout_getContainerVarLayout(
            ptr);
    }

    // How is this type supposed to be bound?
    public SlangParameterCategory getParameterCategory()
    {
        return spReflectionTypeLayout_GetParameterCategory(
            ptr);
    }

    public uint getCategoryCount()
    {
        return spReflectionTypeLayout_GetCategoryCount(ptr);
    }

    public SlangParameterCategory getCategoryByIndex(uint index)
    {
        return spReflectionTypeLayout_GetCategoryByIndex(
            ptr,
            index);
    }

    public uint getRowCount() { return getType()->getRowCount(); }

    public uint getColumnCount() { return getType()->getColumnCount(); }

    public SlangScalarType getScalarType() { return getType()->getScalarType(); }

    public TypeReflection* getResourceResultType() { return getType()->getResourceResultType(); }

    public SlangResourceShape getResourceShape() { return getType()->getResourceShape(); }

    public SlangResourceAccess getResourceAccess() { return getType()->getResourceAccess(); }

    public ConstU8Str getName() { return getType()->getName(); }

    public SlangMatrixLayoutMode getMatrixLayoutMode()
    {
        return spReflectionTypeLayout_GetMatrixLayoutMode(ptr);
    }

    public int getGenericParamIndex()
    {
        return spReflectionTypeLayout_getGenericParamIndex(ptr);
    }

    public TypeLayoutReflection* getPendingDataTypeLayout()
    {
        return spReflectionTypeLayout_getPendingDataTypeLayout(ptr);
    }

    public VariableLayoutReflection* getSpecializedTypePendingDataVarLayout()
    {
        return spReflectionTypeLayout_getSpecializedTypePendingDataVarLayout(ptr);
    }

    public SlangInt getBindingRangeCount()
    {
        return spReflectionTypeLayout_getBindingRangeCount(ptr);
    }

    public SlangBindingType getBindingRangeType(SlangInt index)
    {
        return spReflectionTypeLayout_getBindingRangeType(ptr, index);
    }

    public CBool isBindingRangeSpecializable(SlangInt index)
    {
        return spReflectionTypeLayout_isBindingRangeSpecializable(ptr, index) == 1;
    }

    public SlangInt getBindingRangeBindingCount(SlangInt index)
    {
        return spReflectionTypeLayout_getBindingRangeBindingCount(ptr, index);
    }

    public SlangInt getFieldBindingRangeOffset(SlangInt fieldIndex)
    {
        return spReflectionTypeLayout_getFieldBindingRangeOffset(ptr, fieldIndex);
    }

    public SlangInt getExplicitCounterBindingRangeOffset()
    {
        return spReflectionTypeLayout_getExplicitCounterBindingRangeOffset(ptr);
    }

    public TypeLayoutReflection* getBindingRangeLeafTypeLayout(SlangInt index)
    {
        return spReflectionTypeLayout_getBindingRangeLeafTypeLayout(
            ptr,
            index);
    }

    public VariableReflection* getBindingRangeLeafVariable(SlangInt index)
    {
        return spReflectionTypeLayout_getBindingRangeLeafVariable(ptr, index);
    }

    public SlangImageFormat getBindingRangeImageFormat(SlangInt index)
    {
        return spReflectionTypeLayout_getBindingRangeImageFormat(ptr, index);
    }

    public SlangInt getBindingRangeDescriptorSetIndex(SlangInt index)
    {
        return spReflectionTypeLayout_getBindingRangeDescriptorSetIndex(ptr, index);
    }

    public SlangInt getBindingRangeFirstDescriptorRangeIndex(SlangInt index)
    {
        return spReflectionTypeLayout_getBindingRangeFirstDescriptorRangeIndex(ptr, index);
    }

    public SlangInt getBindingRangeDescriptorRangeCount(SlangInt index)
    {
        return spReflectionTypeLayout_getBindingRangeDescriptorRangeCount(
            ptr,
            index);
    }

    public SlangInt getDescriptorSetCount()
    {
        return spReflectionTypeLayout_getDescriptorSetCount(ptr);
    }

    public SlangInt getDescriptorSetSpaceOffset(SlangInt setIndex)
    {
        return spReflectionTypeLayout_getDescriptorSetSpaceOffset(
            ptr,
            setIndex);
    }

    public SlangInt getDescriptorSetDescriptorRangeCount(SlangInt setIndex)
    {
        return spReflectionTypeLayout_getDescriptorSetDescriptorRangeCount(
            ptr,
            setIndex);
    }

    public SlangInt getDescriptorSetDescriptorRangeIndexOffset(SlangInt setIndex, SlangInt rangeIndex)
    {
        return spReflectionTypeLayout_getDescriptorSetDescriptorRangeIndexOffset(
            ptr,
            setIndex,
            rangeIndex);
    }

    public SlangInt getDescriptorSetDescriptorRangeDescriptorCount(SlangInt setIndex, SlangInt rangeIndex)
    {
        return spReflectionTypeLayout_getDescriptorSetDescriptorRangeDescriptorCount(
            ptr,
            setIndex,
            rangeIndex);
    }

    public SlangBindingType getDescriptorSetDescriptorRangeType(SlangInt setIndex, SlangInt rangeIndex)
    {
        return spReflectionTypeLayout_getDescriptorSetDescriptorRangeType(
            ptr,
            setIndex,
            rangeIndex);
    }

    public SlangParameterCategory getDescriptorSetDescriptorRangeCategory(
        SlangInt setIndex,
        SlangInt rangeIndex)
    {
        return spReflectionTypeLayout_getDescriptorSetDescriptorRangeCategory(
            ptr,
            setIndex,
            rangeIndex);
    }

    public SlangInt getSubObjectRangeCount()
    {
        return spReflectionTypeLayout_getSubObjectRangeCount(ptr);
    }

    public SlangInt getSubObjectRangeBindingRangeIndex(SlangInt subObjectRangeIndex)
    {
        return spReflectionTypeLayout_getSubObjectRangeBindingRangeIndex(
            ptr,
            subObjectRangeIndex);
    }

    public SlangInt getSubObjectRangeSpaceOffset(SlangInt subObjectRangeIndex)
    {
        return spReflectionTypeLayout_getSubObjectRangeSpaceOffset(
            ptr,
            subObjectRangeIndex);
    }

    public VariableLayoutReflection* getSubObjectRangeOffset(SlangInt subObjectRangeIndex)
    {
        return spReflectionTypeLayout_getSubObjectRangeOffset(
            ptr,
            subObjectRangeIndex);
    }
};

public unsafe struct Modifier
{
}

public unsafe struct VariableReflection
{
    VariableReflection* ptr => (VariableReflection*)Unsafe.AsPointer(ref this);

    public ConstU8Str getName()
    {
        return spReflectionVariable_GetName(ptr);
    }

    public TypeReflection* getType()
    {
        return spReflectionVariable_GetType(ptr);
    }

    public Modifier* findModifier(SlangModifierID id)
    {
        return spReflectionVariable_FindModifier(ptr, id);
    }

    public uint getUserAttributeCount()
    {
        return spReflectionVariable_GetUserAttributeCount(ptr);
    }

    public Attribute* getUserAttributeByIndex(uint index)
    {
        return spReflectionVariable_GetUserAttribute(
            ptr,
            index);
    }

    public Attribute* findAttributeByName(IGlobalSession* globalSession, ConstU8Str name)
    {
        return spReflectionVariable_FindUserAttributeByName(
            ptr,
            globalSession,
            name);
    }

    public Attribute* findUserAttributeByName(IGlobalSession* globalSession, ConstU8Str name)
    {
        return findAttributeByName(globalSession, name);
    }

    public CBool hasDefaultValue()
    {
        return spReflectionVariable_HasDefaultValue(ptr);
    }

    public SlangResult getDefaultValueInt(long* value)
    {
        return spReflectionVariable_GetDefaultValueInt(ptr, value);
    }

    public GenericReflection* getGenericContainer()
    {
        return spReflectionVariable_GetGenericContainer(ptr);
    }

    public VariableReflection* applySpecializations(GenericReflection* generic)
    {
        return spReflectionVariable_applySpecializations(ptr, generic);
    }
};

public unsafe struct VariableLayoutReflection
{
    VariableLayoutReflection* ptr => (VariableLayoutReflection*)Unsafe.AsPointer(ref this);

    public VariableReflection* getVariable()
    {
        return spReflectionVariableLayout_GetVariable(ptr);
    }

    public ConstU8Str getName()
    {
        return getVariable()->getName();
    }

    public Modifier* findModifier(SlangModifierID id) { return getVariable()->findModifier(id); }

    public TypeLayoutReflection* getTypeLayout()
    {
        return spReflectionVariableLayout_GetTypeLayout(
            ptr);
    }

    public SlangParameterCategory getCategory() { return getTypeLayout()->getParameterCategory(); }

    public uint getCategoryCount() { return getTypeLayout()->getCategoryCount(); }

    public SlangParameterCategory getCategoryByIndex(uint index)
    {
        return getTypeLayout()->getCategoryByIndex(index);
    }


    public nuint getOffset(SlangParameterCategory category)
    {
        return spReflectionVariableLayout_GetOffset(ptr, category);
    }

    public TypeReflection* getType() { return getVariable()->getType(); }

    public uint getBindingIndex()
    {
        return spReflectionParameter_GetBindingIndex(ptr);
    }

    public uint getBindingSpace()
    {
        return spReflectionParameter_GetBindingSpace(ptr);
    }

    public nuint getBindingSpace(SlangParameterCategory category)
    {
        return spReflectionVariableLayout_GetSpace(ptr, category);
    }

    public SlangImageFormat getImageFormat()
    {
        return spReflectionVariableLayout_GetImageFormat(ptr);
    }

    public ConstU8Str getSemanticName()
    {
        return spReflectionVariableLayout_GetSemanticName(ptr);
    }

    public nuint getSemanticIndex()
    {
        return spReflectionVariableLayout_GetSemanticIndex(ptr);
    }

    public SlangStage getStage()
    {
        return spReflectionVariableLayout_getStage(ptr);
    }

    public VariableLayoutReflection* getPendingDataLayout()
    {
        return spReflectionVariableLayout_getPendingDataLayout(
            ptr);
    }
};

public unsafe struct FunctionReflection
{
    FunctionReflection* ptr => (FunctionReflection*)Unsafe.AsPointer(ref this);

    public ConstU8Str getName()
    {
        return spReflectionFunction_GetName(ptr);
    }

    public TypeReflection* getReturnType()
    {
        return spReflectionFunction_GetResultType(ptr);
    }

    public uint getParameterCount()
    {
        return spReflectionFunction_GetParameterCount(ptr);
    }

    public VariableReflection* getParameterByIndex(uint index)
    {
        return spReflectionFunction_GetParameter(
            ptr,
            index);
    }

    public uint getUserAttributeCount()
    {
        return spReflectionFunction_GetUserAttributeCount(ptr);
    }

    public Attribute* getUserAttributeByIndex(uint index)
    {
        return spReflectionFunction_GetUserAttribute(ptr, index);
    }

    public Attribute* findAttributeByName(IGlobalSession* globalSession, ConstU8Str name)
    {
        return spReflectionFunction_FindUserAttributeByName(
            ptr,
            globalSession,
            name);
    }

    public Attribute* findUserAttributeByName(IGlobalSession* globalSession, ConstU8Str name)
    {
        return findAttributeByName(globalSession, name);
    }

    public Modifier* findModifier(SlangModifierID id)
    {
        return spReflectionFunction_FindModifier(
            ptr,
            id);
    }

    public GenericReflection* getGenericContainer()
    {
        return spReflectionFunction_GetGenericContainer(ptr);
    }

    public FunctionReflection* applySpecializations(GenericReflection* generic)
    {
        return spReflectionFunction_applySpecializations(ptr, generic);
    }

    public FunctionReflection* specializeWithArgTypes(uint argCount, TypeReflection** types)
    {
        return spReflectionFunction_specializeWithArgTypes(
            ptr,
            (nint)argCount,
            types);
    }

    public CBool isOverloaded()
    {
        return spReflectionFunction_isOverloaded(ptr);
    }

    public uint getOverloadCount()
    {
        return spReflectionFunction_getOverloadCount(ptr);
    }

    public FunctionReflection* getOverload(uint index)
    {
        return spReflectionFunction_getOverload(
            ptr,
            index);
    }
};

public unsafe struct GenericReflection
{
    GenericReflection* ptr => (GenericReflection*)Unsafe.AsPointer(ref this);

    public DeclReflection* asDecl()
    {
        return spReflectionGeneric_asDecl(ptr);
    }

    public ConstU8Str getName()
    {
        return spReflectionGeneric_GetName(ptr);
    }

    public uint getTypeParameterCount()
    {
        return spReflectionGeneric_GetTypeParameterCount(ptr);
    }

    public VariableReflection* getTypeParameter(uint index)
    {
        return spReflectionGeneric_GetTypeParameter(
            ptr,
            index);
    }

    public uint getValueParameterCount()
    {
        return spReflectionGeneric_GetValueParameterCount(ptr);
    }

    public VariableReflection* getValueParameter(uint index)
    {
        return spReflectionGeneric_GetValueParameter(
            ptr,
            index);
    }

    public uint getTypeParameterConstraintCount(VariableReflection* typeParam)
    {
        return spReflectionGeneric_GetTypeParameterConstraintCount(
            ptr,
            typeParam);
    }

    public TypeReflection* getTypeParameterConstraintType(VariableReflection* typeParam, uint index)
    {
        return spReflectionGeneric_GetTypeParameterConstraintType(
            ptr,
            typeParam,
            index);
    }

    public DeclReflection* getInnerDecl()
    {
        return spReflectionGeneric_GetInnerDecl(ptr);
    }

    public SlangDeclKind getInnerKind()
    {
        return spReflectionGeneric_GetInnerKind(ptr);
    }

    public GenericReflection* getOuterGenericContainer()
    {
        return spReflectionGeneric_GetOuterGenericContainer(
            ptr);
    }

    public TypeReflection* getConcreteType(VariableReflection* typeParam)
    {
        return spReflectionGeneric_GetConcreteType(
            ptr,
            typeParam);
    }

    public long getConcreteIntVal(VariableReflection* valueParam)
    {
        return spReflectionGeneric_GetConcreteIntVal(
            ptr,
            valueParam);
    }

    public GenericReflection* applySpecializations(GenericReflection* generic)
    {
        return spReflectionGeneric_applySpecializations(
            ptr,
            generic);
    }
};

public unsafe struct EntryPointReflection
{
    EntryPointReflection* ptr => (EntryPointReflection*)Unsafe.AsPointer(ref this);

    public ConstU8Str getName()
    {
        return spReflectionEntryPoint_getName(ptr);
    }

    public ConstU8Str getNameOverride()
    {
        return spReflectionEntryPoint_getNameOverride(ptr);
    }

    public uint getParameterCount()
    {
        return spReflectionEntryPoint_getParameterCount(ptr);
    }

    public FunctionReflection* getFunction()
    {
        return spReflectionEntryPoint_getFunction(
            ptr);
    }

    public VariableLayoutReflection* getParameterByIndex(uint index)
    {
        return spReflectionEntryPoint_getParameterByIndex(
            ptr,
            index);
    }

    public SlangStage getStage()
    {
        return spReflectionEntryPoint_getStage(ptr);
    }

    public void getComputeThreadGroupSize(SlangUInt axisCount, SlangUInt* outSizeAlongAxis)
    {
        spReflectionEntryPoint_getComputeThreadGroupSize(
            ptr,
            axisCount,
            outSizeAlongAxis);
    }

    public void getComputeWaveSize(SlangUInt* outWaveSize)
    {
        spReflectionEntryPoint_getComputeWaveSize(
            ptr,
            outWaveSize);
    }

    public CBool usesAnySampleRateInput()
    {
        return spReflectionEntryPoint_usesAnySampleRateInput(ptr) != 0;
    }

    public VariableLayoutReflection* getVarLayout()
    {
        return spReflectionEntryPoint_getVarLayout(
            ptr);
    }

    public TypeLayoutReflection* getTypeLayout() { return getVarLayout()->getTypeLayout(); }

    public VariableLayoutReflection* getResultVarLayout()
    {
        return spReflectionEntryPoint_getResultVarLayout(
            ptr);
    }

    public CBool hasDefaultConstantBuffer()
    {
        return spReflectionEntryPoint_hasDefaultConstantBuffer(ptr) != 0;
    }
};

public unsafe struct TypeParameterReflection
{
    TypeParameterReflection* ptr => (TypeParameterReflection*)Unsafe.AsPointer(ref this);

    public ConstU8Str getName()
    {
        return spReflectionTypeParameter_GetName(ptr);
    }
    public uint getIndex()
    {
        return spReflectionTypeParameter_GetIndex(ptr);
    }

    public uint getConstraintCount()
    {
        return spReflectionTypeParameter_GetConstraintCount(ptr);
    }

    public TypeReflection* getConstraintByIndex(uint index)
    {
        return spReflectionTypeParameter_GetConstraintByIndex(
            ptr,
            index);
    }
};


public unsafe struct ShaderReflection
{
    ShaderReflection* ptr => (ShaderReflection*)Unsafe.AsPointer(ref this);

    public uint getParameterCount() { return spReflection_GetParameterCount(ptr); }

    public uint getTypeParameterCount()
    {
        return spReflection_GetTypeParameterCount(ptr);
    }

    public ISession* getSession() { return spReflection_GetSession(ptr); }

    public TypeParameterReflection* getTypeParameterByIndex(uint index)
    {
        return spReflection_GetTypeParameterByIndex(
            ptr,
            index);
    }

    public TypeParameterReflection* findTypeParameter(ConstU8Str name)
    {
        return
            spReflection_FindTypeParameter(ptr, name);
    }

    public VariableLayoutReflection* getParameterByIndex(uint index)
    {
        return spReflection_GetParameterByIndex(
            ptr,
            index);
    }

    public static ShaderReflection* get(ICompileRequest* request)
    {
        return spGetReflection(request);
    }

    public SlangUInt getEntryPointCount()
    {
        return spReflection_getEntryPointCount(ptr);
    }

    public EntryPointReflection* getEntryPointByIndex(SlangUInt index)
    {
        return
            spReflection_getEntryPointByIndex(ptr, index);
    }

    public SlangUInt getGlobalConstantBufferBinding()
    {
        return spReflection_getGlobalConstantBufferBinding(ptr);
    }

    public nuint getGlobalConstantBufferSize()
    {
        return spReflection_getGlobalConstantBufferSize(ptr);
    }

    public TypeReflection* findTypeByName(ConstU8Str name)
    {
        return spReflection_FindTypeByName(ptr, name);
    }

    public FunctionReflection* findFunctionByName(ConstU8Str name)
    {
        return spReflection_FindFunctionByName(ptr, name);
    }

    public FunctionReflection* findFunctionByNameInType(TypeReflection* type, ConstU8Str name)
    {
        return spReflection_FindFunctionByNameInType(
            ptr,
            type,
            name);
    }

    public VariableReflection* findVarByNameInType(TypeReflection* type, ConstU8Str name)
    {
        return spReflection_FindVarByNameInType(
            ptr,
            type,
            name);
    }

    public TypeLayoutReflection* getTypeLayout(
        TypeReflection* type,
        SlangLayoutRules rules)
    {
        return spReflection_GetTypeLayout(
            ptr,
            type,
            rules);
    }

    public EntryPointReflection* findEntryPointByName(ConstU8Str name)
    {
        return
            spReflection_findEntryPointByName(ptr, name);
    }

    public TypeReflection* specializeType(
        TypeReflection* type,
        SlangInt specializationArgCount,
        TypeReflection** specializationArgs,
        out ISlangBlob* outDiagnostics)
    {
        return spReflection_specializeType(
            ptr,
            type,
            specializationArgCount,
            specializationArgs,
            out outDiagnostics);
    }

    public GenericReflection* specializeGeneric(
        GenericReflection* generic,
        SlangInt specializationArgCount,
        SlangReflectionGenericArgType* specializationArgTypes,
        GenericArgReflection* specializationArgVals,
        out ISlangBlob* outDiagnostics)
    {
        return spReflection_specializeGeneric(
            ptr,
            generic,
            specializationArgCount,
            specializationArgTypes,
            specializationArgVals,
            out outDiagnostics);
    }

    public CBool isSubType(TypeReflection* subType, TypeReflection* superType)
    {
        return spReflection_isSubType(
            ptr,
            subType,
            superType);
    }

    public SlangUInt getHashedStringCount()
    {
        return spReflection_getHashedStringCount(ptr);
    }

    public ConstU8Str getHashedString(SlangUInt index, nuint* outCount)
    {
        return spReflection_getHashedString(ptr, index, outCount);
    }

    public TypeLayoutReflection* getGlobalParamsTypeLayout()
    {
        return spReflection_getGlobalParamsTypeLayout(
            ptr);
    }

    public VariableLayoutReflection* getGlobalParamsVarLayout()
    {
        return spReflection_getGlobalParamsVarLayout(
            ptr);
    }

    public SlangResult toJson(out ISlangBlob* outBlob)
    {
        return spReflection_ToJson(ptr, null, out outBlob);
    }
};


public unsafe struct DeclReflection
{
    DeclReflection* ptr => (DeclReflection*)Unsafe.AsPointer(ref this);

    public ConstU8Str getName()
    {
        return spReflectionDecl_getName(ptr);
    }

    public SlangDeclKind getKind() { return spReflectionDecl_getKind(ptr); }

    public uint getChildrenCount()
    {
        return spReflectionDecl_getChildrenCount(ptr);
    }

    public DeclReflection* getChild(uint index)
    {
        return spReflectionDecl_getChild(ptr, index);
    }

    public TypeReflection* getType()
    {
        return spReflection_getTypeFromDecl(ptr);
    }

    public VariableReflection* asVariable()
    {
        return spReflectionDecl_castToVariable(ptr);
    }

    public FunctionReflection* asFunction()
    {
        return spReflectionDecl_castToFunction(ptr);
    }

    public GenericReflection* asGeneric()
    {
        return spReflectionDecl_castToGeneric(ptr);
    }

    public DeclReflection* getParent()
    {
        return spReflectionDecl_getParent(ptr);
    }


    public List<nint> GetChildrenOfKind(SlangDeclKind kind)
    {
        List<nint> children = new();

        for (uint i = 0; i < getChildrenCount(); i++)
        {
            DeclReflection* child = getChild(i);

            if (child->getKind() != kind)
                continue;

            children.Add((nint)getChild(i));
        }

        return children;
    }


    public List<nint> GetChildren()
    {
        List<nint> children = new();

        for (uint i = 0; i < getChildrenCount(); i++)
            children.Add((nint)getChild(i));

        return children;
    }
};


[StructLayout(LayoutKind.Sequential)]
public unsafe struct SpecializationArg
{
    public enum Kind : int
    {
        Unknown, /**< An invalid specialization argument. */
        Type,    /**< Specialize to a type. */
    };

    /** The kind of specialization argument. */
    public Kind kind;

    /** A type specialization argument, used for `Kind::Type`. */
    public TypeReflection* type;

    public static SpecializationArg fromType(TypeReflection* inType)
    {
        SpecializationArg rs;
        rs.kind = Kind.Type;
        rs.type = inType;
        return rs;
    }
};


public struct CompileCoreModuleFlag
{
}


/** Description of a code generation target.
 */
[StructLayout(LayoutKind.Sequential)]
public unsafe struct TargetDesc()
{
    /** The size of this structure, in bytes.
     */
    public nuint structureSize = (nuint)sizeof(TargetDesc);

    /** The target format to generate code for (e.g., SPIR-V, DXIL, etc.)
     */
    public SlangCompileTarget format = SlangCompileTarget.TARGET_UNKNOWN;

    /** The compilation profile supported by the target (e.g., "Shader Model 5.1")
     */
    public SlangProfileID profile = SlangProfileID.UNKNOWN;

    /** Flags for the code generation target. Currently unused. */
    public SlangTargetFlags flags = SlangTargetFlags.Default;

    /** Default mode to use for floating-point operations on the target.
     */
    public SlangFloatingPointMode floatingPointMode = SlangFloatingPointMode.DEFAULT;

    /** The line directive mode for output source code.
     */
    public SlangLineDirectiveMode lineDirectiveMode = SlangLineDirectiveMode.DEFAULT;

    /** Whether to force `scalar` layout for glsl shader storage buffers.
     */
    public CBool forceGLSLScalarBufferLayout = false;

    /** Pointer to an array of compiler option entries, whose size is compilerOptionEntryCount.
     */
    public CompilerOptionEntry* compilerOptionEntries = null;

    /** Number of additional compiler option entries.
     */
    public uint compilerOptionEntryCount = 0;
};


[StructLayout(LayoutKind.Sequential)]
public unsafe struct PreprocessorMacroDesc
{
    public ConstU8Str name;
    public ConstU8Str value;
};


[StructLayout(LayoutKind.Sequential)]
public unsafe struct SessionDesc()
{
    /** The size of this structure, in bytes.
     */
    public nuint structureSize = (nuint)sizeof(SessionDesc);

    /** Code generation targets to include in the session.
     */
    public TargetDesc* targets = null;
    public SlangInt targetCount = 0;

    /** Flags to configure the session.
     */
    public SessionFlags flags = SessionFlags.None;

    /** Default layout to assume for variables with matrix types.
     */
    public SlangMatrixLayoutMode defaultMatrixLayoutMode = SlangMatrixLayoutMode.ROW_MAJOR;

    /** Paths to use when searching for `#include`d or `import`ed files.
     */
    public ConstU8Str* searchPaths = null;
    public SlangInt searchPathCount = 0;

    public PreprocessorMacroDesc* preprocessorMacros = null;
    public SlangInt preprocessorMacroCount = 0;

    public ISlangFileSystem* fileSystem = null;

    public CBool enableEffectAnnotations = false;
    public CBool allowGLSLSyntax = false;

    /** Pointer to an array of compiler option entries, whose size is compilerOptionEntryCount.
     */
    public CompilerOptionEntry* compilerOptionEntries = null;

    /** Number of additional compiler option entries.
     */
    public uint compilerOptionEntryCount = 0;
};


/* Description of a Slang global session.
 */
[StructLayout(LayoutKind.Sequential)]
public unsafe struct SlangGlobalSessionDesc()
{
    /// Size of this struct.
    private uint _structureSize = (uint)sizeof(SlangGlobalSessionDesc);

    /// Slang API version.
    public uint ApiVersion = 0;

    /// Slang language version.
    public uint LanguageVersion = 2025;

    /// Whether to enable GLSL support.
    public CBool EnableGLSL = false;

    /// Reserved for future use.
    private unsafe fixed uint _reserved[16];
};
