using System;

namespace Prowl.Graphite;

/// <summary>
/// Layout of a mapped resource.
/// </summary>
public readonly struct MappedResource
{
    /// <summary>
    /// The mapped resource.
    /// </summary>
    public readonly MappableResource Resource;

    /// <summary>
    /// Map mode used.
    /// </summary>
    public readonly MapMode Mode;

    /// <summary>
    /// Pointer to mapped data start.
    /// </summary>
    public readonly IntPtr Data;

    /// <summary>
    /// Mapped data size in bytes.
    /// </summary>
    public readonly uint SizeInBytes;

    /// <summary>
    /// Subresource for textures (buffers: N/A).
    /// </summary>
    public readonly uint Subresource;

    /// <summary>
    /// Pitch between texel rows (buffers: N/A).
    /// </summary>
    public readonly uint RowPitch;

    /// <summary>
    /// Pitch between depth slices (3D only).
    /// </summary>
    public readonly uint DepthPitch;

    internal MappedResource(
        MappableResource resource,
        MapMode mode,
        IntPtr data,
        uint sizeInBytes,
        uint subresource,
        uint rowPitch,
        uint depthPitch)
    {
        Resource = resource;
        Mode = mode;
        Data = data;
        SizeInBytes = sizeInBytes;
        Subresource = subresource;
        RowPitch = rowPitch;
        DepthPitch = depthPitch;
    }

    internal MappedResource(MappableResource resource, MapMode mode, IntPtr data, uint sizeInBytes)
    {
        Resource = resource;
        Mode = mode;
        Data = data;
        SizeInBytes = sizeInBytes;

        Subresource = 0;
        RowPitch = 0;
        DepthPitch = 0;
    }
}
