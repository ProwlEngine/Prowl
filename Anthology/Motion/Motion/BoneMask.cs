using Prowl.Vector;

namespace Prowl.Motion;

/// <summary>
/// A weight in [0,1] per bone and per float channel, restricting blends to part of the skeleton.
/// Channels start at 1.
/// </summary>
public sealed class BoneMask
{
    private readonly Skeleton _skeleton;
    private readonly float[] _weights;
    private readonly float[] _channels;

    /// <summary>Creates a mask for the skeleton with every weight set to <paramref name="fixedWeight"/>.</summary>
    public BoneMask(Skeleton skeleton, float fixedWeight = 0f)
    {
        ArgumentNullException.ThrowIfNull(skeleton);
        _skeleton = skeleton;
        _weights = new float[skeleton.BoneCount];
        if (fixedWeight != 0f)
            Array.Fill(_weights, Maths.Clamp(fixedWeight, 0f, 1f));
        _channels = new float[skeleton.FloatChannelCount];
        Array.Fill(_channels, 1f);
    }

    public Skeleton Skeleton => _skeleton;

    /// <summary>Number of weights (one per bone).</summary>
    public int Length => _weights.Length;

    /// <summary>Number of channel weights (one per float channel).</summary>
    public int ChannelCount => _channels.Length;

    public float GetWeight(int boneIndex) => _weights[boneIndex];

    public void SetWeight(int boneIndex, float weight) => _weights[boneIndex] = Maths.Clamp(weight, 0f, 1f);

    /// <summary>How much of a layer reaches one float channel.</summary>
    public float GetChannelWeight(int channelIndex) => _channels[channelIndex];

    public void SetChannelWeight(int channelIndex, float weight) => _channels[channelIndex] = Maths.Clamp(weight, 0f, 1f);

    /// <summary>Sets every channel weight to the given value.</summary>
    public void ResetChannelWeights(float weight) => Array.Fill(_channels, Maths.Clamp(weight, 0f, 1f));

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
        for (int i = 0; i < _channels.Length; i++)
            _channels[i] *= other._channels[i];
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
        for (int i = 0; i < _channels.Length; i++)
            _channels[i] = Maths.Lerp(_channels[i], target._channels[i], clamped);
    }

    public void CopyFrom(BoneMask other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (other._weights.Length != _weights.Length)
            throw new ArgumentException("Bone mask lengths must match.", nameof(other));

        Array.Copy(other._weights, _weights, _weights.Length);
        Array.Copy(other._channels, _channels, _channels.Length);
    }

    /// <summary>
    /// Builds a mask from seed weights: each bone takes the weight of its nearest seeded ancestor or
    /// itself, and bones with none take the rest weight.
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
