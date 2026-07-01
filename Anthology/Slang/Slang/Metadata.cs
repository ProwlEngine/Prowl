// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Runtime.CompilerServices;

using Prowl.Slang.Native;


namespace Prowl.Slang;


/// <summary>
/// Metadata for compilation targets.
/// </summary>
public class Metadata : IEquatable<Metadata>
{
    internal IMetadata _metadata;


    internal Metadata(IMetadata metadata)
    {
        _metadata = metadata;
    }



    /// <summary>
    /// Determines if a resource parameter at the specified binding location is actually being used
    /// in the compiled shader.
    /// </summary>
    /// <param name="category">Is this a `t` register? `s` register?</param>
    /// <param name="spaceIndex">`space` for D3D12, `set` for Vulkan.</param>
    /// <param name="registerIndex">`register` for D3D12, `binding` for Vulkan.</param>
    /// <returns>True if the resource is used in the shader, False otherwise.</returns>
    public bool IsParameterLocationUsed(ParameterCategory category, uint spaceIndex, uint registerIndex)
    {
        _metadata.IsParameterLocationUsed(category, spaceIndex, registerIndex, out CBool outUsed)
            .Throw("Failure checking parameter location usage");

        return outUsed;
    }


    /// <inheritdoc/>
    public bool Equals(Metadata? other)
    {
        if (other != null)
            return Unsafe.As<NativeComProxy>(_metadata).Equals(Unsafe.As<NativeComProxy>(other._metadata));

        return false;
    }


    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        if (obj is Metadata other)
            return Equals(other);

        return false;
    }

    /// <inheritdoc/>
    public override int GetHashCode() => (int)Unsafe.As<NativeComProxy>(_metadata).ComPtr;
}
