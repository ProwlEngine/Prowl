using System;

namespace Prowl.Graphite;

/// <summary>
/// Describes a <see cref="ComputeProgram"/>, for creation using a <see cref="ResourceFactory"/>.
/// </summary>
public struct ComputeDescription : IEquatable<ComputeDescription>
{
    /// <summary>
    /// The compute stage description. <see cref="ShaderStageDescription.Stage"/> must be
    /// <see cref="ShaderStages.Compute"/>.
    /// </summary>
    public ShaderStageDescription Stage;

    /// <summary>
    /// The resource layouts declared by this compute program.
    /// </summary>
    public ResourceLayoutDescription[] ResourceLayouts;

    /// <summary>
    /// The X dimension of the thread group size.
    /// </summary>
    public uint ThreadGroupSizeX;

    /// <summary>
    /// The Y dimension of the thread group size.
    /// </summary>
    public uint ThreadGroupSizeY;

    /// <summary>
    /// The Z dimension of the thread group size.
    /// </summary>
    public uint ThreadGroupSizeZ;

    /// <summary>
    /// Constructs a new <see cref="ComputeDescription"/>.
    /// </summary>
    public ComputeDescription(
        ShaderStageDescription stage,
        ResourceLayoutDescription[] resourceLayouts,
        uint threadGroupSizeX,
        uint threadGroupSizeY,
        uint threadGroupSizeZ)
    {
        Stage = stage;
        ResourceLayouts = resourceLayouts;
        ThreadGroupSizeX = threadGroupSizeX;
        ThreadGroupSizeY = threadGroupSizeY;
        ThreadGroupSizeZ = threadGroupSizeZ;
    }

    /// <summary>
    /// Element-wise equality.
    /// </summary>
    public bool Equals(ComputeDescription other)
    {
        return Stage.Equals(other.Stage)
            && Util.ArrayEqualsEquatable(ResourceLayouts, other.ResourceLayouts)
            && ThreadGroupSizeX == other.ThreadGroupSizeX
            && ThreadGroupSizeY == other.ThreadGroupSizeY
            && ThreadGroupSizeZ == other.ThreadGroupSizeZ;
    }

    /// <summary>
    /// Returns the hash code for this instance.
    /// </summary>
    public override readonly int GetHashCode()
    {
        return HashCode.Combine(
            Stage,
            ResourceLayouts.ArrayHash(),
            ThreadGroupSizeX,
            ThreadGroupSizeY,
            ThreadGroupSizeZ);
    }
}
