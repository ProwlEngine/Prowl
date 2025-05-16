using System.Runtime.InteropServices;

using SlangInt = nint;
using SlangUInt = nuint;

namespace Prowl.Slang.Native;


// Functions officialy deprecated in the slang repo but still required for reflection.
internal static unsafe partial class SlangNative_Deprecated
{
    const string LibName = "slang";

    // User Attribute
    [LibraryImport(LibName)]
    public static unsafe partial ConstU8Str spReflectionUserAttribute_GetName(Attribute* attrib);

    [LibraryImport(LibName)]
    public static unsafe partial uint spReflectionUserAttribute_GetArgumentCount(
        Attribute* attrib);

    [LibraryImport(LibName)]
    public static unsafe partial TypeReflection* spReflectionUserAttribute_GetArgumentType(
        Attribute* attrib,
        uint index);

    [LibraryImport(LibName)]
    public static unsafe partial SlangResult spReflectionUserAttribute_GetArgumentValueInt(
        Attribute* attrib,
        uint index,
        int* rs);

    [LibraryImport(LibName)]
    public static unsafe partial SlangResult spReflectionUserAttribute_GetArgumentValueFloat(
        Attribute* attrib,
        uint index,
        float* rs);

    /** Returns the string-typed value of a user attribute argument
        The string returned is not null-terminated. The length of the string is returned via
       `outSize`. If index of out of range, or if the specified argument is not a string, the
       function will return nullptr.
    */
    [LibraryImport(LibName)]
    public static unsafe partial ConstU8Str spReflectionUserAttribute_GetArgumentValueString(
        Attribute* attrib,
        uint index,
        out nuint outSize);

    // Type Reflection

    [LibraryImport(LibName)]
    public static unsafe partial SlangTypeKind spReflectionType_GetKind(TypeReflection* type);
    [LibraryImport(LibName)]
    public static unsafe partial uint spReflectionType_GetUserAttributeCount(TypeReflection* type);
    [LibraryImport(LibName)]
    public static unsafe partial Attribute* spReflectionType_GetUserAttribute(
        TypeReflection* type,
        uint index);

    [LibraryImport(LibName)]
    public static unsafe partial Attribute* spReflectionType_FindUserAttributeByName(
        TypeReflection* type,
        ConstU8Str name);

    [LibraryImport(LibName)]
    public static unsafe partial TypeReflection* spReflectionType_applySpecializations(
        TypeReflection* type,
        GenericReflection* generic);

    [LibraryImport(LibName)]
    public static unsafe partial uint spReflectionType_GetFieldCount(TypeReflection* type);

    [LibraryImport(LibName)]
    public static unsafe partial VariableReflection* spReflectionType_GetFieldByIndex(
        TypeReflection* type,
        uint index);

    /** Returns the number of elements in the given type.

This operation is valid for vector and array types. For other types it returns zero.

When invoked on an unbounded-size array it will return `SLANG_UNBOUNDED_SIZE`,
which is defined to be `~nuint(0)`.

If the size of a type cannot be statically computed, perhaps because it depends on
a generic parameter that has not been bound to a specific value, this function returns zero.
*/
    [LibraryImport(LibName)]
    public static unsafe partial nuint spReflectionType_GetElementCount(TypeReflection* type);

    [LibraryImport(LibName)]
    public static unsafe partial TypeReflection* spReflectionType_GetElementType(TypeReflection* type);

    [LibraryImport(LibName)]
    public static unsafe partial uint spReflectionType_GetRowCount(TypeReflection* type);
    [LibraryImport(LibName)]
    public static unsafe partial uint spReflectionType_GetColumnCount(TypeReflection* type);
    [LibraryImport(LibName)]
    public static unsafe partial SlangScalarType spReflectionType_GetScalarType(TypeReflection* type);

