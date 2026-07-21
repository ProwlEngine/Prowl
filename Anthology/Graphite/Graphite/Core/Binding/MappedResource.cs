using System;
using System.Runtime.CompilerServices;

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

/// <summary>
/// Typed by-ref view over a mapped resource.
/// </summary>
/// <typeparam name="T">Blittable type mapped data is viewed as.</typeparam>
public readonly unsafe struct MappedResourceView<T> where T : struct
{
    private static readonly int s_sizeofT = Unsafe.SizeOf<T>();

    /// <summary>
    /// The wrapped mapped resource.
    /// </summary>
    public readonly MappedResource MappedResource;
    /// <summary>
    /// Total size in bytes.
    /// </summary>
    public readonly uint SizeInBytes;
    /// <summary>
    /// Number of structs in the resource, i.e. bytes / struct size.
    /// </summary>
    public readonly int Count;

    /// <summary>
    /// Wraps a mapped resource.
    /// </summary>
    /// <param name="rawResource">Raw mapped resource.</param>
    public MappedResourceView(MappedResource rawResource)
    {
        MappedResource = rawResource;
        SizeInBytes = rawResource.SizeInBytes;
        Count = (int)(SizeInBytes / s_sizeofT);
    }

    /// <summary>
    /// Ref to value at index.
    /// </summary>
    /// <param name="index">Index.</param>
    /// <returns>Ref to value.</returns>
    public readonly ref T this[int index]
    {
        get
        {
            if (index >= Count || index < 0)
            {
                throw new IndexOutOfRangeException(
                    $"Given index ({index}) must be non-negative and less than Count ({Count}).");
            }

            byte* ptr = (byte*)MappedResource.Data + (index * s_sizeofT);
            return ref Unsafe.AsRef<T>(ptr);
        }
    }

    /// <summary>
    /// Ref to value at index.
    /// </summary>
    /// <param name="index">Index.</param>
    /// <returns>Ref to value.</returns>
    public readonly ref T this[uint index]
    {
        get
        {
            if (index >= Count)
            {
                throw new IndexOutOfRangeException(
                    $"Given index ({index}) must be less than Count ({Count}).");
            }

            byte* ptr = (byte*)MappedResource.Data + (index * s_sizeofT);
            return ref Unsafe.AsRef<T>(ptr);
        }
    }

    /// <summary>
    /// Ref to value at 2D texture coords.
    /// </summary>
    /// <param name="x">X coord.</param>
    /// <param name="y">Y coord.</param>
    /// <returns>Ref to value.</returns>
    public readonly ref T this[int x, int y]
    {
        get
        {
            byte* ptr = (byte*)MappedResource.Data + (y * MappedResource.RowPitch) + (x * s_sizeofT);
            return ref Unsafe.AsRef<T>(ptr);
        }
    }

    /// <summary>
    /// Ref to value at 2D texture coords.
    /// </summary>
    /// <param name="x">X coord.</param>
    /// <param name="y">Y coord.</param>
    /// <returns>Ref to value.</returns>
    public readonly ref T this[uint x, uint y]
    {
        get
        {
            byte* ptr = (byte*)MappedResource.Data + (y * MappedResource.RowPitch) + (x * s_sizeofT);
            return ref Unsafe.AsRef<T>(ptr);
        }
    }

    /// <summary>
    /// Ref to value at 3D texture coords.
    /// </summary>
    /// <param name="x">X coord.</param>
    /// <param name="y">Y coord.</param>
    /// <param name="z">Z coord.</param>
    /// <returns>Ref to value.</returns>
    public readonly ref T this[int x, int y, int z]
    {
        get
        {
            byte* ptr = (byte*)MappedResource.Data
                + (z * MappedResource.DepthPitch)
                + (y * MappedResource.RowPitch)
                + (x * s_sizeofT);
            return ref Unsafe.AsRef<T>(ptr);
        }
    }

    /// <summary>
    /// Ref to value at 3D texture coords.
    /// </summary>
    /// <param name="x">X coord.</param>
    /// <param name="y">Y coord.</param>
    /// <param name="z">Z coord.</param>
    /// <returns>Ref to value.</returns>
    public readonly ref T this[uint x, uint y, uint z]
    {
        get
        {
            byte* ptr = (byte*)MappedResource.Data
                + (z * MappedResource.DepthPitch)
                + (y * MappedResource.RowPitch)
                + (x * s_sizeofT);
            return ref Unsafe.AsRef<T>(ptr);
        }
    }
}
