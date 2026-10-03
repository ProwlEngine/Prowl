// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Clay;
using Prowl.Motion;
using Prowl.Vector;
using Prowl.Vector.Spatial;

using ClayAnim = Prowl.Clay.AnimationClip;
using MotionClip = Prowl.Motion.AnimationClip;
using MotionSkeleton = Prowl.Motion.Skeleton;

namespace Prowl.Runtime.AssetImporting;

/// <summary>
/// Turns a Clay model's node hierarchy and animation curves into the skeleton and sampled clips
/// Prowl.Motion plays.
/// </summary>
/// <remarks>
/// Clips are stored sampled and compressed rather than as curves. Curves are cheap to author and
/// expensive to sample; frames are the other way round, and a shipped game only ever samples.
/// </remarks>
internal static class ModelRigBuilder
{
    /// <summary>The bones and channels of one model, with the lookups the clip builder needs.</summary>
    internal sealed class Rig
    {
        public MotionSkeleton Skeleton = null!;
        /// <summary>Bone index per Clay node index. Bones are one per node, in node order.</summary>
        public string[] BonePaths = Array.Empty<string>();
        /// <summary>Channel index per (mesh index, blend shape index).</summary>
        public Dictionary<(int Mesh, int Shape), int> Channels = new();
    }

    /// <summary>
    /// Builds a skeleton covering every node of the model, so a clip can drive any of them, not only
    /// the ones a skin happens to reference. Bones are named after their node, or after their path when
    /// two nodes share a name, since a name that resolves to two bones resolves to neither.
    /// </summary>
    public static Rig BuildRig(Model model, GameObject[] nodeGOs, GameObject rootGO)
    {
        int count = model.Nodes.Count;
        var nameCounts = new Dictionary<string, int>(count);
        for (int i = 0; i < count; i++)
        {
            string name = nodeGOs[i].Name;
            nameCounts[name] = nameCounts.TryGetValue(name, out int n) ? n + 1 : 1;
        }

        var ids = new StringID[count];
        var parents = new int[count];
        var reference = new Transform3D[count];
        var paths = new string[count];

        for (int i = 0; i < count; i++)
        {
            ModelNode node = model.Nodes[i];
            string name = nodeGOs[i].Name;
            paths[i] = Transform.GetRelativePath(nodeGOs[i].Transform, rootGO.Transform);

            ids[i] = new StringID(nameCounts[name] == 1 ? name : paths[i]);
            parents[i] = node.Parent?.Index ?? MotionSkeleton.InvalidIndex;
            reference[i] = new Transform3D(node.LocalPosition, node.LocalRotation, node.LocalScale);
        }

        var rig = new Rig { BonePaths = paths };
        List<StringID> channelIds = CollectChannels(model, rig.Channels);
        rig.Skeleton = new MotionSkeleton(ids, parents, reference, -1, channelIds);
        return rig;
    }

    /// <summary>
    /// One channel per distinct blend shape name across the model. Sharing a name is deliberate: a
    /// "Smile" authored on both the head and the teeth is one thing an animation drives, not two.
    /// </summary>
    private static List<StringID> CollectChannels(Model model, Dictionary<(int, int), int> channels)
    {
        var ids = new List<StringID>();
        var byName = new Dictionary<string, int>();

        for (int m = 0; m < model.Meshes.Count; m++)
        {
            Prowl.Clay.Mesh mesh = model.Meshes[m];
            if (mesh.BlendShapes is not { Length: > 0 }) continue;

            for (int s = 0; s < mesh.BlendShapes.Length; s++)
            {
                string name = mesh.BlendShapes[s].Name ?? $"BlendShape{s}";
                if (!byName.TryGetValue(name, out int channel))
                {
                    channel = ids.Count;
                    byName[name] = channel;
                    ids.Add(new StringID(name));
                }
                channels[(m, s)] = channel;
            }
        }
        return ids;
    }