    [LibraryImport(LibName)]
    public static unsafe partial SlangResourceShape spReflectionType_GetResourceShape(TypeReflection* type);
    [LibraryImport(LibName)]
    public static unsafe partial SlangResourceAccess spReflectionType_GetResourceAccess(TypeReflection* type);
    [LibraryImport(LibName)]
    public static unsafe partial TypeReflection* spReflectionType_GetResourceResultType(
            TypeReflection* type);

    [LibraryImport(LibName)]
    public static unsafe partial ConstU8Str spReflectionType_GetName(TypeReflection* type);

    [LibraryImport(LibName)]
    public static unsafe partial SlangResult
    spReflectionType_GetFullName(TypeReflection* type, out ISlangBlob* outNameBlob);

    [LibraryImport(LibName)]
    public static unsafe partial GenericReflection* spReflectionType_GetGenericContainer(
            TypeReflection* type);

    // Type Layout Reflection

    [LibraryImport(LibName)]
    public static unsafe partial TypeReflection* spReflectionTypeLayout_GetType(TypeLayoutReflection* type);
    [LibraryImport(LibName)]
    public static unsafe partial SlangTypeKind spReflectionTypeLayout_getKind(TypeLayoutReflection* type);
    [LibraryImport(LibName)]
    public static unsafe partial nuint spReflectionTypeLayout_GetSize(
            TypeLayoutReflection* type,
        SlangParameterCategory category);
    [LibraryImport(LibName)]
    public static unsafe partial nuint spReflectionTypeLayout_GetStride(
            TypeLayoutReflection* type,
        SlangParameterCategory category);

    [LibraryImport(LibName)]
    public static unsafe partial int spReflectionTypeLayout_getAlignment(
            TypeLayoutReflection* type,
        SlangParameterCategory category);

    [LibraryImport(LibName)]
    public static unsafe partial uint spReflectionTypeLayout_GetFieldCount(TypeLayoutReflection* type);
    [LibraryImport(LibName)]
    public static unsafe partial VariableLayoutReflection* spReflectionTypeLayout_GetFieldByIndex(
        TypeLayoutReflection* type,
        uint index);

    [LibraryImport(LibName)]
    public static unsafe partial SlangInt spReflectionTypeLayout_findFieldIndexByName(
            TypeLayoutReflection* typeLayout,
        ConstU8Str nameBegin,
        ConstU8Str nameEnd);

    [LibraryImport(LibName)]
    public static unsafe partial VariableLayoutReflection* spReflectionTypeLayout_GetExplicitCounter(
            TypeLayoutReflection* typeLayout);

    [LibraryImport(LibName)]
    public static unsafe partial nuint spReflectionTypeLayout_GetElementStride(
            TypeLayoutReflection* type,
        SlangParameterCategory category);
    [LibraryImport(LibName)]
    public static unsafe partial TypeLayoutReflection* spReflectionTypeLayout_GetElementTypeLayout(
            TypeLayoutReflection* type);
    [LibraryImport(LibName)]
    public static unsafe partial VariableLayoutReflection* spReflectionTypeLayout_GetElementVarLayout(
            TypeLayoutReflection* type);
    [LibraryImport(LibName)]
    public static unsafe partial VariableLayoutReflection* spReflectionTypeLayout_getContainerVarLayout(
            TypeLayoutReflection* type);

    [LibraryImport(LibName)]
    public static unsafe partial SlangParameterCategory
    spReflectionTypeLayout_GetParameterCategory(TypeLayoutReflection* type);

    [LibraryImport(LibName)]
    public static unsafe partial uint spReflectionTypeLayout_GetCategoryCount(TypeLayoutReflection* type);
    [LibraryImport(LibName)]
    public static unsafe partial SlangParameterCategory
    spReflectionTypeLayout_GetCategoryByIndex(TypeLayoutReflection* type, uint index);

    [LibraryImport(LibName)]
    public static unsafe partial SlangMatrixLayoutMode
    spReflectionTypeLayout_GetMatrixLayoutMode(TypeLayoutReflection* type);

