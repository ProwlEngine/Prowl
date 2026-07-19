// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Graphite;
using Prowl.Vector;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// A frozen capture of one camera's frame: the full <see cref="RenderFrameReport"/> metadata plus the
/// heavy read-back data (resource pixels and drawn wireframe geometry). Serialized to a single Echo
/// blob (.rendersnapshot) by the capture path and reloaded by the snapshot viewer.
/// </summary>
public sealed class RenderSnapshot
{
    /// <summary>Blob format version, bumped when the layout changes.</summary>
    public int Version = 1;

    /// <summary>The frame's full profiling report.</summary>
    public RenderFrameReport Report = new();

    /// <summary>Read-back pixels for each graph resource, keyed by interned resource name.</summary>
    public List<SnapshotTexture> Textures = new();

    /// <summary>Wireframe geometry for each drawn mesh/submesh, deduped by guid + submesh.</summary>
    public List<SnapshotGeometry> Geometry = new();

    /// <summary>Camera context for the wireframe viewer.</summary>
    public CapturedCamera Camera = new();
}

/// <summary>Read-back pixel data for a single graph resource (mip 0 only).</summary>
public sealed class SnapshotTexture
{
    public string ResourceId = "";
    public int Width;
    public int Height;
    public int Depth = 1;
    public PixelFormat Format;
    public bool IsDepth;
    public byte[] Pixels = Array.Empty<byte>();
}

/// <summary>Wireframe geometry for a drawn mesh/submesh: interleaved xyz positions and triangle indices.</summary>
public sealed class SnapshotGeometry
{
    public Guid MeshGuid;
    public int SubMeshIndex = -1;
    public string Name = "";

    /// <summary>Vertex positions as flat xyz triples.</summary>
    public float[] Positions = Array.Empty<float>();

    public int[] Indices = Array.Empty<int>();
}

/// <summary>Minimal camera state kept with a snapshot so the wireframe viewer can reproduce the view.</summary>
public sealed class CapturedCamera
{
    public Float4x4 View;
    public Float4x4 Projection;
    public Float3 Position;
    public int PixelWidth;
    public int PixelHeight;
}
