using Prowl.Vector;

namespace Prowl.Clay;

/// <summary>
/// A node in the imported scene hierarchy. A named transform plus optional mesh / skin
/// references, with no components or scripts attached.
/// </summary>
/// <remarks>
/// Local transform is stored in TRS form. The 4x4 matrices are computed once during import.
/// Sibling order is preserved from the source file.
/// </remarks>
public sealed class ModelNode
{
    /// <summary>Index of this node in <see cref="Model.Nodes"/>.</summary>
    public required int Index { get; init; }

    /// <summary>Node name. Empty when the source did not name the node.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Parent node, or <c>null</c> when this is the model root.</summary>
    public ModelNode? Parent { get; internal set; }

    /// <summary>Child nodes in source-file order.</summary>
    public IReadOnlyList<ModelNode> Children { get; internal set; } = Array.Empty<ModelNode>();

    /// <summary>Local position relative to <see cref="Parent"/>.</summary>
    public Float3 LocalPosition { get; init; }

    /// <summary>Local rotation relative to <see cref="Parent"/>.</summary>
    public Quaternion LocalRotation { get; init; } = Quaternion.Identity;

    /// <summary>Local scale relative to <see cref="Parent"/>.</summary>
    public Float3 LocalScale { get; init; } = Float3.One;

    /// <summary>4x4 TRS matrix representing this node's transform relative to its parent.</summary>
    public Float4x4 LocalMatrix { get; init; }

    /// <summary>4x4 matrix from this node's local space to the model's root space, baked at import time.</summary>
    public Float4x4 WorldMatrix { get; init; }

    /// <summary>Index into <see cref="Model.Meshes"/> for the mesh attached to this node, or -1 for none.</summary>
    public int MeshIndex { get; init; } = -1;

    /// <summary>Index into <see cref="Model.Skins"/> when this node is a <c>SkinnedMeshRenderer</c> host, otherwise -1.</summary>
    public int SkinIndex { get; init; } = -1;

    /// <summary>
    /// Format-specific scalar metadata (FBX user properties, glTF <c>extras</c> object, etc.).
    /// Empty when the source did not attach any.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Metadata { get; init; } =
        new Dictionary<string, object?>();
}
