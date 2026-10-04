using System;

namespace Prowl.Graphite;

/// <summary>
/// Compute program descriptor for ResourceFactory.
/// </summary>
public struct ComputeDescription
{
    /// <summary>
    /// Compute stage; must be Compute type.
    /// </summary>
    public ShaderStageDescription Stage;

    /// <summary>
    /// Resource layouts declared.
    /// </summary>
    public ResourceLayoutDescription[] ResourceLayouts;

    /// <summary>
    /// Thread group size X.
    /// </summary>
    public uint ThreadGroupSizeX;

    /// <summary>
    /// Thread group size Y.
    /// </summary>
    public uint ThreadGroupSizeY;

    /// <summary>
    /// Thread group size Z.
    /// </summary>
    public uint ThreadGroupSizeZ;

    /// <summary>
    /// Creates new instance.
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
}
