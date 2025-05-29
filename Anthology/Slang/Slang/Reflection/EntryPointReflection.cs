// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using static Prowl.Slang.Native.SlangNative_Deprecated;


namespace Prowl.Slang;


/// <summary>
/// Provides reflection information for an entry point in a shader module.
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
    /// Gets the original name of the entry point as defined in the shader source.
    /// </summary>
    public readonly string Name =>
        spReflectionEntryPoint_getName(_ptr).String;

    /// <summary>
    /// Gets the overridden name of the entry point, if any.
    /// </summary>
    public readonly string NameOverride =>
        spReflectionEntryPoint_getNameOverride(_ptr).String;

    /// <summary>
    /// Gets the number of parameters for this entry point.
    /// </summary>
    public readonly uint ParameterCount =>
        spReflectionEntryPoint_getParameterCount(_ptr);

    /// <summary>
    /// Gets the function reflection information for this entry point.
    /// </summary>
    public readonly FunctionReflection Function =>
        new(spReflectionEntryPoint_getFunction(_ptr), _component);

    /// <summary>
    /// Gets the variable layout reflection for a parameter at the specified index.
    /// </summary>
    /// <param name="index">The zero-based index of the parameter.</param>
    /// <returns>A <see cref="VariableLayoutReflection"/> object containing the parameter's layout information.</returns>
    public readonly VariableLayoutReflection GetParameterByIndex(uint index) =>
        new(spReflectionEntryPoint_getParameterByIndex(_ptr, index), _component);

    /// <summary>
    /// Gets an enumerable collection of all parameters for this entry point.
    /// </summary>
    public readonly IEnumerable<VariableLayoutReflection> Parameters =>
        Utility.For(ParameterCount, GetParameterByIndex);

    /// <summary>
    /// Gets the shader stage this entry point is designed for (e.g., vertex, fragment, compute).
    /// </summary>
    public readonly ShaderStage Stage =>
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
    /// If this entrypoint is a compute or pixel kernel, gets the amount of lanes in a single wavefront.
    /// </summary>
    public readonly uint GetComputeWaveSize()
    {
        spReflectionEntryPoint_getComputeWaveSize(_ptr, out nuint outWaveSize);
        return (uint)outWaveSize;
    }

    /// <summary>
    /// Determines whether this entry point uses any sample-rate input.
    /// </summary>
    /// <returns>True if the entry point uses sample-rate input; otherwise, false.</returns>
    public readonly bool UsesAnySampleRateInput =>
        spReflectionEntryPoint_usesAnySampleRateInput(_ptr) != 0;

    /// <summary>
    /// Gets the variable layout reflection information for this entry point.
    /// </summary>
    public readonly VariableLayoutReflection VarLayout =>
        new(spReflectionEntryPoint_getVarLayout(_ptr), _component);

    /// <summary>
    /// Gets the type layout reflection information for this entry point.
    /// </summary>
    public readonly TypeLayoutReflection TypeLayout =>
        VarLayout.TypeLayout;

    /// <summary>
    /// Gets the variable layout reflection information for the result of this entry point.
    /// </summary>
    public readonly VariableLayoutReflection ResultVarLayout =>
        new(spReflectionEntryPoint_getResultVarLayout(_ptr), _component);

    /// <summary>
    /// Determines whether this entry point has a default constant buffer.
    /// </summary>
    /// <returns>True if the entry point has a default constant buffer; otherwise, false.</returns>
    public readonly bool HasDefaultConstantBuffer =>
        spReflectionEntryPoint_hasDefaultConstantBuffer(_ptr) != 0;
}
