using System;
using System.Collections.Generic;

using static Prowl.Slang.Native.SlangNative_Deprecated;


namespace Prowl.Slang;


/// <summary>
/// Reflection information for a shader entrypoint defined in a module.
/// </summary>
public unsafe struct EntryPointReflection
{
    internal ComponentType _component;
    internal Native.EntryPointReflection* _ptr;


    internal EntryPointReflection(Native.EntryPointReflection* ptr, ComponentType component)
    {
        ArgumentNullException.ThrowIfNull(ptr, nameof(ptr));

        _component = component;
        _ptr = ptr;
    }


    /// <summary>
    /// The name of the entrypoint.
    /// </summary>
    public readonly string Name =>
        spReflectionEntryPoint_getName(_ptr).String;

    /// <summary>
    /// The name override for this <see cref="EntryPointReflection"/>
    /// </summary>
    public readonly string NameOverride =>
        spReflectionEntryPoint_getNameOverride(_ptr).String;

    /// <summary>
    /// The number of variable parameters in this entrypoint.
    /// </summary>
    public readonly uint ParameterCount =>
        spReflectionEntryPoint_getParameterCount(_ptr);

    /// <summary>
    /// The inner <see cref="FunctionReflection"/> of this entrypoint.
    /// </summary>
    public readonly FunctionReflection Function =>
        new(spReflectionEntryPoint_getFunction(_ptr), _component);

    /// <summary>
    /// Returns the <see cref="VariableLayoutReflection"/> in this entrypoint's parameter list at the given index.
    /// </summary>
    public readonly VariableLayoutReflection GetParameterByIndex(uint index) =>
        new(spReflectionEntryPoint_getParameterByIndex(_ptr, index), _component);

    /// <summary>
    /// The parameters in this entrypoint.
    /// </summary>
    public readonly IEnumerable<VariableLayoutReflection> Parameters =>
        Utility.For(ParameterCount, GetParameterByIndex);

    /// <summary>
    /// The shader stage this entrypoint targets.
    /// </summary>
    public readonly SlangStage Stage =>
        spReflectionEntryPoint_getStage(_ptr);

    /// <summary>
    /// If this entrypoint is a compute kernel, gets the threadgroup dimensions for this kernel in an (x, y, z) tuple.
    /// </summary>
    public readonly (uint, uint, uint) GetComputeThreadGroupSize()
    {
        nuint* sizes = stackalloc nuint[3];
        spReflectionEntryPoint_getComputeThreadGroupSize(_ptr, 3, sizes);
        return ((uint)sizes[0], (uint)sizes[1], (uint)sizes[2]);
    }

    /// <summary>
    /// If this entrypoint is a compute or pixel kernel, gets the amount of lanes in a single wave/warp/wavefront.
    /// </summary>
    public readonly uint GetComputeWaveSize()
    {
        spReflectionEntryPoint_getComputeWaveSize(_ptr, out nuint outWaveSize);
        return (uint)outWaveSize;
    }

    /// <summary>
    /// Indicates if this entrypoint accepts an input of any sample rate.
    /// </summary>
    public readonly bool UsesAnySampleRateInput =>
        spReflectionEntryPoint_usesAnySampleRateInput(_ptr) != 0;

    /// <summary>
    /// The variable layout for this entrypoint.
    /// </summary>
    public readonly VariableLayoutReflection VarLayout =>
        new(spReflectionEntryPoint_getVarLayout(_ptr), _component);

    /// <summary>
    /// The type layout information for this entrypoint.
    /// </summary>
    public readonly TypeLayoutReflection TypeLayout =>
        VarLayout.TypeLayout;

    /// <summary>
    /// The result variable information for this entrypoint.
    /// </summary>
    public readonly VariableLayoutReflection ResultVarLayout =>
        new(spReflectionEntryPoint_getResultVarLayout(_ptr), _component);

    /// <summary>
    /// Indicates if this entrypoint uses a default constant buffer (global cbuffer in HLSL).
    /// </summary>
    public readonly bool HasDefaultConstantBuffer =>
        spReflectionEntryPoint_hasDefaultConstantBuffer(_ptr) != 0;
}
