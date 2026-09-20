using System.Collections.Generic;
using System.IO;
using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion;

/// <summary>
/// A clip whose per bone tracks are quantized: channels that do not change across the clip are stored
/// once (static), and changing channels are range quantized per frame (16 bit translation/scale,
/// 48 bit smallest three rotation). Sampling decompresses and interpolates, matching
/// <see cref="AnimationClip"/>'s behaviour at a fraction of the memory. It carries the same root
/// motion, sync track, events and secondary clips, so the graph and animators play it directly.
/// </summary>
public sealed class CompressedAnimationClip : AnimationClipBase
{
    // A channel that never changes is one value; otherwise the values are kept per frame.
    private readonly struct FloatTrack
    {
        private readonly float _constant;
        private readonly float[]? _values;

        private FloatTrack(float constant, float[]? values)
        {
            _constant = constant;
            _values = values;
        }

        public int FrameDataLength => _values?.Length ?? 0;

        public static FloatTrack From(float[] values)
        {
            if (values.Length == 0)
                return new FloatTrack(0f, null);

            foreach (float v in values)
                if (MathF.Abs(v - values[0]) > VectorStaticEpsilon)
                    return new FloatTrack(0f, values);

            return new FloatTrack(values[0], null);
        }

        public float Sample(int frame, int nextFrame, float frac)
        {
            if (_values is null)
                return _constant;
            float value = _values[frame];
            return nextFrame < 0 ? value : value + (_values[nextFrame] - value) * frac;
        }

        public void Write(BinaryWriter writer)
        {
            writer.Write(_values is null);
            if (_values is null)
            {
                writer.Write(_constant);
                return;
            }
            writer.Write(_values.Length);
            foreach (float value in _values)
                writer.Write(value);
        }

        public static FloatTrack Read(BinaryReader reader)
        {
            if (reader.ReadBoolean())
                return new FloatTrack(reader.ReadSingle(), null);

            var values = new float[reader.ReadInt32()];
            for (int i = 0; i < values.Length; i++)
                values[i] = reader.ReadSingle();
            return new FloatTrack(0f, values);
        }
    }

    private sealed class BoneTrack
    {
        public bool RotationStatic, TranslationStatic, ScaleStatic;
        public Quaternion StaticRotation = Quaternion.Identity;
        public Float3 StaticTranslation, StaticScale = Float3.One;
        public Float3 TranslationMin, TranslationRange, ScaleMin, ScaleRange;
        public ushort[]? RotationData;     // 3 words per frame
        public ushort[]? TranslationData;  // 3 words per frame
        public ushort[]? ScaleData;        // 3 words per frame

        public void Write(BinaryWriter writer)
        {
            writer.Write(RotationStatic);
            writer.Write(TranslationStatic);
            writer.Write(ScaleStatic);
            MotionBinary.Write(writer, StaticRotation);
            MotionBinary.Write(writer, StaticTranslation);
            MotionBinary.Write(writer, StaticScale);
            MotionBinary.Write(writer, TranslationMin);
            MotionBinary.Write(writer, TranslationRange);
            MotionBinary.Write(writer, ScaleMin);
            MotionBinary.Write(writer, ScaleRange);
            WriteWords(writer, RotationData);
            WriteWords(writer, TranslationData);
            WriteWords(writer, ScaleData);
        }

        public static BoneTrack Read(BinaryReader reader) => new()
        {
            RotationStatic = reader.ReadBoolean(),
            TranslationStatic = reader.ReadBoolean(),
            ScaleStatic = reader.ReadBoolean(),
            StaticRotation = MotionBinary.ReadQuaternion(reader),
            StaticTranslation = MotionBinary.ReadFloat3(reader),
            StaticScale = MotionBinary.ReadFloat3(reader),
            TranslationMin = MotionBinary.ReadFloat3(reader),
            TranslationRange = MotionBinary.ReadFloat3(reader),
            ScaleMin = MotionBinary.ReadFloat3(reader),
            ScaleRange = MotionBinary.ReadFloat3(reader),
            RotationData = ReadWords(reader),
            TranslationData = ReadWords(reader),
            ScaleData = ReadWords(reader),
        };