    [LibraryImport(LibName)]
    public static unsafe partial int spReflectionTypeLayout_getGenericParamIndex(TypeLayoutReflection* type);

    [LibraryImport(LibName)]
    public static unsafe partial TypeLayoutReflection* spReflectionTypeLayout_getPendingDataTypeLayout(
            TypeLayoutReflection* type);

    [LibraryImport(LibName)]
    public static unsafe partial VariableLayoutReflection*
    spReflectionTypeLayout_getSpecializedTypePendingDataVarLayout(TypeLayoutReflection* type);

    [LibraryImport(LibName)]
    public static unsafe partial SlangInt
    spReflectionTypeLayout_getBindingRangeCount(TypeLayoutReflection* typeLayout);
    [LibraryImport(LibName)]
    public static unsafe partial SlangBindingType spReflectionTypeLayout_getBindingRangeType(
            TypeLayoutReflection* typeLayout,
        SlangInt index);
    [LibraryImport(LibName)]
    public static unsafe partial SlangInt spReflectionTypeLayout_isBindingRangeSpecializable(
            TypeLayoutReflection* typeLayout,
        SlangInt index);
    [LibraryImport(LibName)]
    public static unsafe partial SlangInt spReflectionTypeLayout_getBindingRangeBindingCount(
            TypeLayoutReflection* typeLayout,
        SlangInt index);
    [LibraryImport(LibName)]
    public static unsafe partial TypeLayoutReflection* spReflectionTypeLayout_getBindingRangeLeafTypeLayout(
            TypeLayoutReflection* typeLayout,
        SlangInt index);
    [LibraryImport(LibName)]
    public static unsafe partial VariableReflection* spReflectionTypeLayout_getBindingRangeLeafVariable(
            TypeLayoutReflection* typeLayout,
        SlangInt index);
    [LibraryImport(LibName)]
    public static unsafe partial SlangImageFormat spReflectionTypeLayout_getBindingRangeImageFormat(
            TypeLayoutReflection* typeLayout,
        SlangInt index);
    [LibraryImport(LibName)]
    public static unsafe partial SlangInt spReflectionTypeLayout_getFieldBindingRangeOffset(
            TypeLayoutReflection* typeLayout,
        SlangInt fieldIndex);
    [LibraryImport(LibName)]
    public static unsafe partial SlangInt spReflectionTypeLayout_getExplicitCounterBindingRangeOffset(
            TypeLayoutReflection* inTypeLayout);

    [LibraryImport(LibName)]
    public static unsafe partial SlangInt spReflectionTypeLayout_getBindingRangeDescriptorSetIndex(
            TypeLayoutReflection* typeLayout,
        SlangInt index);
    [LibraryImport(LibName)]
    public static unsafe partial SlangInt spReflectionTypeLayout_getBindingRangeFirstDescriptorRangeIndex(
            TypeLayoutReflection* typeLayout,
        SlangInt index);
    [LibraryImport(LibName)]
    public static unsafe partial SlangInt spReflectionTypeLayout_getBindingRangeDescriptorRangeCount(
            TypeLayoutReflection* typeLayout,
        SlangInt index);

