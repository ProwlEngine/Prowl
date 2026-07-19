// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Graphite;

using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// The default per-object draw payload a culler emits and a pass consumes. Instance data is not
/// carried yet - the concrete instanced-batching path is deferred; single-instance draws bind their
/// per-object matrices through <see cref="Properties"/>.
/// </summary>
public struct DrawCommand
{
    public Mesh Mesh;
    public Material Material;
    public Float4x4 Model;
    public int Layer;

    /// <summary>Per-object shader properties (object matrices, SH, ...) from the renderable.</summary>
    public PropertySet Properties;

    /// <summary>Index of the material's shader pass this command draws (resolved from the query tag).</summary>
    public int PassIndex;

    /// <summary>Stable id of the renderable that produced this command, or -1 if unknown.</summary>
    public int SourceRenderableId;
}
