using System;

using Prowl.Graphite.Debugging;

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
    /// Stable identifier, unique and never reused.
    /// </summary>
    public ResourceId ResourceId { get; } = ResourceId.Next();

    /// <summary>
    /// Bumps on every content write, stamped at record time. Reads and binds do not bump it.
    /// Draws and dispatches bump the buffers bound for storage write, conservatively.
    /// </summary>
    public uint ContentVersion { get; private set; }

    internal BufferDescription DescriptionValue => _description;

    /// <summary>
    /// Identifier and content version together.
    /// </summary>
    public ResourceVersion CurrentVersion => new(ResourceId, ContentVersion);

    internal void MarkContentChanged()
    {
        unchecked { ContentVersion++; }
    }
}
