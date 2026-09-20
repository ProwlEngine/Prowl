using System.Collections.Generic;
using System.IO;
using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion;

/// <summary>
/// Reads and writes the library's data as a compact binary stream, so a host engine can store
/// skeletons, clips and humanoid descriptions without knowing how they are laid out. Compressed clips
/// keep their quantized form, so saving and loading one costs no extra precision.
/// </summary>
/// <remarks>
/// Events and secondary clips are not written. They carry host meaning (an id event usually points at
/// game code), so the host saves them its own way and passes them back when reading a clip.
/// </remarks>
public static class MotionBinary
{
    private const uint SkeletonMagic = 0x4D534B4Cu;  // MSKL
    private const uint ClipMagic = 0x4D434C50u;      // MCLP
    private const uint HumanoidMagic = 0x4D48434Cu;  // MHCL
    private const uint DescriptionMagic = 0x4D484443u; // MHDC
    // 2 added the scalar channels a humanoid clip carries beside its muscles.
    private const int Version = 2;

    private enum ClipKind : byte { Uncompressed = 0, Compressed = 1 }

    // ---- skeleton ------------------------------------------------------------------------------

    /// <summary>Writes a skeleton: bone names, parents, reference pose, LOD split and float channels.</summary>
    public static void Write(BinaryWriter writer, Skeleton skeleton)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(skeleton);