    [LibraryImport(LibName)]
    public static unsafe partial SlangInt
    spReflectionTypeLayout_getDescriptorSetCount(TypeLayoutReflection* typeLayout);
    [LibraryImport(LibName)]
    public static unsafe partial SlangInt spReflectionTypeLayout_getDescriptorSetSpaceOffset(
            TypeLayoutReflection* typeLayout,
        SlangInt setIndex);
    [LibraryImport(LibName)]
    public static unsafe partial SlangInt spReflectionTypeLayout_getDescriptorSetDescriptorRangeCount(
            TypeLayoutReflection* typeLayout,
        SlangInt setIndex);
    [LibraryImport(LibName)]
    public static unsafe partial SlangInt spReflectionTypeLayout_getDescriptorSetDescriptorRangeIndexOffset(
            TypeLayoutReflection* typeLayout,
        SlangInt setIndex,
        SlangInt rangeIndex);
    [LibraryImport(LibName)]
    public static unsafe partial SlangInt spReflectionTypeLayout_getDescriptorSetDescriptorRangeDescriptorCount(
            TypeLayoutReflection* typeLayout,
        SlangInt setIndex,
        SlangInt rangeIndex);
    [LibraryImport(LibName)]
    public static unsafe partial SlangBindingType spReflectionTypeLayout_getDescriptorSetDescriptorRangeType(
            TypeLayoutReflection* typeLayout,
        SlangInt setIndex,
        SlangInt rangeIndex);
    [LibraryImport(LibName)]
    public static unsafe partial SlangParameterCategory spReflectionTypeLayout_getDescriptorSetDescriptorRangeCategory(
            TypeLayoutReflection* typeLayout,
        SlangInt setIndex,
        SlangInt rangeIndex);

    [LibraryImport(LibName)]
    public static unsafe partial SlangInt spReflectionTypeLayout_getSubObjectRangeCount(TypeLayoutReflection* typeLayout);

    [LibraryImport(LibName)]
    public static unsafe partial SlangInt spReflectionTypeLayout_getSubObjectRangeBindingRangeIndex(
        TypeLayoutReflection* typeLayout,
        SlangInt subObjectRangeIndex);

    [LibraryImport(LibName)]
    public static unsafe partial SlangInt spReflectionTypeLayout_getSubObjectRangeSpaceOffset(
        TypeLayoutReflection* typeLayout,
        SlangInt subObjectRangeIndex);

    [LibraryImport(LibName)]
    public static unsafe partial VariableLayoutReflection* spReflectionTypeLayout_getSubObjectRangeOffset(
        TypeLayoutReflection* typeLayout,
        SlangInt subObjectRangeIndex);

    // Variable Reflection

    [LibraryImport(LibName)]
    public static unsafe partial ConstU8Str spReflectionVariable_GetName(VariableReflection* var);

    [LibraryImport(LibName)]
    public static unsafe partial TypeReflection* spReflectionVariable_GetType(VariableReflection* var);

    [LibraryImport(LibName)]
    public static unsafe partial Modifier* spReflectionVariable_FindModifier(
        VariableReflection* var,
        SlangModifierID modifierID);

    [LibraryImport(LibName)]
    public static unsafe partial uint spReflectionVariable_GetUserAttributeCount(VariableReflection* var);

    [LibraryImport(LibName)]
    public static unsafe partial Attribute* spReflectionVariable_GetUserAttribute(
        VariableReflection* var,
        uint index);

    [LibraryImport(LibName)]
    public static unsafe partial Attribute* spReflectionVariable_FindUserAttributeByName(
        VariableReflection* var,
        IGlobalSession* globalSession,
        ConstU8Str name);

    [LibraryImport(LibName)]
    public static unsafe partial CBool spReflectionVariable_HasDefaultValue(VariableReflection* inVar);

    [LibraryImport(LibName)]
    public static unsafe partial SlangResult spReflectionVariable_GetDefaultValueInt(VariableReflection* inVar, out long rs);

    [LibraryImport(LibName)]
    public static unsafe partial GenericReflection* spReflectionVariable_GetGenericContainer(
            VariableReflection* var);

    [LibraryImport(LibName)]
    public static unsafe partial VariableReflection* spReflectionVariable_applySpecializations(
        VariableReflection* var,
        GenericReflection* generic);

    // Variable Layout Reflection

    [LibraryImport(LibName)]
    public static unsafe partial VariableReflection* spReflectionVariableLayout_GetVariable(
            VariableLayoutReflection* var);

    [LibraryImport(LibName)]
    public static unsafe partial TypeLayoutReflection* spReflectionVariableLayout_GetTypeLayout(
            VariableLayoutReflection* var);

