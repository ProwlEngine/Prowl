
namespace Prowl.Graphite;

/// <summary>Device resource for a single compute shader program.</summary>
public abstract class ComputeProgram : ShaderProgram
{
    private readonly ShaderStageDescription _stageDescription;
    private readonly uint _threadGroupSizeX;
    private readonly uint _threadGroupSizeY;
    private readonly uint _threadGroupSizeZ;

    internal ComputeProgram(in ComputeDescription description)
        : base(description.ResourceLayouts)
    {
        _stageDescription = description.Stage;
        Key = ProgramKey.Compute([description.Stage]);
        _threadGroupSizeX = description.ThreadGroupSizeX;
        _threadGroupSizeY = description.ThreadGroupSizeY;
        _threadGroupSizeZ = description.ThreadGroupSizeZ;
    }

    /// <summary>Compute stage with SPIR-V and entry point.</summary>
    public ShaderStageDescription StageDescription => _stageDescription;

    /// <summary>Thread group size X.</summary>
    public uint ThreadGroupSizeX => _threadGroupSizeX;

    /// <summary>Thread group size Y.</summary>
    public uint ThreadGroupSizeY => _threadGroupSizeY;

    /// <summary>Thread group size Z.</summary>
    public uint ThreadGroupSizeZ => _threadGroupSizeZ;
}