        private static void WriteWords(BinaryWriter writer, ushort[]? data)
        {
            writer.Write(data?.Length ?? -1);
            if (data is null)
                return;
            foreach (ushort word in data)
                writer.Write(word);
        }

        private static ushort[]? ReadWords(BinaryReader reader)
        {
            int length = reader.ReadInt32();
            if (length < 0)
                return null;
            var data = new ushort[length];
            for (int i = 0; i < length; i++)
                data[i] = reader.ReadUInt16();
            return data;
        }
    }

    // Half of one step of the 15 bit rotation quantization: anything smaller is lost on encode anyway.
    private const float RotationStaticEpsilon = 0.70710678f / 32767f;
    private const float VectorStaticEpsilon = 1e-5f;

    private readonly BoneTrack[] _tracks;
    private readonly FloatTrack[] _floatTracks;
    private readonly int _boneCount;

    public CompressedAnimationClip(
        Skeleton skeleton,
        IReadOnlyList<Pose> keyFrames,
        float durationSeconds,
        bool isAdditive = false,
        RootMotion? rootMotion = null,
        SyncTrack? syncTrack = null,
        IReadOnlyList<AnimationEvent>? events = null,
        IReadOnlyList<AnimationClipBase>? secondaryClips = null)
        : base(skeleton, AnimationClip.CountFrames(keyFrames), durationSeconds, isAdditive, rootMotion, syncTrack, events, secondaryClips)
    {
        _boneCount = skeleton.BoneCount;
        _tracks = new BoneTrack[_boneCount];
        for (int f = 0; f < FrameCount; f++)
            if (keyFrames[f].BoneCount != _boneCount)
                throw new ArgumentException("Every key frame must match the skeleton bone count.", nameof(keyFrames));

        for (int b = 0; b < _boneCount; b++)
            _tracks[b] = CompressBone(keyFrames, b);

        _floatTracks = new FloatTrack[skeleton.FloatChannelCount];
        for (int c = 0; c < _floatTracks.Length; c++)
            _floatTracks[c] = CompressFloat(keyFrames, c);
    }

    /// <summary>Compresses an uncompressed clip, keeping its root motion, sync track, events and secondary clips.</summary>
    public CompressedAnimationClip(AnimationClip source)
        : base(source.Skeleton, source.FrameCount, source.Duration, source.IsAdditive, source.RootMotion, source.SyncTrack, source.Events, source.SecondaryClips)
    {
        _boneCount = source.Skeleton.BoneCount;
        _tracks = new BoneTrack[_boneCount];

        var frames = new Pose[source.FrameCount];
        for (int f = 0; f < frames.Length; f++)
        {
            frames[f] = new Pose(source.Skeleton);
            for (int b = 0; b < _boneCount; b++)
                frames[f].WriteLocal(b, source.GetKeyFrameTransform(f, b));
            frames[f].FinishWrite(PoseState.Pose);
        }

        for (int b = 0; b < _boneCount; b++)
            _tracks[b] = CompressBone(frames, b);

        _floatTracks = new FloatTrack[source.Skeleton.FloatChannelCount];
        for (int c = 0; c < _floatTracks.Length; c++)
        {
            var values = new float[source.FrameCount];
            for (int f = 0; f < values.Length; f++)
                values[f] = source.GetKeyFrameFloat(f, c);
            _floatTracks[c] = FloatTrack.From(values);
        }
    }

    /// <summary>Approximate per-frame data size in bytes (excludes static channels and object overhead).</summary>
    public long CompressedSizeBytes
    {
        get
        {
            long total = 0;
            foreach (BoneTrack t in _tracks)
            {
                if (t.RotationData is not null) total += t.RotationData.Length * sizeof(ushort);
                if (t.TranslationData is not null) total += t.TranslationData.Length * sizeof(ushort);
                if (t.ScaleData is not null) total += t.ScaleData.Length * sizeof(ushort);
            }
            foreach (FloatTrack t in _floatTracks)
                total += t.FrameDataLength * sizeof(float);
            return total;
        }
    }

