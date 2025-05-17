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


    public string Name =>
        spReflectionEntryPoint_getName(_ptr).String;

    public string NameOverride =>
        spReflectionEntryPoint_getNameOverride(_ptr).String;

    public uint ParameterCount =>
        spReflectionEntryPoint_getParameterCount(_ptr);

    public FunctionReflection Function =>
        new(spReflectionEntryPoint_getFunction(_ptr), _session);

    public VariableLayoutReflection GetParameterByIndex(uint index) =>
        new(spReflectionEntryPoint_getParameterByIndex(_ptr, index), _session);

    public IEnumerable<VariableLayoutReflection> Parameters =>
        Utility.For(ParameterCount, GetParameterByIndex);

    public SlangStage Stage =>
        spReflectionEntryPoint_getStage(_ptr);

    public (uint, uint, uint) GetComputeThreadGroupSize()
    {
        nuint* sizes = stackalloc nuint[3];
        spReflectionEntryPoint_getComputeThreadGroupSize(_ptr, 3, sizes);
        return ((uint)sizes[0], (uint)sizes[1], (uint)sizes[2]);
    }

    public void GetComputeWaveSize(out nuint outWaveSize) =>
        spReflectionEntryPoint_getComputeWaveSize(_ptr, out outWaveSize);

    public bool UsesAnySampleRateInput =>
        spReflectionEntryPoint_usesAnySampleRateInput(_ptr) != 0;

    public VariableLayoutReflection VarLayout =>
        new(spReflectionEntryPoint_getVarLayout(_ptr), _session);

    public TypeLayoutReflection TypeLayout =>
        VarLayout.TypeLayout;

    public VariableLayoutReflection ResultVarLayout =>
        new(spReflectionEntryPoint_getResultVarLayout(_ptr), _session);

    public bool HasDefaultConstantBuffer =>
        spReflectionEntryPoint_hasDefaultConstantBuffer(_ptr) != 0;
}
