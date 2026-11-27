using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion;

/// <summary>
/// Steers root motion so a body part reaches a target by a point in the animation. Each update, ask
/// for the correction for the ramp's advance and fold it into the root delta with
/// <see cref="CorrectRootDelta"/>.
/// </summary>
public static class MatchTarget
{
    /// <summary>Ramp 0 at <paramref name="startTime"/> rising to 1 at <paramref name="targetTime"/>.</summary>
    public static float ComputeRampWeight(float currentNormalizedTime, float startTime, float targetTime)
    {
        if (targetTime <= startTime)
            return currentNormalizedTime >= targetTime ? 1f : 0f;
        return Math.Clamp((currentNormalizedTime - startTime) / (targetTime - startTime), 0f, 1f);
    }

    /// <summary>
    /// The correction for one update in which the ramp advanced from <paramref name="previousRampWeight"/>
    /// to <paramref name="currentRampWeight"/>: the share (current minus previous) over (1 minus previous)
    /// of the remaining error. See <see cref="ComputeCorrection(Transform3D, Transform3D, Float3, float, float)"/>.
    /// </summary>
    public static Transform3D ComputeCorrection(Transform3D bodyPart, Transform3D matchTarget, Float3 positionWeight, float rotationWeight, float previousRampWeight, float currentRampWeight)
    {
        float previous = Math.Clamp(previousRampWeight, 0f, 1f);
        float current = Math.Clamp(currentRampWeight, 0f, 1f);
        if (current <= previous)
            return Transform3D.Identity;
        float fraction = previous >= 1f ? 1f : (current - previous) / (1f - previous);
        return ComputeCorrection(bodyPart, matchTarget, positionWeight, rotationWeight, fraction);
    }

    /// <summary>
    /// A correction moving <paramref name="bodyPart"/> the given <paramref name="fraction"/> of the way to
    /// <paramref name="matchTarget"/>, pivoting about the body part, in the target's space.
    /// </summary>
    public static Transform3D ComputeCorrection(Transform3D bodyPart, Transform3D matchTarget, Float3 positionWeight, float rotationWeight, float fraction)
    {
        float f = Math.Clamp(fraction, 0f, 1f);

        Float3 error = matchTarget.position - bodyPart.position;
        var translation = new Float3(error.X * positionWeight.X * f, error.Y * positionWeight.Y * f, error.Z * positionWeight.Z * f);

        Quaternion rotation = Quaternion.Slerp(
            Quaternion.Identity,
            matchTarget.rotation * Quaternion.Inverse(bodyPart.rotation),
            Math.Clamp(f * rotationWeight, 0f, 1f));

        Float3 pivot = bodyPart.position;
        return new Transform3D(pivot + translation - rotation * pivot, rotation, Float3.One);
    }

    /// <summary>
    /// Folds a correction into a root delta. <paramref name="characterTransform"/> is the character in
    /// the correction's space before the delta is applied. The result, applied as
    /// character * delta, lands the character where correction * character * rootDelta would.
    /// </summary>
    public static Transform3D CorrectRootDelta(Transform3D characterTransform, Transform3D rootDelta, Transform3D correction)
    {
        Transform3D corrected = TransformOps.Combine(correction, TransformOps.Combine(characterTransform, rootDelta));
        return TransformOps.Delta(characterTransform, corrected);
    }
}
