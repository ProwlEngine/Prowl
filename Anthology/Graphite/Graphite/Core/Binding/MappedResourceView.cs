using System;
using System.Runtime.CompilerServices;

namespace Prowl.Graphite;

/// <summary>
/// By-ref view over a mapped resource.
/// </summary>
/// <typeparam name="T">Type of mapped data.</typeparam>
public readonly unsafe struct MappedResourceView<T> where T : struct
{
    private static readonly int s_sizeofT = Unsafe.SizeOf<T>();

    /// <summary>
    /// Wrapped resource.
    /// </summary>
    public readonly MappedResource MappedResource;
    /// <summary>
    /// Size in bytes.
    /// </summary>
    public readonly uint SizeInBytes;
    /// <summary>
    /// Struct count.
    /// </summary>
    public readonly int Count;

    /// <summary>
    /// Wraps a mapped resource.
    /// </summary>
    /// <param name="rawResource">Resource to wrap.</param>
    public MappedResourceView(MappedResource rawResource)
    {
        MappedResource = rawResource;
        SizeInBytes = rawResource.SizeInBytes;
        Count = (int)(SizeInBytes / s_sizeofT);
    }

    /// <summary>
    /// Value at index.
    /// </summary>
    /// <param name="index">Index.</param>
    /// <returns>Ref.</returns>
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
    /// Value at index.
    /// </summary>
    /// <param name="index">Index.</param>
    /// <returns>Ref.</returns>
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
    /// Value at 2D coords.
    /// </summary>
    /// <param name="x">X coord.</param>
    /// <param name="y">Y coord.</param>
    /// <returns>Ref.</returns>
    public readonly ref T this[int x, int y]
    {
        get
        {
            byte* ptr = (byte*)MappedResource.Data + (y * MappedResource.RowPitch) + (x * s_sizeofT);
            return ref Unsafe.AsRef<T>(ptr);
        }
    }

    /// <summary>
    /// Value at 2D coords.
    /// </summary>
    /// <param name="x">X coord.</param>
    /// <param name="y">Y coord.</param>
    /// <returns>Ref.</returns>
    public readonly ref T this[uint x, uint y]
    {
        get
        {
            byte* ptr = (byte*)MappedResource.Data + (y * MappedResource.RowPitch) + (x * s_sizeofT);
            return ref Unsafe.AsRef<T>(ptr);
        }
    }

    /// <summary>
    /// Value at 3D coords.
    /// </summary>
    /// <param name="x">X coord.</param>
    /// <param name="y">Y coord.</param>
    /// <param name="z">Z coord.</param>
    /// <returns>Ref.</returns>
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
    /// Value at 3D coords.
    /// </summary>
    /// <param name="x">X coord.</param>
    /// <param name="y">Y coord.</param>
    /// <param name="z">Z coord.</param>
    /// <returns>Ref.</returns>
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