    [LibraryImport(LibName)]
    public static unsafe partial nuint spReflectionVariableLayout_GetOffset(
            VariableLayoutReflection* var,
        SlangParameterCategory category);

    [LibraryImport(LibName)]
    public static unsafe partial nuint spReflectionVariableLayout_GetSpace(
            VariableLayoutReflection* var,
        SlangParameterCategory category);

    [LibraryImport(LibName)]
    public static unsafe partial SlangImageFormat
    spReflectionVariableLayout_GetImageFormat(VariableLayoutReflection* var);

    [LibraryImport(LibName)]
    public static unsafe partial ConstU8Str spReflectionVariableLayout_GetSemanticName(
        VariableLayoutReflection* var);

    [LibraryImport(LibName)]
    public static unsafe partial nuint
    spReflectionVariableLayout_GetSemanticIndex(VariableLayoutReflection* var);


    // Function Reflection
    [LibraryImport(LibName)]
    public static unsafe partial ConstU8Str spReflectionFunction_GetName(FunctionReflection* func);

    [LibraryImport(LibName)]
    public static unsafe partial Modifier* spReflectionFunction_FindModifier(
        FunctionReflection* var,
        SlangModifierID modifierID);

    [LibraryImport(LibName)]
    public static unsafe partial uint spReflectionFunction_GetUserAttributeCount(
        FunctionReflection* func);

    [LibraryImport(LibName)]
    public static unsafe partial Attribute* spReflectionFunction_GetUserAttribute(
        FunctionReflection* func,
        uint index);

    [LibraryImport(LibName)]
    public static unsafe partial Attribute* spReflectionFunction_FindUserAttributeByName(
        FunctionReflection* func,
        IGlobalSession* globalSession,
        ConstU8Str name);

    [LibraryImport(LibName)]
    public static unsafe partial uint spReflectionFunction_GetParameterCount(FunctionReflection* func);

    [LibraryImport(LibName)]
    public static unsafe partial VariableReflection* spReflectionFunction_GetParameter(
            FunctionReflection* func,
        uint index);

    [LibraryImport(LibName)]
    public static unsafe partial TypeReflection* spReflectionFunction_GetResultType(
            FunctionReflection* func);

    [LibraryImport(LibName)]
    public static unsafe partial GenericReflection* spReflectionFunction_GetGenericContainer(
            FunctionReflection* func);

    [LibraryImport(LibName)]
    public static unsafe partial FunctionReflection* spReflectionFunction_applySpecializations(
        FunctionReflection* func,
        GenericReflection* generic);

    [LibraryImport(LibName)]
    public static unsafe partial FunctionReflection* spReflectionFunction_specializeWithArgTypes(
        FunctionReflection* func,
        SlangInt argTypeCount,
        TypeReflection** argTypes);

    [LibraryImport(LibName)]
    public static unsafe partial CBool spReflectionFunction_isOverloaded(FunctionReflection* func);

    [LibraryImport(LibName)]
    public static unsafe partial uint spReflectionFunction_getOverloadCount(FunctionReflection* func);

    [LibraryImport(LibName)]
    public static unsafe partial FunctionReflection* spReflectionFunction_getOverload(
            FunctionReflection* func,
        uint index);

    // Abstract Decl Reflection

    [LibraryImport(LibName)]
    public static unsafe partial uint spReflectionDecl_getChildrenCount(DeclReflection* parentDecl);
    [LibraryImport(LibName)]
    public static unsafe partial DeclReflection* spReflectionDecl_getChild(
            DeclReflection* parentDecl,
        uint index);
    [LibraryImport(LibName)]
    public static unsafe partial ConstU8Str spReflectionDecl_getName(DeclReflection* decl);
    [LibraryImport(LibName)]
    public static unsafe partial SlangDeclKind spReflectionDecl_getKind(DeclReflection* decl);
    [LibraryImport(LibName)]
    public static unsafe partial FunctionReflection* spReflectionDecl_castToFunction(DeclReflection* decl);
    [LibraryImport(LibName)]
    public static unsafe partial VariableReflection* spReflectionDecl_castToVariable(DeclReflection* decl);
    [LibraryImport(LibName)]
    public static unsafe partial GenericReflection* spReflectionDecl_castToGeneric(DeclReflection* decl);
    [LibraryImport(LibName)]
    public static unsafe partial TypeReflection* spReflection_getTypeFromDecl(DeclReflection* decl);
    [LibraryImport(LibName)]
    public static unsafe partial DeclReflection* spReflectionDecl_getParent(DeclReflection* decl);

