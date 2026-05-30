
namespace Prowl.Veldrid;

/// <summary>
/// A device resource encapsulating a single compute shader program.
/// See <see cref="ComputeDescription"/>.
/// </summary>
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

    /// <summary>
    /// The X dimension of the thread group size.
    /// </summary>
    public uint ThreadGroupSizeX => _threadGroupSizeX;

    /// <summary>
    /// The Y dimension of the thread group size.
    /// </summary>
    public uint ThreadGroupSizeY => _threadGroupSizeY;

    /// <summary>
    /// The Z dimension of the thread group size.
    /// </summary>
    public uint ThreadGroupSizeZ => _threadGroupSizeZ;
}
