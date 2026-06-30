namespace Prowl.Clay;

/// <summary>
/// Up to four bone influences on a single vertex (the standard hardware-skinning layout).
/// </summary>
/// <remarks>
/// Weights are expected to sum to 1.0 after the LimitBoneWeights post-process step runs.
/// Bone indices reference the parent <see cref="Skin.BoneNodeIndices"/> array, not <see cref="Model.Nodes"/>
/// directly.
/// </remarks>
public struct BoneWeight : IEquatable<BoneWeight>
{
    /// <summary>Bone index for the strongest influence.</summary>
    public int Index0;
    /// <summary>Bone index for the second-strongest influence.</summary>
    public int Index1;
    /// <summary>Bone index for the third-strongest influence.</summary>
    public int Index2;
    /// <summary>Bone index for the weakest tracked influence.</summary>
    public int Index3;

    /// <summary>Weight of <see cref="Index0"/>.</summary>
    public float Weight0;
    /// <summary>Weight of <see cref="Index1"/>.</summary>
    public float Weight1;
    /// <summary>Weight of <see cref="Index2"/>.</summary>
    public float Weight2;
    /// <summary>Weight of <see cref="Index3"/>.</summary>
    public float Weight3;

    /// <inheritdoc />
    public readonly bool Equals(BoneWeight other) =>
        Index0 == other.Index0 && Index1 == other.Index1 && Index2 == other.Index2 && Index3 == other.Index3 &&
        Weight0 == other.Weight0 && Weight1 == other.Weight1 && Weight2 == other.Weight2 && Weight3 == other.Weight3;

    /// <inheritdoc />
    public override readonly bool Equals(object? obj) => obj is BoneWeight w && Equals(w);

    /// <inheritdoc />
    public override readonly int GetHashCode() =>
        HashCode.Combine(Index0, Index1, Index2, Index3, Weight0, Weight1, Weight2, Weight3);
}
