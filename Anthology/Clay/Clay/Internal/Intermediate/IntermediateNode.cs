using Prowl.Vector;

namespace Prowl.Clay.Internal.Intermediate;

/// <summary>
/// Mutable counterpart of <see cref="ModelNode"/>. Owns its own children list; parent references
/// are upward-only pointers maintained by the format readers and post-process steps.
/// </summary>
internal sealed class IntermediateNode
{
    public string Name { get; set; } = string.Empty;
    public IntermediateNode? Parent { get; set; }
    public List<IntermediateNode> Children { get; } = new();

    public Float3 LocalPosition { get; set; }
    public Quaternion LocalRotation { get; set; } = Quaternion.Identity;
    public Float3 LocalScale { get; set; } = Float3.One;

    public int MeshIndex { get; set; } = -1;
    public int SkinIndex { get; set; } = -1;

    public Dictionary<string, object?> Metadata { get; } = new();

    /// <summary>Cached index assigned during bake-out; -1 before then.</summary>
    public int BakeIndex { get; set; } = -1;
}
