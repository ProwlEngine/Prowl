using System.Collections.Generic;
using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion;

/// <summary>
/// General N-bone inverse kinematics using Cyclic Coordinate Descent (CCD). Rotates each joint in
/// the chain, from the one nearest the end effector back toward the root, to aim the end effector at
/// the target, repeating for a number of iterations.
/// </summary>
public static class ChainIK
{
    private const float Epsilon = 1e-5f;
    private const int MaxStackChain = 32;
    private const int MaxStackPath = 64;

    /// <summary>
    /// Solves the chain (ordered root..end, each a direct or indirect ancestor of the next) so the
    /// last bone reaches the model space <paramref name="target"/>. Stops early once within
    /// <paramref name="tolerance"/>. The full solve is blended from the input pose by
    /// <paramref name="weight"/>. Invalid chains leave the pose untouched.
    /// </summary>
    public static void Solve(Pose pose, IReadOnlyList<int> chain, Float3 target, int iterations = 10, float tolerance = 1e-3f, float weight = 1f)
    {
        ArgumentNullException.ThrowIfNull(pose);
        ArgumentNullException.ThrowIfNull(chain);
        if (chain.Count < 2 || !(weight > 0f) || !float.IsFinite(target.X) || !float.IsFinite(target.Y) || !float.IsFinite(target.Z))
            return;

        Skeleton skeleton = pose.Skeleton;
        if (!IsValidChain(skeleton, chain))
            return;

        int jointCount = chain.Count - 1;
        Span<Quaternion> original = jointCount <= MaxStackChain ? stackalloc Quaternion[jointCount] : new Quaternion[jointCount];
        for (int j = 0; j < jointCount; j++)
            original[j] = pose.GetTransform(chain[j]).rotation;

        // The bones from the root down to the end bone, and the model transforms of just those bones.
        int[] parents = skeleton.SanitizedParentIndices;
        int endBone = chain[chain.Count - 1];
        int pathLength = 0;
        for (int bone = endBone; bone != Skeleton.InvalidIndex; bone = parents[bone])
            pathLength++;
        Span<int> path = pathLength <= MaxStackPath ? stackalloc int[pathLength] : new int[pathLength];
        Span<Transform3D> model = pathLength <= MaxStackPath ? stackalloc Transform3D[pathLength] : new Transform3D[pathLength];
        for (int bone = endBone, i = pathLength - 1; i >= 0; bone = parents[bone], i--)
            path[i] = bone;

        Span<int> jointAt = jointCount <= MaxStackChain ? stackalloc int[jointCount] : new int[jointCount];
        for (int j = 0, i = 0; j < jointCount; j++)
        {
            while (path[i] != chain[j])
                i++;
            jointAt[j] = i;
        }

        UpdateModel(pose, path, model, 0);
        int end = pathLength - 1;
        for (int iteration = 0; iteration < iterations; iteration++)
        {
            if (Float3.Distance(model[end].position, target) <= tolerance)
                break;

            for (int j = jointCount - 1; j >= 0; j--)
            {
                int at = jointAt[j];
                Transform3D jointWorld = model[at];
                Float3 toEnd = model[end].position - jointWorld.position;
                Float3 toTarget = target - jointWorld.position;
                if (Float3.Length(toEnd) < Epsilon || Float3.Length(toTarget) < Epsilon)
                    continue;

                Quaternion newWorld = TransformOps.FromToRotation(toEnd, toTarget) * jointWorld.rotation;
                Quaternion parentWorld = at == 0 ? Quaternion.Identity : model[at - 1].rotation;

                Transform3D local = pose.GetTransform(path[at]);
                pose.SetTransform(path[at], new Transform3D(local.position, Quaternion.Inverse(parentWorld) * newWorld, local.scale));
                UpdateModel(pose, path, model, at);
            }
        }

        if (weight >= 1f)
            return;

        for (int j = 0; j < jointCount; j++)
        {
            Transform3D local = pose.GetTransform(chain[j]);
            pose.SetTransform(chain[j], new Transform3D(local.position, Quaternion.Slerp(original[j], local.rotation, weight), local.scale));
        }
    }

    // Recomputes the model transforms along the path from the given position down.
    private static void UpdateModel(Pose pose, ReadOnlySpan<int> path, Span<Transform3D> model, int from)
    {
        for (int i = from; i < path.Length; i++)
            model[i] = i == 0 ? pose.GetTransform(path[i]) : TransformOps.Combine(model[i - 1], pose.GetTransform(path[i]));
    }

    /// <summary>True if every bone exists and each is an ancestor of the next.</summary>
    public static bool IsValidChain(Skeleton skeleton, IReadOnlyList<int> chain)
    {
        ArgumentNullException.ThrowIfNull(skeleton);
        ArgumentNullException.ThrowIfNull(chain);
        for (int i = 0; i < chain.Count; i++)
        {
            if (!skeleton.IsValidBoneIndex(chain[i]))
                return false;
            if (i > 0 && !skeleton.IsChildBoneOf(chain[i - 1], chain[i]))
                return false;
        }
        return true;
    }
}
