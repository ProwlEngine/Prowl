using System;

namespace Prowl.Graphite;

/// <summary>
/// Compiled shader stage.
/// </summary>
public struct ShaderStageDescription : IEquatable<ShaderStageDescription>
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
    /// Debug flag.
    /// </summary>
    public bool Debug;

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
        Debug = false;
    }

    /// <summary>
    /// New stage.
    /// </summary>
    /// <param name="stage">The stage.</param>
    /// <param name="shaderBytes">Raw shader bytes.</param>
    /// <param name="entryPoint">Entry point function name.</param>
    /// <param name="debug">Debug flag.</param>
    public ShaderStageDescription(ShaderStages stage, byte[] shaderBytes, string entryPoint, bool debug)
    {
        Stage = stage;
        ShaderBytes = shaderBytes;
        EntryPoint = entryPoint;
        Debug = debug;
    }

    /// <summary>
    /// Field-by-field equality.
    /// </summary>
    public readonly bool Equals(ShaderStageDescription other)
    {
        return Stage == other.Stage
            && ShaderBytes == other.ShaderBytes
            && string.Equals(EntryPoint, other.EntryPoint)
            && Debug == other.Debug;
    }

    /// <summary>
    /// Hash code.
    /// </summary>
    public override readonly int GetHashCode()
    {
        return HashCode.Combine(
            (int)Stage,
            ShaderBytes?.GetHashCode() ?? 0,
            EntryPoint?.GetHashCode() ?? 0,
            Debug);
    }
}
