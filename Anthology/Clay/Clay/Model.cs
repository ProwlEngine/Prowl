namespace Prowl.Clay;

/// <summary>
/// The fully imported model: a hierarchy of <see cref="ModelNode"/>s plus flat asset tables.
/// </summary>
/// <remarks>
/// Returned by <c>ModelImporter.Load(...)</c>.
/// All public members are immutable after import; the engine consumes this snapshot directly or
/// converts it into runtime GameObjects/Meshes/Materials.
/// </remarks>
public sealed class Model
{
    /// <summary>Source file path, when loaded from disk. <c>null</c> for stream-based imports.</summary>
    public string? SourcePath { get; init; }

    /// <summary>File-level metadata.</summary>
    public required ModelMetadata Metadata { get; init; }

    /// <summary>Root node of the hierarchy.</summary>
    public required ModelNode Root { get; init; }

    /// <summary>Flat list of all nodes in depth-first order. Indices match <see cref="ModelNode.Index"/>.</summary>
    public required IReadOnlyList<ModelNode> Nodes { get; init; }

    /// <summary>All meshes in the scene.</summary>
    public required IReadOnlyList<Mesh> Meshes { get; init; }

    /// <summary>All materials in the scene.</summary>
    public required IReadOnlyList<Material> Materials { get; init; }

    /// <summary>All textures referenced by materials.</summary>
    public required IReadOnlyList<Texture> Textures { get; init; }

    /// <summary>All skins in the scene.</summary>
    public required IReadOnlyList<Skin> Skins { get; init; }

    /// <summary>All animation clips in the scene.</summary>
    public required IReadOnlyList<AnimationClip> AnimationClips { get; init; }

    /// <summary>Non-fatal warnings and informational entries collected during import.</summary>
    public required ImportLog Log { get; init; }

    /// <summary>Returns the first node whose <see cref="ModelNode.Name"/> matches, or <c>null</c>.</summary>
    public ModelNode? FindNode(string name)
    {
        for (int i = 0; i < Nodes.Count; i++)
        {
            if (Nodes[i].Name == name)
                return Nodes[i];
        }
        return null;
    }
}
