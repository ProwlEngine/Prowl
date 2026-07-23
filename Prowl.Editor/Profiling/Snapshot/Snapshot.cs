using System.Collections.Generic;

namespace Prowl.Editor.Profiling;

/// <summary>
/// A cloned, fully independent runtime frame (HasCaptureDepth == true - the only place the full
/// PipelineSwitch/CallingObject/DrawCall depth exists) plus every SnapshotResource referenced by that
/// frame's ResourceRef/ReferenceBuffer entries. This array of resources is the only thing that tells a
/// Snapshot apart from a regular runtime frame.
/// </summary>
public sealed record Snapshot(
    string? Name, long FrameIndex,
    ProfiledFrame Frame,
    IReadOnlyList<SnapshotResource> Resources);

public enum SnapshotResourceKind { Texture, Buffer }

/// <summary>
/// A single buffer or texture (a RenderTexture's attachments count as one resource, same as one
/// RenderResourceID slot in the render graph), tracked across every version it was written to since
/// the start of the frame. A ResourceRef/ReferenceBuffer elsewhere in the Snapshot's Frame points at
/// one specific entry in Versions via its SnapshotResourceID.
/// </summary>
public sealed record SnapshotResource(
    uint ResourceId, string Name, SnapshotResourceKind Kind,
    IReadOnlyList<SnapshotResourceVersion> Versions);

/// <summary>One captured state of a SnapshotResource. Subtextures is populated for Kind == Texture (one
/// entry per attachment - color/depth); BufferData/BufferMeta are populated for Kind == Buffer.</summary>
public sealed record SnapshotResourceVersion(
    uint Version,
    IReadOnlyList<SnapshotSubTexture> Subtextures,
    byte[] BufferData,
    SnapshotBufferMeta? BufferMeta);

public readonly record struct SnapshotSubTexture(
    string Name, Graphite.PixelFormat Format, uint Width, uint Height, uint Depth, uint MipLevels, byte[] Pixels);

public readonly record struct SnapshotBufferMeta(
    Graphite.BufferUsage Kind, uint SizeBytes, uint Stride, IReadOnlyList<BufferField> Layout);

public readonly record struct BufferField(string Name, string Type, uint Offset, uint SizeBytes);