    /// <summary>
    /// Which parts of a bone's movement a clip moves the character by, rather than keeping in the pose.
    /// The bone is the one carrying the body, the hips on a humanoid.
    /// </summary>
    public readonly record struct RootExtraction(int Bone, bool Travel, bool Turn, bool Height)
    {
        public static readonly RootExtraction None = new(-1, false, false, false);

        public bool Any => Bone >= 0 && (Travel || Turn || Height);
    }

    /// <summary>
    /// Samples a Clay clip onto the rig at a fixed rate and compresses the result. Bones the clip says
    /// nothing about hold their reference pose, so a clip animating one finger stays a clip about one
    /// finger.
    /// </summary>
    public static CompressedAnimationClip BuildClip(ClayAnim source, Model model, Rig rig, float sampleRate, float trimStart = 0f, float trimEnd = 0f)
        => BuildClip(source, model, rig, sampleRate, trimStart, trimEnd, RootExtraction.None);

    public static CompressedAnimationClip BuildClip(ClayAnim source, Model model, Rig rig, float sampleRate, float trimStart, float trimEnd, RootExtraction root)
    {
        MotionSkeleton skeleton = rig.Skeleton;
        float rate = sampleRate > 0f ? sampleRate : 30f;
        (float start, float end) = Trim(source, rate, trimStart, trimEnd);
        float duration = end - start;
        int frameCount = Math.Max(2, (int)MathF.Round(duration * rate) + 1);
        float step = frameCount > 1 ? duration / (frameCount - 1) : 0f;

        var poses = new Pose[frameCount];
        var previousRotation = new Quaternion[skeleton.BoneCount];
        bool hasPrevious = false;

        for (int f = 0; f < frameCount; f++)
        {
            float time = source.StartTime + start + f * step;
            var pose = new Pose(skeleton);
            pose.SetToReferencePose(false);

            foreach (AnimationBinding binding in source.Bindings)
            {
                if (binding.NodeIndex < 0 || binding.NodeIndex >= skeleton.BoneCount) continue;

                if (binding.Property == AnimatedProperty.BlendShapeWeight)
                {
                    SampleChannel(binding, model, rig, pose, time);
                    continue;
                }
                SampleBone(binding, pose, time);
            }

            // Sampling a curve whose keys flip sign gives frames a half turn apart, which the clip then
            // interpolates the long way round. Keeping each frame on the same side of the previous one
            // is the same rule the curve applies to its own keys.
            if (hasPrevious) KeepRotationsContinuous(pose, previousRotation, skeleton.BoneCount);
            for (int b = 0; b < skeleton.BoneCount; b++)
                previousRotation[b] = pose.GetTransform(b).rotation;
            hasPrevious = true;

            poses[f] = pose;
        }

        RootMotion? rootMotion = root.Any ? ExtractRootMotion(poses, skeleton, duration, root) : null;
        return new CompressedAnimationClip(new MotionClip(skeleton, poses, duration, rootMotion: rootMotion));
    }

    /// <summary>
    /// The slice of the take a clip keeps, in seconds of the take. A slice that is empty or inside out
    /// is ignored rather than obeyed: the clip the file describes is a better answer than no clip at all.
    /// </summary>
    public static (float Start, float End) Trim(ClayAnim source, float sampleRate, float trimStart, float trimEnd)
    {
        // A file can hold a clip with no length: a single pose, or keys that all sit at the same time.
        // A clip has to last something, so it becomes one frame's worth and plays as a held pose.
        float rate = sampleRate > 0f ? sampleRate : 30f;
        float full = float.IsFinite(source.Duration) && source.Duration > 0f ? source.Duration : 1f / rate;

        float start = Math.Clamp(float.IsFinite(trimStart) ? trimStart : 0f, 0f, full);
        float end = float.IsFinite(trimEnd) && trimEnd > 0f ? Math.Clamp(trimEnd, 0f, full) : full;
        if (!(end - start > 1e-4f)) { start = 0f; end = full; }
        return (start, end);
    }

