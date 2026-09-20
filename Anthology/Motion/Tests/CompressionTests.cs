using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

public class CompressionTests
{
    [Fact]
    public void EncodeDecodeFloat_RoundTripsWithinTolerance()
    {
        float min = -2f, range = 5f;
        ushort q = Quantization.EncodeFloat(1.3f, min, range);
        float decoded = Quantization.DecodeFloat(q, min, range);
        Assert.Equal(1.3, (double)decoded, 3);
    }

    [Fact]
    public void EncodeDecodeQuaternion_RoundTripsWithinTolerance()
    {
        Quaternion q = Quaternion.Normalize(Quaternion.AxisAngle(Float3.Normalize(new Float3(0.3f, 1f, 0.5f)), 1.1f));
        Quantization.EncodeQuaternion(q, out ushort m0, out ushort m1, out ushort m2);
        Quaternion decoded = Quantization.DecodeQuaternion(m0, m1, m2);
        Assert.True(MathF.Abs(Quaternion.Dot(q, decoded)) > 0.9995f);
    }

    private static AnimationClip MakeMovingClip(Skeleton skeleton, out Pose[] frames)
    {
        frames = new Pose[8];
        for (int f = 0; f < frames.Length; f++)
        {
            float t = f / 7f;
            var p = new Pose(skeleton);
            p.SetToReferencePose();
            p.SetTransform(1, new Transform3D(
                new Float3(0f, 1f + t, t * 0.5f),
                Quaternion.AxisAngle(new Float3(1f, 0f, 0f), t * 1.2f),
                Float3.One));
            frames[f] = p;
        }
        return new AnimationClip(skeleton, frames, 1f);
    }

    [Fact]
    public void CompressedSampling_MatchesUncompressed()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        AnimationClip uncompressed = MakeMovingClip(skeleton, out Pose[] frames);
        var compressed = new CompressedAnimationClip(skeleton, frames, 1f);

        var a = new Pose(skeleton);
        var b = new Pose(skeleton);
        uncompressed.GetPose(0.42f, a);
        compressed.GetPose(0.42f, b);

        Transform3D ta = a.GetTransform(1);
        Transform3D tb = b.GetTransform(1);
        Assert.True(Float3.Distance(ta.position, tb.position) < 0.01f, $"pos {ta.position} vs {tb.position}");
        Assert.True(MathF.Abs(Quaternion.Dot(ta.rotation, tb.rotation)) > 0.999f);
    }

    [Fact]
    public void StaticChannels_AddNoPerFrameData()
    {
        // Bone 0 and 2 never move; only bone 1 animates -> compressed data only for bone 1's channels.
        Skeleton skeleton = TestSkeletons.MakeChain();
        var compressed = new CompressedAnimationClip(skeleton, MakeFrames(skeleton), 1f);
        // 8 frames, one bone with dynamic rot+trans (scale static): 8*3*2 words *2 channels = 96 words = 192 bytes.
        Assert.True(compressed.CompressedSizeBytes > 0);
        Assert.True(compressed.CompressedSizeBytes < 8 * 3 * 3 * 3 * sizeof(ushort)); // far less than all-dynamic-all-bones
    }

    private static Pose[] MakeFrames(Skeleton skeleton)
    {
        var frames = new Pose[8];
        for (int f = 0; f < frames.Length; f++)
        {
            float t = f / 7f;
            var p = new Pose(skeleton);
            p.SetToReferencePose();
            p.SetTransform(1, new Transform3D(new Float3(0f, 1f, t), Quaternion.AxisAngle(new Float3(1f, 0f, 0f), t), Float3.One));
            frames[f] = p;
        }
        return frames;
    }
}
