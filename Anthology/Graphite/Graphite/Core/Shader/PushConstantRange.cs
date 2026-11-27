namespace Prowl.Graphite;

/// <summary>
/// The push constant block a program declares, one range from offset 0 seen by the given stages. Size 0 means none.
/// </summary>
public record struct PushConstantRange
{
    /// <summary>Stages that read the block.</summary>
    public ShaderStages Stages;

    /// <summary>Block size, a multiple of 4 and at most <see cref="CommandBuffer.PushConstantCapacity"/>.</summary>
    public uint SizeInBytes;

    /// <summary>Creates a range.</summary>
    public PushConstantRange(ShaderStages stages, uint sizeInBytes)
    {
        Stages = stages;
        SizeInBytes = sizeInBytes;
    }
}