    /// <summary>
    /// Moves markers timed on the take onto a clip cut from it: shifted by where the slice starts, cut to
    /// the slice, and dropped when they fall wholly outside it.
    /// </summary>
    public static List<ClipEvent> PlaceEvents(IReadOnlyList<ClipEvent> onTake, float start, float end)
    {
        var placed = new List<ClipEvent>(onTake.Count);
        foreach (ClipEvent e in onTake)
        {
            float from = Math.Max(e.Time, start);
            float to = Math.Min(e.Time + Math.Max(0f, e.Length), end);
            if (from > end || to < start) continue;

            ClipEvent copy = e.Clone();
            copy.Time = from - start;
            copy.Length = Math.Max(0f, to - from);
            placed.Add(copy);
        }
        return placed;
    }

    /// <summary>
    /// The bone a clip moves the character by when nothing says otherwise: the highest one in the
    /// hierarchy whose position the clip moves, which is the hips on most rigs. A file that keys every
    /// bone keys the ones that stand still too, so a track that never moves counts only when none do.
    /// </summary>
    public static int AnimatedRootBone(ClayAnim source, MotionSkeleton skeleton)
    {
        int moving = -1, movingDepth = int.MaxValue, keyed = -1, keyedDepth = int.MaxValue;
        foreach (AnimationBinding binding in source.Bindings)
        {
            if (binding.Property != AnimatedProperty.Position || binding.NodeIndex < 0 || binding.NodeIndex >= skeleton.BoneCount) continue;

            int depth = 0;
            for (int b = skeleton.GetParentBoneIndex(binding.NodeIndex); b >= 0; b = skeleton.GetParentBoneIndex(b)) depth++;
            if (depth < keyedDepth) { keyed = binding.NodeIndex; keyedDepth = depth; }
            if (depth < movingDepth && Moves(binding.Curve)) { moving = binding.NodeIndex; movingDepth = depth; }
        }
        return moving >= 0 ? moving : keyed;
    }

    private static bool Moves(Prowl.Vector.AnimationCurve curve)
    {
        for (int i = 1; i < curve.Count; i++)
            if (Float3.LengthSquared(curve[i].Value3 - curve[0].Value3) > 1e-10f) return true;
        return false;
    }

    /// <summary>
    /// Moves the body bone's travel and turn out of the pose and into root motion, relative to where the
    /// clip starts, so a walk plays on the spot and moves the character instead of sliding away from it
    /// and snapping back each loop. The turn is taken as an even sweep from the first frame's heading to
    /// the last, which keeps the sway of a stride in the pose and the net turn of a turn in the root.
    /// </summary>
    private static RootMotion ExtractRootMotion(Pose[] poses, MotionSkeleton skeleton, float duration, RootExtraction root)
    {
        int count = poses.Length;
        var parent = new Transform3D[count];
        var body = new Transform3D[count];
        for (int f = 0; f < count; f++)
        {
            parent[f] = ParentModelSpace(poses[f], skeleton, root.Bone);
            body[f] = Combine(parent[f], poses[f].GetTransform(root.Bone));
        }

        float turn = root.Turn ? NetTurn(body) : 0f;
        Float3 start = body[0].position;

        var frames = new Transform3D[count];
        for (int f = 0; f < count; f++)
        {
            float t = count > 1 ? f / (float)(count - 1) : 0f;
            Float3 moved = body[f].position - start;
            var position = new Float3(root.Travel ? moved.X : 0f, root.Height ? moved.Y : 0f, root.Travel ? moved.Z : 0f);
            frames[f] = new Transform3D(position, Quaternion.AxisAngle(Float3.UnitY, turn * t), Float3.One);

            // The body keeps whatever the root does not take, in the root's frame.
            Transform3D kept = Delta(frames[f], body[f]);
            poses[f].SetTransform(root.Bone, Delta(parent[f], kept));
        }
        return new RootMotion(frames, duration);
    }

