using System;

namespace Prowl.Graphite;

/// <summary>
/// GPU-side data buffer. Fixed size, no resizing.
/// </summary>
public abstract partial class DeviceBuffer : GraphicsResource
{
    private protected BufferDescription _description;

    private protected DeviceBuffer(in BufferDescription description)
    {
        _description = description;
    }

    /// <summary>
    /// Size in bytes, fixed at creation.
    /// </summary>
    public uint SizeInBytes => _description.SizeInBytes;

    /// <summary>
    /// Allowed uses.
    /// </summary>
    public BufferUsage Usage => _description.Usage;

    /// <summary>
    /// Bumps on every content change (CPU write or GPU copy in). Use to skip re-snapshotting unchanged data.
    /// Doesn't catch GPU compute writes to read-write bound buffers.
    /// </summary>
    public uint ContentVersion { get; private set; }

    internal void MarkContentChanged()
    {
        unchecked { ContentVersion++; }
    }
}