        writer.Write(SkeletonMagic);
        writer.Write(Version);
        writer.Write(skeleton.BoneCount);
        for (int b = 0; b < skeleton.BoneCount; b++)
        {
            WriteId(writer, skeleton.GetBoneID(b));
            writer.Write(skeleton.GetParentBoneIndex(b));
            Write(writer, skeleton.GetBoneParentSpaceTransform(b));
        }
        writer.Write(skeleton.LowLodBoneCount);
        writer.Write(skeleton.FloatChannelCount);
        for (int c = 0; c < skeleton.FloatChannelCount; c++)
            WriteId(writer, skeleton.GetFloatChannelID(c));
    }

    public static Skeleton ReadSkeleton(BinaryReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ReadHeader(reader, SkeletonMagic);

        int count = reader.ReadInt32();
        var ids = new StringID[count];
        var parents = new int[count];
        var reference = new Transform3D[count];
        for (int b = 0; b < count; b++)
        {
            ids[b] = ReadId(reader);
            parents[b] = reader.ReadInt32();
            reference[b] = ReadTransform(reader);
        }

        int lowLod = reader.ReadInt32();
        int channelCount = reader.ReadInt32();
        var channels = new StringID[channelCount];
        for (int c = 0; c < channelCount; c++)
            channels[c] = ReadId(reader);

        return new Skeleton(ids, parents, reference, lowLod, channels);
    }

    // ---- clips ---------------------------------------------------------------------------------

    /// <summary>Writes a clip's timing, key frames, root motion and sync track (not its events).</summary>
    public static void Write(BinaryWriter writer, AnimationClipBase clip)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(clip);

        writer.Write(ClipMagic);
        writer.Write(Version);
        writer.Write(clip.FrameCount);
        writer.Write(clip.Duration);
        writer.Write(clip.IsAdditive);
        Write(writer, clip.RootMotion);
        Write(writer, clip.SyncTrack);

        switch (clip)
        {
            case AnimationClip uncompressed:
                writer.Write((byte)ClipKind.Uncompressed);
                int bones = uncompressed.Skeleton.BoneCount;
                int channels = uncompressed.Skeleton.FloatChannelCount;
                for (int f = 0; f < uncompressed.FrameCount; f++)
                {
                    for (int b = 0; b < bones; b++)
                        Write(writer, uncompressed.GetKeyFrameTransform(f, b));
                    for (int c = 0; c < channels; c++)
                        writer.Write(uncompressed.GetKeyFrameFloat(f, c));
                }
                break;

            case CompressedAnimationClip compressed:
                writer.Write((byte)ClipKind.Compressed);
                compressed.WriteTracks(writer);
                break;

            default:
                throw new NotSupportedException($"{clip.GetType().Name} cannot be written; bake it to an AnimationClip first.");
        }
    }

    /// <summary>
    /// Reads a clip written by <see cref="Write(BinaryWriter, AnimationClipBase)"/> onto
    /// <paramref name="skeleton"/>, which must be the skeleton it was written for.
    /// </summary>
    public static AnimationClipBase ReadClip(
        BinaryReader reader,
        Skeleton skeleton,
        IReadOnlyList<AnimationEvent>? events = null,
        IReadOnlyList<AnimationClipBase>? secondaryClips = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(skeleton);
        ReadHeader(reader, ClipMagic);

        int frameCount = reader.ReadInt32();
        float duration = reader.ReadSingle();
        bool additive = reader.ReadBoolean();
        RootMotion? rootMotion = ReadRootMotion(reader);
        SyncTrack syncTrack = ReadSyncTrack(reader);
        var kind = (ClipKind)reader.ReadByte();

        if (kind == ClipKind.Compressed)
            return new CompressedAnimationClip(reader, skeleton, frameCount, duration, additive, rootMotion, syncTrack, events, secondaryClips);

        int bones = skeleton.BoneCount;
        int channels = skeleton.FloatChannelCount;
        var frames = new Pose[frameCount];
        for (int f = 0; f < frameCount; f++)
        {
            var pose = new Pose(skeleton);
            for (int b = 0; b < bones; b++)
                pose.WriteLocal(b, ReadTransform(reader));
            for (int c = 0; c < channels; c++)
                pose.WriteFloat(c, reader.ReadSingle());
            pose.FinishWrite(additive ? PoseState.AdditivePose : PoseState.Pose);
            frames[f] = pose;
        }

        return new AnimationClip(skeleton, frames, duration, additive, rootMotion, syncTrack, events, secondaryClips);
    }

    // ---- humanoid ------------------------------------------------------------------------------

    /// <summary>Writes a baked muscle space clip (not its events).</summary>
    public static void Write(BinaryWriter writer, HumanoidClip clip)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(clip);

        writer.Write(HumanoidMagic);
        writer.Write(Version);
        writer.Write(clip.FrameCount);
        writer.Write(clip.Duration);
        writer.Write(clip.SourceScale);
        writer.Write(HumanTrait.MuscleCount);
        Write(writer, clip.RootMotion);
        Write(writer, clip.SyncTrack);

        writer.Write(clip.FloatChannelIds.Count);
        foreach (StringID id in clip.FloatChannelIds)
            WriteId(writer, id);

        var human = new HumanPose();
        for (int f = 0; f < clip.FrameCount; f++)
        {
            clip.GetHumanPose(new FrameTime(f, 0f), human);
            foreach (float muscle in human.Muscles)
                writer.Write(muscle);
            Write(writer, human.BodyPosition);
            Write(writer, human.BodyRotation);
            for (int g = 0; g < HumanPose.GoalCount; g++)
            {
                HumanGoalState goal = human.GetGoal((HumanGoal)g);
                Write(writer, goal.Transform);
                writer.Write(goal.PositionWeight);
                writer.Write(goal.RotationWeight);
                Write(writer, goal.Pole);
                writer.Write(goal.HasPole);
            }

            foreach (float value in clip.FrameFloats(f))
                writer.Write(value);
        }
    }

    public static HumanoidClip ReadHumanoidClip(BinaryReader reader, IReadOnlyList<AnimationEvent>? events = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        int version = ReadHeader(reader, HumanoidMagic);

        int frameCount = reader.ReadInt32();
        float duration = reader.ReadSingle();
        float sourceScale = reader.ReadSingle();
        int muscleCount = reader.ReadInt32();
        if (muscleCount != HumanTrait.MuscleCount)
            throw new InvalidDataException($"The clip has {muscleCount} muscles, this build has {HumanTrait.MuscleCount}.");

        RootMotion? rootMotion = ReadRootMotion(reader);
        SyncTrack syncTrack = ReadSyncTrack(reader);

        int channelCount = version >= 2 ? reader.ReadInt32() : 0;
        var channelIds = new StringID[channelCount];
        for (int c = 0; c < channelCount; c++)
            channelIds[c] = ReadId(reader);
        var channels = new float[frameCount * channelCount];

        var frames = new HumanPose[frameCount];
        for (int f = 0; f < frameCount; f++)
        {
            var human = new HumanPose();
            Span<float> muscles = human.Muscles;
            for (int m = 0; m < muscleCount; m++)
                muscles[m] = reader.ReadSingle();
            human.BodyPosition = ReadFloat3(reader);
            human.BodyRotation = ReadQuaternion(reader);
            for (int g = 0; g < HumanPose.GoalCount; g++)
            {
                human.SetGoal((HumanGoal)g, new HumanGoalState
                {
                    Transform = ReadTransform(reader),
                    PositionWeight = reader.ReadSingle(),
                    RotationWeight = reader.ReadSingle(),
                    Pole = ReadFloat3(reader),
                    HasPole = reader.ReadBoolean(),
                });
            }
            for (int c = 0; c < channelCount; c++)
                channels[f * channelCount + c] = reader.ReadSingle();
            frames[f] = human;
        }

        return HumanoidClip.FromFrames(frames, duration, sourceScale, rootMotion, syncTrack, events,
            channelCount > 0 ? channelIds : null, channelCount > 0 ? channels : null);
    }

    /// <summary>Writes a humanoid mapping: its bones, muscle range overrides and tuning knobs.</summary>
    public static void Write(BinaryWriter writer, HumanDescription description)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(description);

        writer.Write(DescriptionMagic);
        writer.Write(Version);

        var mapped = new List<HumanBodyBone>(description.MappedBones);
        writer.Write(mapped.Count);
        foreach (HumanBodyBone bone in mapped)
        {
            writer.Write((int)bone);
            writer.Write(description.GetSkeletonBoneIndex(bone));
        }

        writer.Write(HumanTrait.MuscleCount);
        for (int m = 0; m < HumanTrait.MuscleCount; m++)
        {
            (float min, float max) = description.GetMuscleRange(m);
            writer.Write(min);
            writer.Write(max);
        }

        writer.Write(description.ArmStretch);
        writer.Write(description.LegStretch);
        writer.Write(description.UpperArmTwist);
        writer.Write(description.LowerArmTwist);
        writer.Write(description.UpperLegTwist);
        writer.Write(description.LowerLegTwist);
        writer.Write(description.FeetSpacing);
    }

    public static HumanDescription ReadHumanDescription(BinaryReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ReadHeader(reader, DescriptionMagic);

        var description = new HumanDescription();
        int mapped = reader.ReadInt32();
        for (int i = 0; i < mapped; i++)
        {
            var bone = (HumanBodyBone)reader.ReadInt32();
            description.SetSkeletonBoneIndex(bone, reader.ReadInt32());
        }

        int muscleCount = reader.ReadInt32();
        for (int m = 0; m < muscleCount; m++)
        {
            float min = reader.ReadSingle();
            float max = reader.ReadSingle();
            if (m < HumanTrait.MuscleCount)
                description.SetMuscleRange(m, min, max);
        }

        description.ArmStretch = reader.ReadSingle();
        description.LegStretch = reader.ReadSingle();
        description.UpperArmTwist = reader.ReadSingle();
        description.LowerArmTwist = reader.ReadSingle();
        description.UpperLegTwist = reader.ReadSingle();
        description.LowerLegTwist = reader.ReadSingle();
        description.FeetSpacing = reader.ReadSingle();
        return description;
    }

    // ---- shared pieces -------------------------------------------------------------------------

    private static int ReadHeader(BinaryReader reader, uint expectedMagic)
    {
        uint magic = reader.ReadUInt32();
        if (magic != expectedMagic)
            throw new InvalidDataException("The stream does not hold the expected Motion data.");
        int version = reader.ReadInt32();
        if (version > Version)
            throw new InvalidDataException($"Motion data version {version} is newer than this build ({Version}).");
        return version;
    }

    private static void Write(BinaryWriter writer, RootMotion? rootMotion)
    {
        if (rootMotion is null)
        {
            writer.Write(0);
            return;
        }
        writer.Write(rootMotion.FrameCount);
        writer.Write(rootMotion.Duration);
        foreach (Transform3D frame in rootMotion.Frames)
            Write(writer, frame);
    }

    private static RootMotion? ReadRootMotion(BinaryReader reader)
    {
        int count = reader.ReadInt32();
        if (count <= 0)
            return null;

        float duration = reader.ReadSingle();
        var frames = new Transform3D[count];
        for (int i = 0; i < count; i++)
            frames[i] = ReadTransform(reader);
        return new RootMotion(frames, duration);
    }

    private static void Write(BinaryWriter writer, SyncTrack track)
    {
        writer.Write(track.EventCount);
        writer.Write(track.StartEventOffset);
        for (int i = 0; i < track.EventCount; i++)
        {
            SyncEvent sync = track.GetEvent(i - track.StartEventOffset);
            WriteId(writer, sync.Id);
            writer.Write(sync.StartTime);
            writer.Write(sync.Duration);
        }
    }

    private static SyncTrack ReadSyncTrack(BinaryReader reader)
    {
        int count = reader.ReadInt32();
        int startOffset = reader.ReadInt32();
        var markers = new SyncEvent[count];
        for (int i = 0; i < count; i++)
        {
            StringID id = ReadId(reader);
            float start = reader.ReadSingle();
            float duration = reader.ReadSingle();
            markers[i] = new SyncEvent(id, start, duration);
        }
        return new SyncTrack(markers, startOffset);
    }

    private static void WriteId(BinaryWriter writer, StringID id)
    {
        writer.Write(id.DebugName ?? string.Empty);
        writer.Write(id.ID);
    }

    private static StringID ReadId(BinaryReader reader)
    {
        string name = reader.ReadString();
        uint hash = reader.ReadUInt32();
        return name.Length > 0 ? new StringID(name) : new StringID(hash);
    }

    internal static void Write(BinaryWriter writer, in Transform3D transform)
    {
        Write(writer, transform.position);
        Write(writer, transform.rotation);
        Write(writer, transform.scale);
    }

    internal static Transform3D ReadTransform(BinaryReader reader)
    {
        Float3 position = ReadFloat3(reader);
        Quaternion rotation = ReadQuaternion(reader);
        Float3 scale = ReadFloat3(reader);
        return new Transform3D(position, rotation, scale);
    }

    internal static void Write(BinaryWriter writer, in Float3 value)
    {
        writer.Write(value.X);
        writer.Write(value.Y);
        writer.Write(value.Z);
    }

    internal static Float3 ReadFloat3(BinaryReader reader) => new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

    internal static void Write(BinaryWriter writer, in Quaternion value)
    {
        writer.Write(value.X);
        writer.Write(value.Y);
        writer.Write(value.Z);
        writer.Write(value.W);
    }

    internal static Quaternion ReadQuaternion(BinaryReader reader) => new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
}
