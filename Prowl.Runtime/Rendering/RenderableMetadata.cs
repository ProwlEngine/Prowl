using Prowl.Vector;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Pushed via <see cref="Prowl.Graphite.CommandBuffer.RecordMetadata"/> once per <see cref="IRenderable"/>
/// considered in a pass draw loop - object-level detail (which mesh/material, culled or not, how many
/// draws it produced) that Graphite's own draw/dispatch events don't carry, since Graphite has no
/// concept of a renderable, only draws.
/// </summary>
public readonly struct RenderableMetadata
{
    public string MaterialName { get; init; }
    public string MeshName { get; init; }
    public int Layer { get; init; }
    public Float3 Position { get; init; }
    public bool Culled { get; init; }

    /// <summary>Draws this object emitted this pass (0 if culled/skipped).</summary>
    public int DrawCallCount { get; init; }
}
