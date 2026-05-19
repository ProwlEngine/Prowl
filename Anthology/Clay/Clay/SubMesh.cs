namespace Prowl.Clay;

/// <summary>
/// A contiguous range of indices in the parent <see cref="Mesh"/>'s shared index buffer that all
/// use the same material. One mesh contains one or more sub-meshes, one draw call each.
/// </summary>
public sealed class SubMesh
{
    /// <summary>Index topology (triangles, lines, or points).</summary>
    public PrimitiveTopology Topology { get; init; } = PrimitiveTopology.Triangles;

    /// <summary>Offset into <see cref="Mesh.Indices"/> where this sub-mesh's indices begin.</summary>
    public int IndexStart { get; init; }

    /// <summary>Number of entries in <see cref="Mesh.Indices"/> belonging to this sub-mesh.</summary>
    public int IndexCount { get; init; }

    /// <summary>
    /// Constant added to every index before vertex-buffer lookup. Useful when packing multiple
    /// sub-meshes into a single vertex buffer without renumbering indices.
    /// </summary>
    public int BaseVertex { get; init; }

    /// <summary>Index into <see cref="Model.Materials"/>, or -1 when no material is assigned.</summary>
    public int MaterialIndex { get; init; } = -1;

    /// <summary>Local-space bounds covering only the vertices referenced by this sub-mesh.</summary>
    public Bounds Bounds { get; init; }
}