    // Reads back a clip written by MotionBinary, keeping the quantized data as it was.
    internal CompressedAnimationClip(
        BinaryReader reader,
        Skeleton skeleton,
        int frameCount,
        float durationSeconds,
        bool isAdditive,
        RootMotion? rootMotion,
        SyncTrack? syncTrack,
        IReadOnlyList<AnimationEvent>? events,
        IReadOnlyList<AnimationClipBase>? secondaryClips)
        : base(skeleton, frameCount, durationSeconds, isAdditive, rootMotion, syncTrack, events, secondaryClips)
    {
        _boneCount = skeleton.BoneCount;
        _tracks = new BoneTrack[_boneCount];
        for (int b = 0; b < _boneCount; b++)
            _tracks[b] = BoneTrack.Read(reader);

        _floatTracks = new FloatTrack[skeleton.FloatChannelCount];
        for (int c = 0; c < _floatTracks.Length; c++)
            _floatTracks[c] = FloatTrack.Read(reader);
    }

    internal void WriteTracks(BinaryWriter writer)
    {
        foreach (BoneTrack track in _tracks)
            track.Write(writer);
        foreach (FloatTrack track in _floatTracks)
            track.Write(writer);
    }

    public override void GetPose(FrameTime frameTime, Pose result, SkeletonMapping? mapping)
    {
        ValidateTarget(result, mapping);

        int i0 = Math.Clamp(frameTime.FrameIndex, 0, FrameCount - 1);
        float frac = frameTime.Percentage;
        bool single = i0 >= FrameCount - 1 || !(frac > 0f);

        if (mapping is null)
        {
            for (int b = 0; b < _boneCount; b++)
                result.WriteLocal(b, single ? Decode(_tracks[b], i0) : Transform3D.Lerp(Decode(_tracks[b], i0), Decode(_tracks[b], i0 + 1), frac));
        }
        else
        {
            IReadOnlyList<Transform3D> reference = result.Skeleton.ParentSpaceReferencePose;
            for (int b = 0; b < result.BoneCount; b++)
            {
                int source = mapping.GetSourceBone(b);
                if (source == Skeleton.InvalidIndex)
                    result.WriteLocal(b, reference[b]);
                else
                    result.WriteLocal(b, single ? Decode(_tracks[source], i0) : Transform3D.Lerp(Decode(_tracks[source], i0), Decode(_tracks[source], i0 + 1), frac));
            }
        }

        for (int channel = 0; channel < result.FloatChannelCount; channel++)
        {
            int source = mapping is null ? (channel < _floatTracks.Length ? channel : Skeleton.InvalidIndex) : mapping.GetSourceFloatChannel(channel);
            result.WriteFloat(channel, source == Skeleton.InvalidIndex ? 0f : _floatTracks[source].Sample(i0, single ? -1 : i0 + 1, frac));
        }

        result.FinishWrite(IsAdditive ? PoseState.AdditivePose : PoseState.Pose);
    }

    private static FloatTrack CompressFloat(IReadOnlyList<Pose> keyFrames, int channel)
    {
        var values = new float[keyFrames.Count];
        for (int f = 0; f < values.Length; f++)
            values[f] = channel < keyFrames[f].FloatChannelCount ? keyFrames[f].GetFloat(channel) : 0f;
        return FloatTrack.From(values);
    }

    private Transform3D Decode(BoneTrack track, int frame)
    {
        Quaternion rotation = track.RotationStatic
            ? track.StaticRotation
            : Quantization.DecodeQuaternion(track.RotationData![frame * 3], track.RotationData![frame * 3 + 1], track.RotationData![frame * 3 + 2]);

        Float3 translation = track.TranslationStatic
            ? track.StaticTranslation
            : DecodeVector(track.TranslationData!, frame, track.TranslationMin, track.TranslationRange);

        Float3 scale = track.ScaleStatic
            ? track.StaticScale
            : DecodeVector(track.ScaleData!, frame, track.ScaleMin, track.ScaleRange);

        return new Transform3D(translation, rotation, scale);
    }

