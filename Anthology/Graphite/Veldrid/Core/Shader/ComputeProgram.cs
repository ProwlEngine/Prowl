using System;
using System.Collections.Generic;

namespace Prowl.Veldrid;

/// <summary>
/// A device resource encapsulating a single compute shader program.
/// See <see cref="ComputeDescription"/>.
/// </summary>
public abstract class ComputeProgram : DeviceResource, IDisposable
{
    private readonly ResourceLayoutDescription[] _resourceLayouts;
    private readonly uint _threadGroupSizeX;
    private readonly uint _threadGroupSizeY;
    private readonly uint _threadGroupSizeZ;

    internal ComputeProgram(ref ComputeDescription description)
    {
        _resourceLayouts = Util.ShallowClone(description.ResourceLayouts) ?? Array.Empty<ResourceLayoutDescription>();
        _threadGroupSizeX = description.ThreadGroupSizeX;
        _threadGroupSizeY = description.ThreadGroupSizeY;
        _threadGroupSizeZ = description.ThreadGroupSizeZ;
    }

    /// <summary>
    /// The resource layouts declared by this compute program.
    /// </summary>
    public IReadOnlyList<ResourceLayoutDescription> ResourceLayouts => _resourceLayouts;

    internal ResourceLayoutDescription[] ResourceLayoutsArray => _resourceLayouts;

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

    /// <summary>
    /// A string identifying this instance.
    /// </summary>
    public abstract string Name { get; set; }

    /// <summary>
    /// A bool indicating whether this instance has been disposed.
    /// </summary>
    public abstract bool IsDisposed { get; }

    /// <summary>
    /// Frees unmanaged device resources controlled by this instance.
    /// </summary>
    public abstract void Dispose();
}
