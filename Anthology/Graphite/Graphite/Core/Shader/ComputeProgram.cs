
namespace Prowl.Graphite;

/// <summary>Device resource for a single compute shader program.</summary>
public abstract class ComputeProgram : ShaderProgram
{
    private readonly uint _threadGroupSizeX;
    private readonly uint _threadGroupSizeY;
    private readonly uint _threadGroupSizeZ;

    internal ComputeProgram(ref ComputeDescription description)
        : base(description.ResourceLayouts)
    {
        _threadGroupSizeX = description.ThreadGroupSizeX;
        _threadGroupSizeY = description.ThreadGroupSizeY;
        _threadGroupSizeZ = description.ThreadGroupSizeZ;
    }

    /// <summary>Thread group size X.</summary>
    public uint ThreadGroupSizeX => _threadGroupSizeX;

    /// <summary>Thread group size Y.</summary>
    public uint ThreadGroupSizeY => _threadGroupSizeY;

    /// <summary>Thread group size Z.</summary>
    public uint ThreadGroupSizeZ => _threadGroupSizeZ;
}
