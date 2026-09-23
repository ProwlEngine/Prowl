using Prowl.Vector;

namespace Prowl.Motion;

/// <summary>
/// A per-bone weight array in [0,1] used to restrict blends to part of the skeleton.
/// Combine is element-wise multiply; blend is a per-weight lerp.
/// </summary>
public sealed class BoneMask
{
    private readonly Skeleton _skeleton;
    private readonly float[] _weights;

    /// <summary>Creates a mask for the skeleton with every weight set to <paramref name="fixedWeight"/>.</summary>
    public BoneMask(Skeleton skeleton, float fixedWeight = 0f)
    {
        ArgumentNullException.ThrowIfNull(skeleton);
        _skeleton = skeleton;
        _weights = new float[skeleton.BoneCount];
        if (fixedWeight != 0f)
            Array.Fill(_weights, Maths.Clamp(fixedWeight, 0f, 1f));
    }

    public Skeleton Skeleton => _skeleton;

    /// <summary>Number of weights (one per bone).</summary>
    public int Length => _weights.Length;

    public float GetWeight(int boneIndex) => _weights[boneIndex];

    public void SetWeight(int boneIndex, float weight) => _weights[boneIndex] = Maths.Clamp(weight, 0f, 1f);

    /// <summary>Sets every weight to the given value.</summary>
    public void ResetWeights(float weight) => Array.Fill(_weights, Maths.Clamp(weight, 0f, 1f));

    /// <summary>Multiplies this mask by another element-wise.</summary>
    public void CombineWith(BoneMask other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (other._weights.Length != _weights.Length)
            throw new ArgumentException("Bone mask lengths must match.", nameof(other));

        for (int i = 0; i < _weights.Length; i++)
            _weights[i] *= other._weights[i];
    }

    /// <summary>Lerps every weight toward the target mask by t in [0,1].</summary>
    public void BlendTo(BoneMask target, float t)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target._weights.Length != _weights.Length)
            throw new ArgumentException("Bone mask lengths must match.", nameof(target));

        float clamped = Maths.Clamp(t, 0f, 1f);
        for (int i = 0; i < _weights.Length; i++)
            _weights[i] = Maths.Lerp(_weights[i], target._weights[i], clamped);
    }

    public void CopyFrom(BoneMask other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (other._weights.Length != _weights.Length)
            throw new ArgumentException("Bone mask lengths must match.", nameof(other));

        Array.Copy(other._weights, _weights, _weights.Length);
    }

    /// <summary>
    /// Builds a mask by feathering authored seed weights down the hierarchy: each bone inherits the
    /// weight of its nearest seeded ancestor (or itself), and bones with no seeded ancestor take the
    /// rest weight.
    /// This turns sparse region authoring (e.g. "spine = 1") into a full per-bone mask.
    /// </summary>
    public static BoneMask CreateHierarchical(Skeleton skeleton, IReadOnlyList<(int Bone, float Weight)> seeds, float restWeight = 0f)
    {
        ArgumentNullException.ThrowIfNull(skeleton);
        ArgumentNullException.ThrowIfNull(seeds);

        var seeded = new Dictionary<int, float>(seeds.Count);
        foreach ((int bone, float weight) in seeds)
            seeded[bone] = Maths.Clamp(weight, 0f, 1f);

        var mask = new BoneMask(skeleton, Maths.Clamp(restWeight, 0f, 1f));
        for (int i = 0; i < skeleton.BoneCount; i++)
        {
            int current = i;
            while (current != Skeleton.InvalidIndex)
            {
                if (seeded.TryGetValue(current, out float weight))
                {
                    mask._weights[i] = weight;
                    break;
                }
                current = skeleton.GetParentBoneIndex(current);
            }
        }
        return mask;
    }
}
