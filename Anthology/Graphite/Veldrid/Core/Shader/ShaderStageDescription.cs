using System;

namespace Prowl.Veldrid;

/// <summary>
/// Describes a single compiled shader stage that forms part of a <see cref="ShaderDescription"/>.
/// </summary>
public struct ShaderStageDescription : IEquatable<ShaderStageDescription>
{
    /// <summary>
    /// The shader stage this entry describes.
    /// </summary>
    public ShaderStages Stage;

    /// <summary>
    /// An array containing the raw shader bytes for this stage.
    /// For Direct3D11 shaders, this array must contain HLSL bytecode or HLSL text.
    /// For Vulkan shaders, this array must contain SPIR-V bytecode.
    /// For OpenGL and OpenGL ES shaders, this array must contain the ASCII-encoded text of the shader code.
    /// </summary>
    public byte[] ShaderBytes;

    /// <summary>
    /// The name of the entry point function in the shader module for this stage.
    /// </summary>
    public string EntryPoint;

    /// <summary>
    /// Indicates whether the shader should be debuggable. Only effective when <see cref="ShaderBytes"/> contains
    /// shader code that will be compiled.
    /// </summary>
    public bool Debug;

    /// <summary>
    /// Constructs a new <see cref="ShaderStageDescription"/>.
    /// </summary>
    /// <param name="stage">The shader stage.</param>
    /// <param name="shaderBytes">An array containing the raw shader bytes.</param>
    /// <param name="entryPoint">The name of the entry point function in the shader module.</param>
    public ShaderStageDescription(ShaderStages stage, byte[] shaderBytes, string entryPoint)
    {
        Stage = stage;
        ShaderBytes = shaderBytes;
        EntryPoint = entryPoint;
        Debug = false;
    }

    /// <summary>
    /// Constructs a new <see cref="ShaderStageDescription"/>.
    /// </summary>
    /// <param name="stage">The shader stage.</param>
    /// <param name="shaderBytes">An array containing the raw shader bytes.</param>
    /// <param name="entryPoint">The name of the entry point function in the shader module.</param>
    /// <param name="debug">Whether the shader should be debuggable.</param>
    public ShaderStageDescription(ShaderStages stage, byte[] shaderBytes, string entryPoint, bool debug)
    {
        Stage = stage;
        ShaderBytes = shaderBytes;
        EntryPoint = entryPoint;
        Debug = debug;
    }

    /// <summary>
    /// Element-wise equality.
    /// </summary>
    public bool Equals(ShaderStageDescription other)
    {
        return Stage == other.Stage
            && ShaderBytes == other.ShaderBytes
            && string.Equals(EntryPoint, other.EntryPoint)
            && Debug == other.Debug;
    }

    /// <summary>
    /// Returns the hash code for this instance.
    /// </summary>
    public override int GetHashCode()
    {
        return HashCode.Combine(
            (int)Stage,
            ShaderBytes?.GetHashCode() ?? 0,
            EntryPoint?.GetHashCode() ?? 0,
            Debug);
    }
}
