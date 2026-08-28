using Prowl.Vector;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Pushed via <see cref="Prowl.Graphite.CommandBuffer.RecordMetadata"/> once per <see cref="IRenderable"/>
/// considered in a pass draw loop, before any draw it covers - object-level identity (which mesh/material,
/// culled or not) that Graphite's own draw events don't carry, since Graphite has no concept of a
/// renderable, only draws. Per <see cref="Prowl.Graphite.CommandBuffer.RecordMetadata"/>, it attaches to
/// every draw issued from this call until the next, so the actual draw count for this renderable is
/// however many real draws land in that window - not something this struct needs to state itself.
/// </summary>
public readonly struct RenderableMetadata
{
    public string MaterialName { get; init; }
    public string MeshName { get; init; }
    public int Layer { get; init; }
    public Float3 Position { get; init; }
    public bool Culled { get; init; }
}
