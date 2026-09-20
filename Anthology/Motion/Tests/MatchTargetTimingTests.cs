using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

public class MatchTargetTimingTests
{
    private static readonly Float3 Up = new(0f, 1f, 0f);
    private static readonly Transform3D PartOffset = new(new Float3(0f, 0f, 1f), Quaternion.Identity, Float3.One);

    private static Transform3D Compose(Transform3D parent, Transform3D child)
        => new(parent.TransformPoint(child.position), parent.rotation * child.rotation, Float3.One);

    // Runs a still clip at the given frame rate while matching the body part onto the target, returning the part error at each checkpoint.
    private static float[] Simulate(int fps, Transform3D target, float rotationWeight, params float[] checkpoints)
    {
        var errors = new float[checkpoints.Length];
        Transform3D character = Transform3D.Identity;
        int frames = fps;
        for (int i = 1; i <= frames; i++)
        {
            float prevTime = (i - 1) / (float)fps;
            float time = i / (float)fps;
            Transform3D part = Compose(character, PartOffset);
            Transform3D correction = MatchTarget.ComputeCorrection(part, target, Float3.One, rotationWeight,
                MatchTarget.ComputeRampWeight(prevTime, 0.2f, 0.6f), MatchTarget.ComputeRampWeight(time, 0.2f, 0.6f));
            character = Compose(character, MatchTarget.CorrectRootDelta(character, Transform3D.Identity, correction));

            for (int c = 0; c < checkpoints.Length; c++)
                if (MathF.Abs(time - checkpoints[c]) < 1e-4f)
                    errors[c] = Float3.Distance(Compose(character, PartOffset).position, target.position);
        }
        return errors;
    }

    [Fact]
    public void Correction_ProgressIsIndependentOfFrameRate()
    {
        var target = new Transform3D(new Float3(2f, 0f, 1f), Quaternion.Identity, Float3.One);
        float[] at30 = Simulate(30, target, 0f, 0.4f, 0.6f);
        float[] at120 = Simulate(120, target, 0f, 0.4f, 0.6f);

        Assert.Equal(1.0, (double)at30[0], 3);
        Assert.Equal(1.0, (double)at120[0], 3);
        Assert.Equal(0.0, (double)at30[1], 3);
        Assert.Equal(0.0, (double)at120[1], 3);
    }

    [Fact]
    public void Correction_RotatesAboutTheBodyPart()
    {
        Transform3D part = PartOffset;
        var target = new Transform3D(part.position, Quaternion.AxisAngle(Up, MathF.PI / 2f), Float3.One);

        Transform3D correction = MatchTarget.ComputeCorrection(part, target, Float3.One, 1f, 1f);
        Transform3D character = Compose(correction, Transform3D.Identity);
        Transform3D corrected = Compose(character, PartOffset);

        Assert.True(Float3.Distance(corrected.position, part.position) < 1e-4f, $"part moved to {corrected.position}");
        Assert.True(Quaternion.Angle(corrected.rotation, target.rotation) < 1e-3f);
    }

    [Fact]
    public void Correction_RotatingAndTranslatingLandsThePartOnTheTarget()
    {
        var target = new Transform3D(new Float3(1f, 0f, 3f), Quaternion.AxisAngle(Up, MathF.PI / 2f), Float3.One);
        float[] errors = Simulate(60, target, 1f, 0.6f);

        Assert.Equal(0.0, (double)errors[0], 3);
    }
}
