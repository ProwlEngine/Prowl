using System.Collections.Generic;
using Prowl.Vector;

namespace Prowl.Motion;

/// <summary>
/// A named IK effector: a bone chain (root..end) driven to a model space target. Three bone chains
/// that form a direct parent chain use the analytic two bone solver, every other chain uses CCD.
/// </summary>
public sealed class IKEffector
{
    public IKEffector(string name, IReadOnlyList<int> chain)
    {
        ArgumentNullException.ThrowIfNull(chain);
        if (chain.Count < 2)
            throw new ArgumentException("An IK effector chain needs at least two bones.", nameof(chain));
        Name = name;
        Chain = new List<int>(chain).ToArray();
    }

    public string Name { get; }
    public int[] Chain { get; }
    public Float3 Target { get; set; }
    public Float3? Pole { get; set; }
    public float Weight { get; set; } = 1f;
    public float Stretch { get; set; }
    public int Iterations { get; set; } = 12;
}

/// <summary>
/// A reusable multi-effector IK rig over a skeleton. Add effectors (arms, legs, spine, tail, etc.),
/// set their targets, and solve them all in one pass. General-purpose;
/// the humanoid foot/hand goal IK is a specialized use of the same two-bone solver.
/// </summary>
public sealed class IKRig
{
    private readonly List<IKEffector> _effectors = new();

    public IReadOnlyList<IKEffector> Effectors => _effectors;

    public IKEffector AddEffector(string name, IReadOnlyList<int> chain)
    {
        var effector = new IKEffector(name, chain);
        _effectors.Add(effector);
        return effector;
    }

    public IKEffector? Find(string name)
    {
        foreach (IKEffector e in _effectors)
            if (e.Name == name)
                return e;
        return null;
    }

    /// <summary>Solves every active effector against its target on the pose. Invalid chains are skipped.</summary>
    public void Solve(Pose pose)
    {
        ArgumentNullException.ThrowIfNull(pose);
        foreach (IKEffector e in _effectors)
        {
            if (!(e.Weight > 0f))
                continue;

            if (IsDirectThreeBoneChain(pose.Skeleton, e.Chain))
                TwoBoneIK.Solve(pose, e.Chain[0], e.Chain[1], e.Chain[2], e.Target, e.Weight, e.Pole, e.Stretch);
            else
                ChainIK.Solve(pose, e.Chain, e.Target, e.Iterations, weight: e.Weight);
        }
    }

    private static bool IsDirectThreeBoneChain(Skeleton skeleton, int[] chain)
        => chain.Length == 3
        && skeleton.IsValidBoneIndex(chain[0]) && skeleton.IsValidBoneIndex(chain[1]) && skeleton.IsValidBoneIndex(chain[2])
        && skeleton.GetParentBoneIndex(chain[1]) == chain[0]
        && skeleton.GetParentBoneIndex(chain[2]) == chain[1];
}
