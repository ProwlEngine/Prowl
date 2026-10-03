// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Vector;
using Prowl.Vector.Spatial;

using MotionSkeleton = Prowl.Motion.Skeleton;

namespace Prowl.Runtime;

/// <summary>
/// Ties a skeleton to the hierarchy under it: a Transform per bone, matched by name and then by path,
/// and the renderers each float channel drives.
/// </summary>
internal sealed class AnimatorBinding
{
    private readonly Transform _root;
    private readonly Transform?[] _bones;
    private readonly List<(SkinnedMeshRenderer Renderer, int Shape)>?[] _channels;

    /// <summary>Bones named by the rig that the hierarchy does not have.</summary>
    public int UnboundBones { get; }

    public AnimatorBinding(Transform root, MotionSkeleton skeleton)
    {
        _root = root;
        _bones = new Transform?[skeleton.BoneCount];
        _channels = new List<(SkinnedMeshRenderer, int)>?[skeleton.FloatChannelCount];

        var byName = new Dictionary<string, Transform>();
        var byPath = new Dictionary<string, Transform>();
        byName.TryAdd(root.GameObject.Name, root);
        byPath.TryAdd(string.Empty, root);
        Collect(root, byName, byPath);

        var bound = new bool[_bones.Length];
        int unbound = 0;
        for (int b = 0; b < _bones.Length; b++)
            if (Bind(skeleton, b, bound, byName, byPath) == null) unbound++;
        UnboundBones = unbound;

        BindChannels(skeleton);
    }

    // A bone is looked for under its parent bone first, so an object elsewhere that shares its name is never taken for it.
    private Transform? Bind(MotionSkeleton skeleton, int bone, bool[] bound, Dictionary<string, Transform> byName, Dictionary<string, Transform> byPath)
    {
        if (bound[bone]) return _bones[bone];
        bound[bone] = true;

        string? name = skeleton.GetBoneID(bone).DebugName;
        if (name == null) return null;

        int parent = skeleton.GetParentBoneIndex(bone);
        Transform? under = parent >= 0 && parent < _bones.Length ? Bind(skeleton, parent, bound, byName, byPath) : null;
        Transform? found = under != null ? FindUnder(under, name) : null;
        if (found == null && !byName.TryGetValue(name, out found)) byPath.TryGetValue(name, out found);
        return _bones[bone] = found;
    }

    // Nearest first, so a direct child wins over a deeper one of the same name.
    private static Transform? FindUnder(Transform parent, string name)
    {
        var queue = new Queue<Transform>();
        queue.Enqueue(parent);
        while (queue.Count > 0)
            foreach (GameObject child in queue.Dequeue().GameObject.Children)
            {
                if (child.Name == name) return child.Transform;
                queue.Enqueue(child.Transform);
            }
        return null;
    }

    private void Collect(Transform parent, Dictionary<string, Transform> byName, Dictionary<string, Transform> byPath)
    {
        foreach (GameObject child in parent.GameObject.Children)
        {
            Transform transform = child.Transform;
            byName.TryAdd(child.Name, transform);
            byPath.TryAdd(Transform.GetRelativePath(transform, _root), transform);
            Collect(transform, byName, byPath);
        }
    }

    // A channel drives the blend shape of that name on every renderer under the animator.
    private void BindChannels(MotionSkeleton skeleton)
    {
        if (_channels.Length == 0) return;

        var renderers = new List<SkinnedMeshRenderer>();
        CollectRenderers(_root, renderers);
        if (renderers.Count == 0) return;

        for (int c = 0; c < _channels.Length; c++)
        {
            string? name = skeleton.GetFloatChannelID(c).DebugName;
            if (name == null) continue;

            foreach (SkinnedMeshRenderer renderer in renderers)
            {
                int shape = renderer.GetBlendShapeIndex(name);
                if (shape < 0) continue;
                (_channels[c] ??= new List<(SkinnedMeshRenderer, int)>()).Add((renderer, shape));
            }
        }
    }

    private static void CollectRenderers(Transform parent, List<SkinnedMeshRenderer> into)
    {
        var own = parent.GameObject.GetComponent<SkinnedMeshRenderer>();
        if (own.IsValid()) into.Add(own);

        foreach (GameObject child in parent.GameObject.Children)
            CollectRenderers(child.Transform, into);
    }

    /// <summary>The Transform a bone is bound to, or null when the hierarchy does not have it.</summary>
    public Transform? BoneTransform(int index) => (uint)index < (uint)_bones.Length ? _bones[index] : null;

    public void ApplyBone(int index, in Transform3D local)
    {
        Transform? bone = _bones[index];
        // The animator's own Transform is moved by root motion, never by the pose.
        if (bone == null || ReferenceEquals(bone, _root) || bone.GameObject.IsNotValid()) return;

        bone.LocalPosition = local.position;
        bone.LocalRotation = local.rotation;
        bone.LocalScale = local.scale;
    }

    public void ApplyChannel(int index, float value)
    {
        List<(SkinnedMeshRenderer Renderer, int Shape)>? targets = _channels[index];
        if (targets == null) return;

        for (int i = 0; i < targets.Count; i++)
            if (targets[i].Renderer.IsValid())
                targets[i].Renderer.SetBlendShapeWeight(targets[i].Shape, value);
    }
}
