using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion;

/// <summary>
/// Steers root motion so a chosen body part reaches a target by a given point in the animation,
/// ramped over a window. Each update the
/// caller samples where the body part will be at the target time (in the same space as the target),
/// asks for the correction for the step from the previous to the current ramp weight, and folds it into
/// the root delta with <see cref="CorrectRootDelta"/>. The correction closes the remaining error in
/// proportion to the ramp advance, so it completes exactly at the target time whatever the frame rate.
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
    /// A correction that moves <paramref name="bodyPart"/> the given <paramref name="fraction"/> of the
    /// way to <paramref name="matchTarget"/>, masked by the per axis position weight and the rotation
    /// weight. It is a transform in the target's space, pre applied to the character: the rotation
    /// pivots about the body part, so the part turns in place and moves only by the translation.
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
