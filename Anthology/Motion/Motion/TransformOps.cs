using System.Collections.Generic;
using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion;

/// <summary>
/// Shared VQS (translation / rotation / scale) transform operations used by the skeleton, poses,
/// root motion, and the blender. Composition order is parent then child, matching
/// <see cref="Transform3D.TransformPoint"/>.
/// </summary>
internal static class TransformOps
{
    /// <summary>Composes a parent transform with a child local transform (parent then child).</summary>
    public static Transform3D Combine(in Transform3D parent, in Transform3D child)
    {
        Quaternion rotation = parent.rotation * child.rotation;
        Float3 rotated = parent.rotation * Mul(child.position, parent.scale);
        return new Transform3D(parent.position + rotated, rotation, Mul(parent.scale, child.scale));
    }

    /// <summary>
    /// The transform that undoes <paramref name="t"/> when composed after it, so Combine(t, Inverse(t))
    /// is the identity. It is also a left inverse whenever the scale is uniform.
    /// </summary>
    public static Transform3D Inverse(in Transform3D t) => Delta(t, Transform3D.Identity);

    /// <summary>
    /// The transform that takes <paramref name="from"/> to <paramref name="to"/> (to in from's frame),
    /// exact for any scale: Combine(from, Delta(from, to)) equals <paramref name="to"/>.
    /// </summary>
    public static Transform3D Delta(in Transform3D from, in Transform3D to)
    {
        Quaternion invRotation = Quaternion.Inverse(from.rotation);
        Float3 invScale = Reciprocal(from.scale);
        Float3 position = Mul(invRotation * (to.position - from.position), invScale);
        return new Transform3D(position, invRotation * to.rotation, Mul(to.scale, invScale));
    }

    /// <summary>Maps a point from the space <paramref name="t"/> transforms into, back into t's local space.</summary>
    public static Float3 InverseTransformPoint(in Transform3D t, Float3 point)
        => Mul(Quaternion.Inverse(t.rotation) * (point - t.position), Reciprocal(t.scale));

    /// <summary>The shortest rotation that takes the <paramref name="from"/> direction onto <paramref name="to"/>.</summary>
    public static Quaternion FromToRotation(Float3 from, Float3 to)
    {
        Float3 f = SafeNormalize(from);
        Float3 g = SafeNormalize(to);
        float d = Float3.Dot(f, g);

        if (d >= 1f - 1e-6f)
            return Quaternion.Identity;
        if (d <= -1f + 1e-6f)
            return Quaternion.AxisAngle(AnyPerpendicular(f), MathF.PI);

        Float3 axis = Float3.Normalize(Float3.Cross(f, g));
        float angle = MathF.Acos(Math.Clamp(d, -1f, 1f));
        return Quaternion.AxisAngle(axis, angle);
    }

    /// <summary>An arbitrary unit vector perpendicular to <paramref name="v"/>.</summary>
    public static Float3 AnyPerpendicular(Float3 v)
    {
        Float3 reference = MathF.Abs(v.X) < 0.9f ? new Float3(1f, 0f, 0f) : new Float3(0f, 1f, 0f);
        return Float3.Normalize(Float3.Cross(v, reference));
    }

    /// <summary>Normalizes a vector, returning +Z for a near-zero input.</summary>
    public static Float3 SafeNormalize(Float3 v)
    {
        float length = Float3.Length(v);
        return length < 1e-8f ? new Float3(0f, 0f, 1f) : new Float3(v.X / length, v.Y / length, v.Z / length);
    }

    /// <summary>Computes model-space transforms from parent-space ones into <paramref name="output"/>.</summary>
    /// <remarks>Works for any bone ordering. A bone whose parent link closes a cycle is treated as a root.</remarks>
    public static void ComputeModelSpace(IReadOnlyList<Transform3D> local, IReadOnlyList<int> parents, Transform3D[] output)
    {
        var hierarchy = new BoneHierarchy(parents);
        ComputeModelSpace(local, hierarchy.Order, hierarchy.Parents, output);
    }

    /// <summary>Computes model space transforms for the first <paramref name="count"/> bones and their ancestors.</summary>
    public static void ComputeModelSpace(IReadOnlyList<Transform3D> local, IReadOnlyList<int> parents, Transform3D[] output, int count)
    {
        var hierarchy = new BoneHierarchy(parents);
        ComputeModelSpace(local, hierarchy.SubsetOrder(count), hierarchy.Parents, output);
    }

    /// <summary>
    /// Computes model space transforms walking a parents first <paramref name="order"/> over sanitized
    /// <paramref name="parents"/> (see <see cref="BoneHierarchy"/>). Bones missing from the order are untouched.
    /// </summary>
    public static void ComputeModelSpace(IReadOnlyList<Transform3D> local, int[] order, int[] parents, Transform3D[] output)
    {
        foreach (int bone in order)
        {
            int p = parents[bone];
            output[bone] = p < 0 ? local[bone] : Combine(output[p], local[bone]);
        }
    }

    private static Float3 Mul(Float3 a, Float3 b) => new(a.X * b.X, a.Y * b.Y, a.Z * b.Z);

    private static Float3 Reciprocal(Float3 v) => new(SafeReciprocal(v.X), SafeReciprocal(v.Y), SafeReciprocal(v.Z));

    private static float SafeReciprocal(float v) => MathF.Abs(v) < 1e-12f ? 0f : 1f / v;
}
