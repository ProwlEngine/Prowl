using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>Clip compression: quantization, static channels and sampling compressed clips.</summary>
public class Compression_Tests
{
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

    [Fact]
    public void CompressedClip_KeepsASmallRotationRamp()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var frames = new Pose[11];
        for (int i = 0; i < frames.Length; i++)
        {
            frames[i] = new Pose(skeleton);
            frames[i].SetToReferencePose();
            float radians = 0.5f * i / 10f * MathF.PI / 180f;
            frames[i].SetTransform(1, new Transform3D(new Float3(0f, 1f, 0f), Quaternion.AxisAngle(new Float3(0f, 1f, 0f), radians), Float3.One));
        }

        var clip = new CompressedAnimationClip(skeleton, frames, 1f);
        var pose = new Pose(skeleton);
        clip.GetPose(1f, pose);

        Assert.True(clip.CompressedSizeBytes > 0);
        float degrees = 2f * MathF.Acos(Math.Clamp(MathF.Abs(pose.GetTransform(1).rotation.W), 0f, 1f)) * 180f / MathF.PI;
        Assert.Equal(0.5, (double)degrees, 2);
    }

    [Fact]
    public void CompressedClip_PlaysInTheGraphWithRootMotionAndEvents()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        AnimationClip source = TestClips.Ramp(skeleton, rootTravel: 2f, events: new AnimationEvent[] { new IdEvent(new StringID("hit"), 0.05f) });
        var compressed = new CompressedAnimationClip(source);
        var g = new AnimationGraph();
        g.SetRoot(g.AddClip(compressed));
        AnimationGraphInstance instance = g.CreateInstance(skeleton);

        instance.Update(0.1f);

        Assert.Equal(0.2, (double)instance.RootMotionDelta.position.Z, 3);
        Assert.Equal(1.0, (double)TestClips.RootZ(instance), 2);
        Assert.Equal(1, TestClips.CountId(instance.Events, "hit"));
    }

    [Fact]
    public void Quantization_NormalizesBeforeEncoding()
    {
        Quantization.EncodeQuaternion(new Quaternion(0f, 0f, 1.2f, 1.6f), out ushort m0, out ushort m1, out ushort m2);
        Quaternion decoded = Quantization.DecodeQuaternion(m0, m1, m2);

        Assert.Equal(0.6, (double)decoded.Z, 3);
        Assert.Equal(0.8, (double)decoded.W, 3);
    }
}
