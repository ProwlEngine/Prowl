// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Runtime.Graphite;

/// <summary>
/// Describes how to create a GPU buffer.
/// </summary>
public struct BufferDescriptor
{
    /// <summary>Size of the buffer in bytes.</summary>
    public uint SizeInBytes;

    /// <summary>How the buffer will be used.</summary>
    public BufferUsage Usage;

    /// <summary>Memory access pattern.</summary>
    public MemoryAccess MemoryAccess;

    /// <summary>Optional debug name for graphics debuggers.</summary>
    public string? DebugName;

    /// <summary>Optional initial data to upload.</summary>
    public ReadOnlyMemory<byte>? InitialData;

    public BufferDescriptor(uint sizeInBytes, BufferUsage usage, MemoryAccess memoryAccess = MemoryAccess.GpuOnly)
    {
        SizeInBytes = sizeInBytes;
        Usage = usage;
        MemoryAccess = memoryAccess;
        DebugName = null;
        InitialData = null;
    }

    /// <summary>
    /// Creates a vertex buffer descriptor.
    /// </summary>
    public static BufferDescriptor Vertex(uint sizeInBytes, bool dynamic = false) => new()
    {
        SizeInBytes = sizeInBytes,
        Usage = BufferUsage.Vertex | BufferUsage.CopyDestination,
        MemoryAccess = dynamic ? MemoryAccess.CpuToGpu : MemoryAccess.GpuOnly,
    };

    /// <summary>
    /// Creates an index buffer descriptor.
    /// </summary>
    public static BufferDescriptor Index(uint sizeInBytes, bool dynamic = false) => new()
    {
        SizeInBytes = sizeInBytes,
        Usage = BufferUsage.Index | BufferUsage.CopyDestination,
        MemoryAccess = dynamic ? MemoryAccess.CpuToGpu : MemoryAccess.GpuOnly,
    };

    /// <summary>
    /// Creates a uniform buffer descriptor.
    /// </summary>
    public static BufferDescriptor Uniform(uint sizeInBytes) => new()
    {
        SizeInBytes = sizeInBytes,
        Usage = BufferUsage.Uniform | BufferUsage.CopyDestination,
        MemoryAccess = MemoryAccess.CpuToGpu,
    };

    /// <summary>
    /// Creates a storage buffer descriptor.
    /// </summary>
    public static BufferDescriptor Storage(uint sizeInBytes, bool cpuReadback = false) => new()
    {
        SizeInBytes = sizeInBytes,
        Usage = BufferUsage.Storage | BufferUsage.CopyDestination | (cpuReadback ? BufferUsage.CopySource : 0),
        MemoryAccess = cpuReadback ? MemoryAccess.GpuToCpu : MemoryAccess.GpuOnly,
    };
}