    // Generic Reflection

    [LibraryImport(LibName)]
    public static unsafe partial DeclReflection* spReflectionGeneric_asDecl(GenericReflection* generic);
    [LibraryImport(LibName)]
    public static unsafe partial ConstU8Str spReflectionGeneric_GetName(GenericReflection* generic);
    [LibraryImport(LibName)]
    public static unsafe partial uint spReflectionGeneric_GetTypeParameterCount(
            GenericReflection* generic);
    [LibraryImport(LibName)]
    public static unsafe partial VariableReflection* spReflectionGeneric_GetTypeParameter(
            GenericReflection* generic,
        uint index);
    [LibraryImport(LibName)]
    public static unsafe partial uint spReflectionGeneric_GetValueParameterCount(
            GenericReflection* generic);
    [LibraryImport(LibName)]
    public static unsafe partial VariableReflection* spReflectionGeneric_GetValueParameter(
            GenericReflection* generic,
        uint index);
    [LibraryImport(LibName)]
    public static unsafe partial uint spReflectionGeneric_GetTypeParameterConstraintCount(
            GenericReflection* generic,
        VariableReflection* typeParam);
    [LibraryImport(LibName)]
    public static unsafe partial TypeReflection* spReflectionGeneric_GetTypeParameterConstraintType(
            GenericReflection* generic,
        VariableReflection* typeParam,
        uint index);
    [LibraryImport(LibName)]
    public static unsafe partial SlangDeclKind spReflectionGeneric_GetInnerKind(GenericReflection* generic);
    [LibraryImport(LibName)]
    public static unsafe partial DeclReflection* spReflectionGeneric_GetInnerDecl(
            GenericReflection* generic);
    [LibraryImport(LibName)]
    public static unsafe partial GenericReflection* spReflectionGeneric_GetOuterGenericContainer(
            GenericReflection* generic);
    [LibraryImport(LibName)]
    public static unsafe partial TypeReflection* spReflectionGeneric_GetConcreteType(
            GenericReflection* generic,
        VariableReflection* typeParam);
    [LibraryImport(LibName)]
    public static unsafe partial long spReflectionGeneric_GetConcreteIntVal(
            GenericReflection* generic,
        VariableReflection* valueParam);
    [LibraryImport(LibName)]
    public static unsafe partial GenericReflection* spReflectionGeneric_applySpecializations(
            GenericReflection* currGeneric,
        GenericReflection* generic);


    /** Get the stage that a variable belongs to (if any).

A variable "belongs" to a specific stage when it is a varying input/output
parameter either defined as part of the parameter list for an entry
point *or* at the global scope of a stage-specific GLSL code file (e.g.,
an `in` parameter in a GLSL `.vs` file belongs to the vertex stage).
*/
    [LibraryImport(LibName)]
    public static unsafe partial SlangStage spReflectionVariableLayout_getStage(VariableLayoutReflection* var);


    [LibraryImport(LibName)]
    public static unsafe partial VariableLayoutReflection* spReflectionVariableLayout_getPendingDataLayout(
            VariableLayoutReflection* var);

    // Shader Parameter Reflection

    [LibraryImport(LibName)]
    public static unsafe partial uint spReflectionParameter_GetBindingIndex(VariableLayoutReflection* parameter);
    [LibraryImport(LibName)]
    public static unsafe partial uint spReflectionParameter_GetBindingSpace(VariableLayoutReflection* parameter);