    private static Float3 DecodeVector(ushort[] data, int frame, Float3 min, Float3 range) => new(
        Quantization.DecodeFloat(data[frame * 3], min.X, range.X),
        Quantization.DecodeFloat(data[frame * 3 + 1], min.Y, range.Y),
        Quantization.DecodeFloat(data[frame * 3 + 2], min.Z, range.Z));

    private BoneTrack CompressBone(IReadOnlyList<Pose> frames, int bone)
    {
        var track = new BoneTrack();

        Quaternion rot0 = Quaternion.Normalize(frames[0].GetTransform(bone).rotation);
        bool rotStatic = true;
        for (int f = 1; f < FrameCount && rotStatic; f++)
            rotStatic = IsSameRotation(rot0, frames[f].GetTransform(bone).rotation);
        track.RotationStatic = rotStatic;
        if (rotStatic)
        {
            track.StaticRotation = rot0;
        }
        else
        {
            track.RotationData = new ushort[FrameCount * 3];
            for (int f = 0; f < FrameCount; f++)
            {
                Quantization.EncodeQuaternion(frames[f].GetTransform(bone).rotation, out ushort m0, out ushort m1, out ushort m2);
                track.RotationData[f * 3] = m0;
                track.RotationData[f * 3 + 1] = m1;
                track.RotationData[f * 3 + 2] = m2;
            }
        }

        CompressVectorChannel(frames, bone, isScale: false, track);
        CompressVectorChannel(frames, bone, isScale: true, track);
        return track;
    }

    private void CompressVectorChannel(IReadOnlyList<Pose> frames, int bone, bool isScale, BoneTrack track)
    {
        Float3 first = Channel(frames[0], bone, isScale);
        Float3 min = first, max = first;
        bool isStatic = true;
        for (int f = 1; f < FrameCount; f++)
        {
            Float3 v = Channel(frames[f], bone, isScale);
            min = new Float3(MathF.Min(min.X, v.X), MathF.Min(min.Y, v.Y), MathF.Min(min.Z, v.Z));
            max = new Float3(MathF.Max(max.X, v.X), MathF.Max(max.Y, v.Y), MathF.Max(max.Z, v.Z));
            if (Float3.Distance(first, v) > VectorStaticEpsilon)
                isStatic = false;
        }

        if (isStatic)
        {
            if (isScale) { track.ScaleStatic = true; track.StaticScale = first; }
            else { track.TranslationStatic = true; track.StaticTranslation = first; }
            return;
        }

        var range = new Float3(MathF.Max(max.X - min.X, 0f), MathF.Max(max.Y - min.Y, 0f), MathF.Max(max.Z - min.Z, 0f));
        var data = new ushort[FrameCount * 3];
        for (int f = 0; f < FrameCount; f++)
        {
            Float3 v = Channel(frames[f], bone, isScale);
            data[f * 3] = Quantization.EncodeFloat(v.X, min.X, range.X);
            data[f * 3 + 1] = Quantization.EncodeFloat(v.Y, min.Y, range.Y);
            data[f * 3 + 2] = Quantization.EncodeFloat(v.Z, min.Z, range.Z);
        }

        if (isScale) { track.ScaleData = data; track.ScaleMin = min; track.ScaleRange = range; }
        else { track.TranslationData = data; track.TranslationMin = min; track.TranslationRange = range; }
    }

    // Compares component wise after aligning hemispheres, at the precision the rotation codec keeps.
    private static bool IsSameRotation(Quaternion reference, Quaternion other)
    {
        Quaternion q = Quaternion.Normalize(other);
        float sign = Quaternion.Dot(reference, q) < 0f ? -1f : 1f;
        return MathF.Abs(reference.X - q.X * sign) <= RotationStaticEpsilon
            && MathF.Abs(reference.Y - q.Y * sign) <= RotationStaticEpsilon
            && MathF.Abs(reference.Z - q.Z * sign) <= RotationStaticEpsilon
            && MathF.Abs(reference.W - q.W * sign) <= RotationStaticEpsilon;
    }

    private static Float3 Channel(Pose pose, int bone, bool isScale)
    {
        Transform3D t = pose.GetTransform(bone);
        return isScale ? t.scale : t.position;
    }
}
