using System;

namespace Prowl.Graphite;

/// <summary>
/// Compiled shader stage.
/// </summary>
public struct ShaderStageDescription
{
    /// <summary>
    /// Which stage this is.
    /// </summary>
    public ShaderStages Stage;

    /// <summary>
    /// Raw shader bytes (Vulkan SPIR-V).
    /// </summary>
    public byte[] ShaderBytes;

    /// <summary>
    /// Entry point function name.
    /// </summary>
    public string EntryPoint;

    /// <summary>
    /// New stage.
    /// </summary>
    /// <param name="stage">The stage.</param>
    /// <param name="shaderBytes">Raw shader bytes.</param>
    /// <param name="entryPoint">Entry point function name.</param>
    public ShaderStageDescription(ShaderStages stage, byte[] shaderBytes, string entryPoint)
    {
        Stage = stage;
        ShaderBytes = shaderBytes;
        EntryPoint = entryPoint;
    }
}