    // Entry Point Reflection

    [LibraryImport(LibName)]
    public static unsafe partial ConstU8Str spReflectionEntryPoint_getName(EntryPointReflection* entryPoint);

    [LibraryImport(LibName)]
    public static unsafe partial ConstU8Str spReflectionEntryPoint_getNameOverride(
        EntryPointReflection* entryPoint);

    [LibraryImport(LibName)]
    public static unsafe partial FunctionReflection* spReflectionEntryPoint_getFunction(
            EntryPointReflection* entryPoint);

    [LibraryImport(LibName)]
    public static unsafe partial uint spReflectionEntryPoint_getParameterCount(
            EntryPointReflection* entryPoint);

    [LibraryImport(LibName)]
    public static unsafe partial VariableLayoutReflection* spReflectionEntryPoint_getParameterByIndex(
        EntryPointReflection* entryPoint,
        uint index);

    [LibraryImport(LibName)]
    public static unsafe partial SlangStage spReflectionEntryPoint_getStage(EntryPointReflection* entryPoint);

    [LibraryImport(LibName)]
    public static unsafe partial void spReflectionEntryPoint_getComputeThreadGroupSize(
        EntryPointReflection* entryPoint,
        SlangUInt axisCount,
        SlangUInt* outSizeAlongAxis);

    [LibraryImport(LibName)]
    public static unsafe partial void spReflectionEntryPoint_getComputeWaveSize(
        EntryPointReflection* entryPoint,
        SlangUInt* outWaveSize);

    [LibraryImport(LibName)]
    public static unsafe partial int spReflectionEntryPoint_usesAnySampleRateInput(
        EntryPointReflection* entryPoint);

    [LibraryImport(LibName)]
    public static unsafe partial VariableLayoutReflection* spReflectionEntryPoint_getVarLayout(
        EntryPointReflection* entryPoint);

    [LibraryImport(LibName)]
    public static unsafe partial VariableLayoutReflection* spReflectionEntryPoint_getResultVarLayout(
        EntryPointReflection* entryPoint);

    [LibraryImport(LibName)]
    public static unsafe partial int spReflectionEntryPoint_hasDefaultConstantBuffer(
        EntryPointReflection* entryPoint);

    // TypeParameterReflection
    [LibraryImport(LibName)]
    public static unsafe partial ConstU8Str spReflectionTypeParameter_GetName(
        TypeParameterReflection* typeParam);
    [LibraryImport(LibName)]
    public static unsafe partial uint spReflectionTypeParameter_GetIndex(TypeParameterReflection* typeParam);
    [LibraryImport(LibName)]
    public static unsafe partial uint spReflectionTypeParameter_GetConstraintCount(
            TypeParameterReflection* typeParam);
    [LibraryImport(LibName)]
    public static unsafe partial TypeReflection* spReflectionTypeParameter_GetConstraintByIndex(
            TypeParameterReflection* typeParam,
        uint index);

    // Shader Reflection

    [LibraryImport(LibName)]
    public static unsafe partial SlangResult spReflection_ToJson(
        ShaderReflection* reflection,
        void* request,
        out ISlangBlob* outBlob);

    [LibraryImport(LibName)]
    public static unsafe partial uint spReflection_GetParameterCount(ShaderReflection* reflection);
    [LibraryImport(LibName)]
    public static unsafe partial VariableLayoutReflection* spReflection_GetParameterByIndex(
            ShaderReflection* reflection,
        uint index);

    [LibraryImport(LibName)]
    public static unsafe partial uint spReflection_GetTypeParameterCount(ShaderReflection* reflection);

    [LibraryImport(LibName)]
    public static unsafe partial ISession* spReflection_GetSession(ShaderReflection* reflection);

