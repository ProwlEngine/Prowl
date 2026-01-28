// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.Graphite;

/// <summary>
/// A GPU buffer for storing vertex, index, uniform, or storage data.
/// Buffers are immutable after creation - use GraphiteDevice.UpdateBuffer for updates.
/// </summary>
public abstract class Buffer : GraphiteResource
{
    /// <summary>Size of the buffer in bytes.</summary>
    public uint SizeInBytes { get; protected set; }

    /// <summary>How the buffer is used.</summary>
    public BufferUsage Usage { get; protected set; }

    /// <summary>Memory access pattern.</summary>
    public MemoryAccess MemoryAccess { get; protected set; }
}