    /// <summary>
    /// The heading the body turns through across the clip, in radians, followed frame to frame so a turn
    /// past half a circle still reads as the turn it is.
    /// </summary>
    private static float NetTurn(Transform3D[] body)
    {
        // Whichever of the bone's own axes lies flattest on the ground at the start is the one that
        // tells its heading; a bone pointing up the spine says nothing about which way it faces.
        Float3 axis = Flatness(body[0].rotation * Float3.UnitZ) >= Flatness(body[0].rotation * Float3.UnitX) ? Float3.UnitZ : Float3.UnitX;

        float total = 0f;
        float previous = Heading(body[0].rotation * axis);
        for (int f = 1; f < body.Length; f++)
        {
            float heading = Heading(body[f].rotation * axis);
            float step = heading - previous;
            while (step > MathF.PI) step -= MathF.Tau;
            while (step < -MathF.PI) step += MathF.Tau;
            total += step;
            previous = heading;
        }
        return total;
    }

    private static float Flatness(Float3 direction) => direction.X * direction.X + direction.Z * direction.Z;

    private static float Heading(Float3 direction) => MathF.Atan2(direction.X, direction.Z);

    private static Transform3D ParentModelSpace(Pose pose, MotionSkeleton skeleton, int bone)
    {
        Transform3D result = Transform3D.Identity;
        var chain = new List<int>();
        for (int b = skeleton.GetParentBoneIndex(bone); b >= 0; b = skeleton.GetParentBoneIndex(b)) chain.Add(b);
        for (int i = chain.Count - 1; i >= 0; i--) result = Combine(result, pose.GetTransform(chain[i]));
        return result;
    }

    private static Transform3D Combine(in Transform3D parent, in Transform3D child)
        => new(parent.position + parent.rotation * (parent.scale * child.position), parent.rotation * child.rotation, parent.scale * child.scale);

    /// <summary>The transform that takes <paramref name="from"/> to <paramref name="to"/>, so Combine(from, it) is to.</summary>
    private static Transform3D Delta(in Transform3D from, in Transform3D to)
    {
        Quaternion inverse = Quaternion.Inverse(from.rotation);
        var invScale = new Float3(1f / from.scale.X, 1f / from.scale.Y, 1f / from.scale.Z);
        return new Transform3D(invScale * (inverse * (to.position - from.position)), inverse * to.rotation, invScale * to.scale);
    }

    private static void SampleBone(AnimationBinding binding, Pose pose, float time)
    {
        if (binding.Curve is not { Count: > 0 } curve) return;

        Transform3D current = pose.GetTransform(binding.NodeIndex);
        Transform3D sampled = binding.Property switch
        {
            AnimatedProperty.Position => new Transform3D(curve.EvaluateFloat3(time), current.rotation, current.scale),
            AnimatedProperty.Rotation => new Transform3D(current.position, curve.EvaluateQuaternion(time), current.scale),
            AnimatedProperty.Scale => new Transform3D(current.position, current.rotation, curve.EvaluateFloat3(time)),
            _ => current,
        };
        pose.SetTransform(binding.NodeIndex, sampled);
    }

    private static void SampleChannel(AnimationBinding binding, Model model, Rig rig, Pose pose, float time)
    {
        if (binding.Curve is not { Count: > 0 } curve) return;

        int mesh = model.Nodes[binding.NodeIndex].MeshIndex;
        if (mesh < 0 || !rig.Channels.TryGetValue((mesh, binding.SubIndex), out int channel)) return;

        pose.SetFloat(channel, curve.Evaluate(time));
    }

    private static void KeepRotationsContinuous(Pose pose, Quaternion[] previous, int boneCount)
    {
        for (int b = 0; b < boneCount; b++)
        {
            Transform3D current = pose.GetTransform(b);
            if (Quaternion.Dot(previous[b], current.rotation) >= 0f) continue;

            Quaternion flipped = new(-current.rotation.X, -current.rotation.Y, -current.rotation.Z, -current.rotation.W);
            pose.SetTransform(b, new Transform3D(current.position, flipped, current.scale));
        }
    }
}
