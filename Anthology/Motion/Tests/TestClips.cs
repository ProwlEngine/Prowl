using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

/// <summary>Synthetic clips for the regression tests. Bone 0 Z encodes the sampled time.</summary>
internal static class TestClips
{
    /// <summary>A clip holding bone 0 at a constant Z.</summary>
    public static AnimationClip Const(Skeleton skeleton, float z, params AnimationEvent[] events)
    {
        Pose frame = At(skeleton, z);
        return new AnimationClip(skeleton, new[] { frame, frame }, 1f, events: events);
    }

    /// <summary>
    /// A clip whose bone 0 Z ramps linearly from 0 to <paramref name="endZ"/> over the clip (so Z is
    /// endZ times the normalized time), with optional root motion travelling <paramref name="rootTravel"/>
    /// along Z per loop.
    /// </summary>
    public static AnimationClip Ramp(Skeleton skeleton, float endZ = 10f, float duration = 1f, float rootTravel = 0f, SyncTrack? syncTrack = null, int frames = 11, params AnimationEvent[] events)
    {
        var poses = new Pose[frames];
        var root = new Transform3D[frames];
        for (int i = 0; i < frames; i++)
        {
            float t = i / (float)(frames - 1);
            poses[i] = At(skeleton, endZ * t);
            root[i] = new Transform3D(new Float3(0f, 0f, rootTravel * t), Quaternion.Identity, Float3.One);
        }
        RootMotion? rootMotion = rootTravel != 0f ? new RootMotion(root, duration) : null;
        return new AnimationClip(skeleton, poses, duration, rootMotion: rootMotion, syncTrack: syncTrack, events: events);
    }

    public static Pose At(Skeleton skeleton, float z)
    {
        var pose = new Pose(skeleton);
        pose.SetToReferencePose();
        pose.SetTransform(0, new Transform3D(new Float3(0f, 0f, z), Quaternion.Identity, Float3.One));
        return pose;
    }

    public static float RootZ(AnimationGraphInstance instance) => instance.Pose.GetTransform(0).position.Z;

    public static SyncTrack Track(params float[] starts)
    {
        var events = new SyncEvent[starts.Length];
        for (int i = 0; i < starts.Length; i++)
            events[i] = new SyncEvent(new StringID("e" + i), starts[i]);
        return new SyncTrack(events);
    }

    public static int CountId(SampledEventsBuffer buffer, string id)
    {
        var sid = new StringID(id);
        int count = 0;
        foreach (SampledEvent e in buffer)
            if (!e.IsIgnored && e.Event is IdEvent idEvent && idEvent.Id == sid)
                count++;
        return count;
    }
}
