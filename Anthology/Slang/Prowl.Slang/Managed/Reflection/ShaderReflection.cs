using System;

using Prowl.Slang.Native;

using static Prowl.Slang.Native.SlangNative_Deprecated;
using System.Collections.Generic;


namespace Prowl.Slang;


public unsafe struct ShaderReflection
{
    internal Session _session;
    internal Native.ShaderReflection* _ptr;


    internal ShaderReflection(Native.ShaderReflection* ptr, Session session)
    {
        ArgumentNullException.ThrowIfNull(ptr, nameof(ptr));

        _session = session;
        _ptr = ptr;
    }

    public uint ParameterCount =>
        spReflection_GetParameterCount(_ptr);

    public uint TypeParameterCount =>
        spReflection_GetTypeParameterCount(_ptr);

    public Session Session =>
        _session;

    public TypeParameterReflection GetTypeParameterByIndex(uint index) =>
        new(spReflection_GetTypeParameterByIndex(_ptr, index), _session);

    public IEnumerable<TypeParameterReflection> TypeParameters =>
        Utility.For(TypeParameterCount, GetTypeParameterByIndex);

    public TypeParameterReflection FindTypeParameter(string name)
    {
        using U8Str str = U8Str.Alloc(name);
        return new(spReflection_FindTypeParameter(_ptr, str), _session);
    }

    public VariableLayoutReflection GetParameterByIndex(uint index) =>
        new(spReflection_GetParameterByIndex(_ptr, index), _session);

    public IEnumerable<VariableLayoutReflection> Parameters =>
        Utility.For(ParameterCount, GetParameterByIndex);

    public uint EntryPointCount =>
        (uint)spReflection_getEntryPointCount(_ptr);

    public EntryPointReflection GetEntryPointByIndex(uint index) =>
        new(spReflection_getEntryPointByIndex(_ptr, index), _session);

    public IEnumerable<EntryPointReflection> EntryPoints =>
        Utility.For(EntryPointCount, GetEntryPointByIndex);

    public nuint GlobalConstantBufferBinding =>
        spReflection_getGlobalConstantBufferBinding(_ptr);

    public nuint GlobalConstantBufferSize =>
        spReflection_getGlobalConstantBufferSize(_ptr);

    public TypeReflection FindTypeByName(string name)
    {
        using U8Str str = U8Str.Alloc(name);
        return new(spReflection_FindTypeByName(_ptr, str), _session);
    }

    public FunctionReflection FindFunctionByName(string name)
    {
        using U8Str str = U8Str.Alloc(name);
        return new(spReflection_FindFunctionByName(_ptr, str), _session);
    }

    public FunctionReflection FindFunctionByNameInType(TypeReflection type, string name)
    {
        using U8Str str = U8Str.Alloc(name);
        return new(spReflection_FindFunctionByNameInType(_ptr, type._ptr, str), _session);
    }

    public VariableReflection FindVarByNameInType(TypeReflection type, string name)
    {
        using U8Str str = U8Str.Alloc(name);
        return new(spReflection_FindVarByNameInType(_ptr, type._ptr, str), _session);
    }

    public TypeLayoutReflection GetTypeLayout(TypeReflection type, SlangLayoutRules rules) =>
        new(spReflection_GetTypeLayout(_ptr, type._ptr, rules), _session);

    public EntryPointReflection FindEntryPointByName(string name)
    {
        using U8Str str = U8Str.Alloc(name);
        return new(spReflection_findEntryPointByName(_ptr, str), _session);
    }

    public TypeReflection SpecializeType(
        TypeReflection type,
        TypeReflection[] specializationArgs,
        out string? diagnostics)
    {
        Native.TypeReflection** specializationArgsPtr = stackalloc Native.TypeReflection*[specializationArgs.Length];

        for (int i = 0; i < specializationArgs.Length; i++)
            specializationArgsPtr[i] = specializationArgs[i]._ptr;

        Native.TypeReflection* reflectionPtr = spReflection_specializeType(
            _ptr,
            type._ptr,
            specializationArgs.Length,
            specializationArgsPtr,
            out ISlangBlob* diagnosticsPtr);

        diagnostics = NativeComProxy.Create(diagnosticsPtr).GetString();

        return new(reflectionPtr, _session);
    }

    public GenericReflection SpecializeGeneric(
        GenericReflection generic,
        (SlangReflectionGenericArgType, GenericArgReflection)[] specializationArgs,
        out string? diagnostics)
    {
        SlangReflectionGenericArgType* specializationArgTypes = stackalloc SlangReflectionGenericArgType[specializationArgs.Length];
        Native.GenericArgReflection* specializationArgVals = stackalloc Native.GenericArgReflection[specializationArgs.Length];

        for (int i = 0; i < specializationArgs.Length; i++)
        {
            specializationArgTypes[i] = specializationArgs[i].Item1;
            specializationArgVals[i] = specializationArgs[i].Item2.ToNative();
        }

        Native.GenericReflection* genericPtr = spReflection_specializeGeneric(
            _ptr,
            generic._ptr,
            specializationArgs.Length,
            specializationArgTypes,
            specializationArgVals,
            out ISlangBlob* diagnosticsPtr);

        diagnostics = NativeComProxy.Create(diagnosticsPtr).GetString();

        return new(genericPtr, _session);
    }

    public bool IsSubType(TypeReflection subType, TypeReflection superType) =>
        spReflection_isSubType(_ptr, subType._ptr, superType._ptr);

    public nuint HashedStringCount =>
        spReflection_getHashedStringCount(_ptr);

    public string GetHashedString(nuint index, out nuint outCount) =>
        spReflection_getHashedString(_ptr, index, out outCount).String;

    public TypeLayoutReflection GlobalParamsTypeLayout =>
        new(spReflection_getGlobalParamsTypeLayout(_ptr), _session);

    public VariableLayoutReflection GlobalParamsVarLayout =>
        new(spReflection_getGlobalParamsVarLayout(_ptr), _session);

    public string ToJson()
    {
        spReflection_ToJson(_ptr, null, out ISlangBlob* outJsonPtr).Throw();

        return NativeComProxy.Create(outJsonPtr).GetString();
    }
}
