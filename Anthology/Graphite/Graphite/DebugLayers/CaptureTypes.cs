using System;

using Prowl.Graphite.RenderGraph;

namespace Prowl.Graphite.Debugging;

/// <summary>One use of a resource by a pass: which version it had, which part, and how it was used.</summary>
public readonly record struct ResourceUse(
    RenderResourceID Resource,
    ResourceVersion Version,
    ResourceRange Range,
    ResourceUsage Usage);

/// <summary>Which attachment of a graph resource a backing image is.</summary>
public enum BackingRole : byte
{
    Color,
    Depth,
    Buffer,
}

/// <summary>One actual image or buffer behind a graph resource for one execution, with its version when the view began.</summary>
public readonly record struct GraphBacking(ResourceId Id, ResourceVersion EntryVersion, BackingRole Role, uint Index);

/// <summary>
/// A graph resource as one view execution saw it. A buffer has one backing, a texture one per color attachment plus depth.
/// </summary>
public readonly record struct GraphResourceInfo(
    RenderResourceID Id,
    string Name,
    GraphResourceKind Kind,
    ReadOnlyMemory<GraphBacking> Backings,
    bool Imported,
    GraphTextureDesc? Texture,
    GraphBufferDesc? Buffer);

/// <summary>Public copy of a pass's declared access to a graph resource.</summary>
public readonly record struct PassResourceAccess(
    RenderResourceID Id,
    GraphResourceKind Kind,
    bool IsOutput,
    TextureState TextureUsage,
    TextureState? DepthUsage,
    BufferAccess BufferUsage);

/// <summary>A pass and the accesses it declared.</summary>
public readonly record struct PassCaptureInfo(
    PassInfo Pass,
    ReadOnlyMemory<PassResourceAccess> Accesses);

/// <summary>Everything a capture hook learns about one view of one graph execution.</summary>
public readonly record struct ViewCaptureInfo(
    ulong ExecutionId,
    string ViewName,
    ReadOnlyMemory<GraphResourceInfo> Resources,
    ReadOnlyMemory<PassCaptureInfo> Passes);

/// <summary>A buffer or texture a pass references that is not a graph resource.</summary>
public readonly record struct ExternalResourceInfo(
    ResourceId Id,
    string Name,
    ResourceVersion EntryVersion,
    TextureDescription? Texture,
    BufferDescription? Buffer);
