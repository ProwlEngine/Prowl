using Prowl.Vector;

namespace Prowl.Clay.Internal.Intermediate;

/// <summary>
/// Mutable counterpart of <see cref="Skin"/>. Bone references live as node pointers until the
/// scene is baked, at which point they become node indices.
/// </summary>
internal sealed class IntermediateSkin
{
    public string? Name { get; set; }
    public IntermediateNode? RootNode { get; set; }
    public List<IntermediateNode> BoneNodes { get; } = new();
    public List<Float4x4> InverseBindPoses { get; } = new();
}
