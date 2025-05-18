using System;

using Prowl.Slang.Native;

using static Prowl.Slang.Native.SlangNative_Deprecated;
using System.Collections.Generic;


namespace Prowl.Slang;


public unsafe struct EntryPointReflection
{
    internal Session _session;
    internal Native.EntryPointReflection* _ptr;


    internal EntryPointReflection(Native.EntryPointReflection* ptr, Session session)
    {
        ArgumentNullException.ThrowIfNull(ptr, nameof(ptr));

        _session = session;
        _ptr = ptr;
    }


    public readonly string Name =>
        spReflectionEntryPoint_getName(_ptr).String;

    public readonly string NameOverride =>
        spReflectionEntryPoint_getNameOverride(_ptr).String;

    public readonly uint ParameterCount =>
        spReflectionEntryPoint_getParameterCount(_ptr);

    public readonly FunctionReflection Function =>
        new(spReflectionEntryPoint_getFunction(_ptr), _session);

    public readonly VariableLayoutReflection GetParameterByIndex(uint index) =>
        new(spReflectionEntryPoint_getParameterByIndex(_ptr, index), _session);

    public IEnumerable<VariableLayoutReflection> Parameters =>
        Utility.For(ParameterCount, GetParameterByIndex);

    public readonly SlangStage Stage =>
        spReflectionEntryPoint_getStage(_ptr);

    public readonly (uint, uint, uint) GetComputeThreadGroupSize()
    {
        nuint* sizes = stackalloc nuint[3];
        spReflectionEntryPoint_getComputeThreadGroupSize(_ptr, 3, sizes);
        return ((uint)sizes[0], (uint)sizes[1], (uint)sizes[2]);
    }

    public readonly void GetComputeWaveSize(out nuint outWaveSize) =>
        spReflectionEntryPoint_getComputeWaveSize(_ptr, out outWaveSize);

    public readonly bool UsesAnySampleRateInput =>
        spReflectionEntryPoint_usesAnySampleRateInput(_ptr) != 0;

    public readonly VariableLayoutReflection VarLayout =>
        new(spReflectionEntryPoint_getVarLayout(_ptr), _session);

    public TypeLayoutReflection TypeLayout =>
        VarLayout.TypeLayout;

    public readonly VariableLayoutReflection ResultVarLayout =>
        new(spReflectionEntryPoint_getResultVarLayout(_ptr), _session);

    public readonly bool HasDefaultConstantBuffer =>
        spReflectionEntryPoint_hasDefaultConstantBuffer(_ptr) != 0;
}
