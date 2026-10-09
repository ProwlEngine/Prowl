using System;

using Prowl.Graphite.RenderGraph;

namespace Prowl.Graphite.Debugging;

/// <summary>Which attachment of a graph resource a backing image is.</summary>
public enum BackingRole : byte
{
    Color,
    Depth,
    Buffer,
}

/// <summary>One actual image or buffer behind a graph resource for one execution, with its version when the view began.</summary>
public readonly record struct GraphBacking(ResourceId Id, ResourceVersion EntryVersion, BackingRole Role, uint Index);

/// <summary>Where a graph resource comes from.</summary>
public enum GraphResourceOrigin : byte
{
    Transient,
    Imported,
    ViewTarget,
}

/// <summary>
/// A graph resource as one view execution saw it. A buffer has one backing, a texture one per color attachment plus depth.
/// </summary>
public readonly record struct GraphResourceInfo(
    RenderResourceID Id,
    string Name,
    GraphResourceKind Kind,
    ReadOnlyMemory<GraphBacking> Backings,
    GraphResourceOrigin Origin,
    GraphTextureDesc? Texture,
    GraphBufferDesc? Buffer);

/// <summary>The resources and passes of one view, matched to its <see cref="ViewInfo"/> by index.</summary>
public readonly record struct ViewCaptureInfo(
    int ViewIndex,
    ReadOnlyMemory<GraphResourceInfo> Resources,
    ReadOnlyMemory<PassInfo> Passes);

/// <summary>One resource a pass touched: version at first reference, version after the pass, and its descriptor.</summary>
public readonly record struct PassReference(
    ResourceVersion First,
    uint LastVersion,
    string Name,
    TextureDescription? Texture,
    BufferDescription? Buffer);
