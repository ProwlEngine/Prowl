using Prowl.Vector;

namespace Prowl.Clay;

/// <summary>
/// Binds a skinned <see cref="Mesh"/> to a set of bones (which are <see cref="ModelNode"/>s).
/// </summary>
/// <remarks>
/// A <see cref="ModelNode"/> with <c>SkinIndex &gt;= 0</c> is the host of a <c>SkinnedMeshRenderer</c>;
/// the mesh's <see cref="Mesh.BoneWeights"/> reference bone indices within this <see cref="BoneNodeIndices"/>
/// array, not absolute node indices.
/// </remarks>
public sealed class Skin
{
    /// <summary>Optional name of the skin.</summary>
    public string? Name { get; init; }

    /// <summary>
    /// Node index of the skeleton root, the closest common ancestor of all joint nodes.
    /// When the source declared an explicit skeleton root (glTF <c>skin.skeleton</c>), that index is used directly.
    /// </summary>
    public int RootNodeIndex { get; init; } = -1;

    /// <summary>
    /// Indices into <see cref="Model.Nodes"/> of the joint nodes, in the order referenced by
    /// vertex <see cref="BoneWeight"/>s and <see cref="Mesh.BindPoses"/>.
    /// </summary>
    public required int[] BoneNodeIndices { get; init; }

    /// <summary>
    /// Inverse bind-pose matrices, parallel to <see cref="BoneNodeIndices"/>. Transforms a vertex
    /// from mesh-local space into the joint's local space at bind time.
    /// </summary>
    public required Float4x4[] InverseBindPoses { get; init; }
}
