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
    /// Pointer to start of mapped data.
    /// </summary>
    public readonly IntPtr Data;

    /// <summary>
    /// Mapped data size in bytes.
    /// </summary>
    public readonly uint SizeInBytes;

    /// <summary>
    /// Mapped subresource for textures. Meaningless for buffers.
    /// </summary>
    public readonly uint Subresource;

    /// <summary>
    /// Bytes between texel rows for textures. Meaningless for buffers.
    /// </summary>
    public readonly uint RowPitch;

    /// <summary>
    /// Bytes between depth slices for 3D textures. Meaningless for buffers or 2D textures.
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