    [LibraryImport(LibName)]
    public static unsafe partial TypeParameterReflection* spReflection_GetTypeParameterByIndex(
            ShaderReflection* reflection,
        uint index);
    [LibraryImport(LibName)]
    public static unsafe partial TypeParameterReflection* spReflection_FindTypeParameter(
            ShaderReflection* reflection,
            ConstU8Str name);

    [LibraryImport(LibName)]
    public static unsafe partial TypeReflection* spReflection_FindTypeByName(
            ShaderReflection* reflection,
            ConstU8Str name);
    [LibraryImport(LibName)]
    public static unsafe partial TypeLayoutReflection* spReflection_GetTypeLayout(
            ShaderReflection* reflection,
        TypeReflection* reflectionType,
        SlangLayoutRules rules);

    [LibraryImport(LibName)]
    public static unsafe partial FunctionReflection* spReflection_FindFunctionByName(
            ShaderReflection* reflection,
            ConstU8Str name);
    [LibraryImport(LibName)]
    public static unsafe partial FunctionReflection* spReflection_FindFunctionByNameInType(
            ShaderReflection* reflection,
        TypeReflection* reflType,
            ConstU8Str name);
    [LibraryImport(LibName)]
    public static unsafe partial VariableReflection* spReflection_FindVarByNameInType(
            ShaderReflection* reflection,
        TypeReflection* reflType,
            ConstU8Str name);

    [LibraryImport(LibName)]
    public static unsafe partial SlangUInt spReflection_getEntryPointCount(ShaderReflection* reflection);
    [LibraryImport(LibName)]
    public static unsafe partial EntryPointReflection* spReflection_getEntryPointByIndex(
            ShaderReflection* reflection,
        SlangUInt index);
    [LibraryImport(LibName)]
    public static unsafe partial EntryPointReflection* spReflection_findEntryPointByName(
            ShaderReflection* reflection,
            ConstU8Str name);

    [LibraryImport(LibName)]
    public static unsafe partial SlangUInt spReflection_getGlobalConstantBufferBinding(ShaderReflection* reflection);
    [LibraryImport(LibName)]
    public static unsafe partial nuint spReflection_getGlobalConstantBufferSize(ShaderReflection* reflection);

    [LibraryImport(LibName)]
    public static unsafe partial TypeReflection* spReflection_specializeType(
        ShaderReflection* reflection,
        TypeReflection* type,
        SlangInt specializationArgCount,
        TypeReflection** specializationArgs,
        out ISlangBlob* outDiagnostics);

    [LibraryImport(LibName)]
    public static unsafe partial GenericReflection* spReflection_specializeGeneric(
            ShaderReflection* inProgramLayout,
        GenericReflection* generic,
        SlangInt argCount,
        SlangReflectionGenericArgType* argTypes,
        GenericArgReflection* args,
        out ISlangBlob* outDiagnostics);

    [LibraryImport(LibName)]
    public static unsafe partial CBool spReflection_isSubType(
        ShaderReflection* reflection,
        TypeReflection* subType,
        TypeReflection* superType);

    /// Get the number of hashed strings
    [LibraryImport(LibName)]
    public static unsafe partial SlangUInt spReflection_getHashedStringCount(ShaderReflection* reflection);

    /// Get a hashed string. The number of chars is written in outCount.
    /// The count does *NOT* including terminating 0. The returned string will be 0 terminated.
    [LibraryImport(LibName)]
    public static unsafe partial ConstU8Str spReflection_getHashedString(
        ShaderReflection* reflection,
        SlangUInt index,
        nuint* outCount);

    /// Get a type layout representing reflection information for the global-scope parameters.
    [LibraryImport(LibName)]
    public static unsafe partial TypeLayoutReflection* spReflection_getGlobalParamsTypeLayout(
            ShaderReflection* reflection);

    /// Get a variable layout representing reflection information for the global-scope parameters.
    [LibraryImport(LibName)]
    public static unsafe partial VariableLayoutReflection* spReflection_getGlobalParamsVarLayout(
            ShaderReflection* reflection);
}
