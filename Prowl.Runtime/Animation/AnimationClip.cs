// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Echo;
using Prowl.Motion;
using Prowl.Vector;
using Prowl.Vector.Spatial;

using MotionAvatar = Prowl.Motion.Avatar;
using MotionClip = Prowl.Motion.AnimationClipBase;
using MotionSkeleton = Prowl.Motion.Skeleton;

namespace Prowl.Runtime;

/// <summary>What a clip stores, which decides which rigs can play it.</summary>
public enum AnimationClipKind
{
    /// <summary>Bone transforms for one skeleton. Plays on that rig, or on one matching it by bone name.</summary>
    Skeletal,
    /// <summary>Muscle space, baked through a humanoid avatar. Plays on any humanoid rig.</summary>
    Humanoid,
    /// <summary>Property curve, object reference, and string tracks driving any GameObject, Transform, or Component in a hierarchy.</summary>
    Property,
}

/// <summary>How an <see cref="AnimationClip"/> repeats or stops when playback reaches the end.</summary>
public enum AnimationClipWrapMode
{
    /// <summary>Plays once and stops at the final keyframe.</summary>
    Once,
    /// <summary>Repeats from the beginning when it reaches the end.</summary>
    Loop,
    /// <summary>Alternates forward and backward continuously.</summary>
    PingPong,
    /// <summary>Keeps running past the end, clamping the evaluated time to the clip's duration.</summary>
    ClampForever,
}

/// <summary>How a property curve track combines with the target property's value.</summary>
public enum AnimationTrackBlendMode
{
    /// <summary>Replaces the target property value with the evaluated curve value.</summary>
    Override = 0,
    /// <summary>Adds the evaluated curve value as a delta offset onto the target property's pre-animated baseline value.</summary>
    Additive = 1,
}

/// <summary>What happens to animated properties when clip playback stops or finishes (Sequencer-style completion state).</summary>
public enum AnimationClipCompletionMode
{
    /// <summary>Animated properties remain at their last evaluated value when playback stops.</summary>
    KeepState = 0,
    /// <summary>Animated properties automatically revert to their pre-animated baseline values when playback finishes or stops.</summary>
    RestoreState = 1,
}

/// <summary>
/// A curve track driving a single scalar property (or one channel of a vector, color, or quaternion)
/// on a GameObject, Transform, or Component relative to the animated root.
/// </summary>
[Serializable]
[FormerlySerializedAs("Prowl.Runtime.SceneCurveTrack")]
public sealed class AnimationCurveTrack
{
    /// <summary>
    /// Path from the animated root to the target GameObject (e.g. <c>""</c> for root, <c>"Arm/Hand"</c> for a descendant).
    /// </summary>
    public string RelativePath = string.Empty;

    /// <summary>
    /// Target type name: <c>"GameObject"</c>, <c>"Transform"</c>, or the component's type name (short name or full name).
    /// </summary>
    public string ComponentType = string.Empty;

    /// <summary>
    /// Dot-separated member path from the target to the scalar property or sub-channel (e.g. <c>"LocalPosition.X"</c>, <c>"Intensity"</c>, <c>"Color.R"</c>).
    /// </summary>
    public string PropertyPath = string.Empty;

    /// <summary>The keyframed curve evaluated in seconds from the start of the clip.</summary>
    public AnimationCurve Curve = new();

    /// <summary>Whether this track overrides the target property or adds its evaluated value as a delta onto the pre-animated baseline.</summary>
    public AnimationTrackBlendMode BlendMode = AnimationTrackBlendMode.Override;

    /// <summary>When true, this track is skipped during evaluation.</summary>
    public bool Muted;

    /// <summary>When true, keyframes on this track are locked against editing in the Animation Window.</summary>
    public bool Locked;

    /// <summary>Editor/runtime solo flag: when any track on a clip has Solo enabled, only soloed tracks evaluate.</summary>
    [NonSerialized] public bool Solo;

    public AnimationCurveTrack() { }

    public AnimationCurveTrack(string relativePath, string componentType, string propertyPath, AnimationCurve? curve = null)
    {
        RelativePath = relativePath ?? string.Empty;
        ComponentType = componentType ?? string.Empty;
        PropertyPath = propertyPath ?? string.Empty;
        Curve = curve ?? new AnimationCurve();
    }

    public AnimationCurveTrack Clone()
    {
        var copy = new AnimationCurveTrack(RelativePath, ComponentType, PropertyPath)
        {
            BlendMode = BlendMode,
            Muted = Muted,
            Locked = Locked,
        };
        for (int i = 0; i < Curve.Count; i++)
            copy.Curve.AddKey(Curve[i]);
        return copy;
    }
}

/// <summary>A discrete keyframe holding an <see cref="EngineObject"/> reference (such as a Sprite or Material) at a given time.</summary>
[Serializable]
[FormerlySerializedAs("Prowl.Runtime.SceneObjectKeyframe")]
public sealed class AnimationObjectKeyframe
{
    public float Time;
    public EngineObject? Value;

    public AnimationObjectKeyframe() { }

    public AnimationObjectKeyframe(float time, EngineObject? value)
    {
        Time = time;
        Value = value;
    }

    public AnimationObjectKeyframe Clone() => new(Time, Value);
}

/// <summary>A discrete keyframe holding a string value at a given time.</summary>
[Serializable]
[FormerlySerializedAs("Prowl.Runtime.SceneStringKeyframe")]
public sealed class AnimationStringKeyframe
{
    public float Time;
    public string Value = string.Empty;

    public AnimationStringKeyframe() { }

    public AnimationStringKeyframe(float time, string value)
    {
        Time = time;
        Value = value ?? string.Empty;
    }

    public AnimationStringKeyframe Clone() => new(Time, Value);
}

/// <summary>
/// A discrete reference track driving an <see cref="EngineObject"/> property or field (such as <see cref="SpriteRenderer.Sprite"/> or <see cref="MeshRenderer.Material"/>).
/// Evaluates using step (hold-previous) semantics.
/// </summary>
[Serializable]
[FormerlySerializedAs("Prowl.Runtime.SceneObjectTrack")]
public sealed class AnimationObjectTrack
{
    public string RelativePath = string.Empty;
    public string ComponentType = string.Empty;
    public string PropertyPath = string.Empty;
    public List<AnimationObjectKeyframe> Keyframes = [];
    public bool Muted;
    public bool Locked;
    [NonSerialized] public bool Solo;

    public AnimationObjectTrack() { }

    public AnimationObjectTrack(string relativePath, string componentType, string propertyPath)
    {
        RelativePath = relativePath ?? string.Empty;
        ComponentType = componentType ?? string.Empty;
        PropertyPath = propertyPath ?? string.Empty;
    }

    /// <summary>Inserts or updates a keyframe at <paramref name="time"/>, keeping the list sorted by time.</summary>
    public int SetKey(float time, EngineObject? value, float tolerance = 0.0005f)
    {
        for (int i = 0; i < Keyframes.Count; i++)
        {
            if (MathF.Abs(Keyframes[i].Time - time) <= tolerance)
            {
                Keyframes[i].Time = time;
                Keyframes[i].Value = value;
                return i;
            }
            if (Keyframes[i].Time > time)
            {
                Keyframes.Insert(i, new AnimationObjectKeyframe(time, value));
                return i;
            }
        }
        Keyframes.Add(new AnimationObjectKeyframe(time, value));
        return Keyframes.Count - 1;
    }

    public void SortKeyframes() => Keyframes.Sort(static (a, b) => a.Time.CompareTo(b.Time));

    /// <summary>Evaluates the discrete object value at <paramref name="time"/> using step semantics.</summary>
    public EngineObject? Evaluate(float time)
    {
        if (Keyframes.Count == 0) return null;
        EngineObject? result = Keyframes[0].Value;
        for (int i = 0; i < Keyframes.Count; i++)
        {
            if (Keyframes[i].Time <= time + 1e-5f)
                result = Keyframes[i].Value;
            else
                break;
        }
        return result;
    }

    public AnimationObjectTrack Clone()
    {
        var copy = new AnimationObjectTrack(RelativePath, ComponentType, PropertyPath)
        {
            Muted = Muted,
            Locked = Locked,
        };
        foreach (AnimationObjectKeyframe kf in Keyframes)
            copy.Keyframes.Add(kf.Clone());
        return copy;
    }
}

/// <summary>
/// A discrete track driving a <see cref="string"/> property or field (such as UI text).
/// Evaluates using step (hold-previous) semantics.
/// </summary>
[Serializable]
[FormerlySerializedAs("Prowl.Runtime.SceneStringTrack")]
public sealed class AnimationStringTrack
{
    public string RelativePath = string.Empty;
    public string ComponentType = string.Empty;
    public string PropertyPath = string.Empty;
    public List<AnimationStringKeyframe> Keyframes = [];
    public bool Muted;
    public bool Locked;
    [NonSerialized] public bool Solo;

    public AnimationStringTrack() { }

    public AnimationStringTrack(string relativePath, string componentType, string propertyPath)
    {
        RelativePath = relativePath ?? string.Empty;
        ComponentType = componentType ?? string.Empty;
        PropertyPath = propertyPath ?? string.Empty;
    }

    /// <summary>Inserts or updates a keyframe at <paramref name="time"/>, keeping the list sorted by time.</summary>
    public int SetKey(float time, string value, float tolerance = 0.0005f)
    {
        for (int i = 0; i < Keyframes.Count; i++)
        {
            if (MathF.Abs(Keyframes[i].Time - time) <= tolerance)
            {
                Keyframes[i].Time = time;
                Keyframes[i].Value = value ?? string.Empty;
                return i;
            }
            if (Keyframes[i].Time > time)
            {
                Keyframes.Insert(i, new AnimationStringKeyframe(time, value));
                return i;
            }
        }
        Keyframes.Add(new AnimationStringKeyframe(time, value));
        return Keyframes.Count - 1;
    }

    public void SortKeyframes() => Keyframes.Sort(static (a, b) => a.Time.CompareTo(b.Time));

    /// <summary>Evaluates the discrete string value at <paramref name="time"/> using step semantics.</summary>
    public string Evaluate(float time)
    {
        if (Keyframes.Count == 0) return string.Empty;
        string result = Keyframes[0].Value;
        for (int i = 0; i < Keyframes.Count; i++)
        {
            if (Keyframes[i].Time <= time + 1e-5f)
                result = Keyframes[i].Value;
            else
                break;
        }
        return result;
    }

    public AnimationStringTrack Clone()
    {
        var copy = new AnimationStringTrack(RelativePath, ComponentType, PropertyPath)
        {
            Muted = Muted,
            Locked = Locked,
        };
        foreach (AnimationStringKeyframe kf in Keyframes)
            copy.Keyframes.Add(kf.Clone());
        return copy;
    }
}

/// <summary>
/// A marker on an <see cref="AnimationClip"/> that invokes methods on components of the target GameObject.
/// Can be either an instant event at a single <see cref="Time"/> (<see cref="EndTime"/> &lt;= <see cref="Time"/>),
/// or a ranged event with a start (<see cref="Time"/>) and end (<see cref="EndTime"/> &gt; <see cref="Time"/>)
/// that invokes Enter (<see cref="FunctionName"/>), optional <see cref="StayFunctionName"/> (passing normalized 0..1 progress),
/// and <see cref="ExitFunctionName"/> callbacks.
/// </summary>
[Serializable]
[FormerlySerializedAs("Prowl.Runtime.SceneAnimationEvent")]
public sealed class AnimationClipEvent
{
    /// <summary>Start time in seconds from the beginning of the clip.</summary>
    public float Time;

    /// <summary>
    /// End time in seconds from the beginning of the clip.
    /// When less than or equal to <see cref="Time"/>, the event is a single-instant event at <see cref="Time"/>.
    /// When greater than <see cref="Time"/>, the event is a ranged (start/end) event spanning [<see cref="Time"/>, <see cref="EndTime"/>].
    /// </summary>
    public float EndTime;

    /// <summary>Duration of the ranged event in seconds (<c>Max(0, EndTime - Time)</c>).</summary>
    public float Duration
    {
        get => MathF.Max(0f, EndTime - Time);
        set => EndTime = value > 1e-5f ? Time + value : Time;
    }

    /// <summary>True when this event spans a time range (<see cref="EndTime"/> &gt; <see cref="Time"/>).</summary>
    public bool IsRange => EndTime > Time + 1e-5f;

    /// <summary>Path from the animated root to the target GameObject (empty for the root itself).</summary>
    public string RelativePath = string.Empty;

    /// <summary>
    /// Name of the method to invoke on matching components when an instant event fires
    /// or when playback enters a ranged event's [Time, EndTime] window.
    /// </summary>
    public string FunctionName = string.Empty;

    /// <summary>
    /// Optional method invoked each update while playback remains inside a ranged event's [Time, EndTime] window.
    /// If the target method takes a single <see cref="float"/> parameter, the normalized progress (0..1) across the range is passed.
    /// </summary>
    public string StayFunctionName = string.Empty;

    /// <summary>
    /// Optional method invoked when playback exits a ranged event's [Time, EndTime] window.
    /// </summary>
    public string ExitFunctionName = string.Empty;

    /// <summary>Which parameter to pass to <see cref="FunctionName"/> (and <see cref="ExitFunctionName"/> if it accepts an argument).</summary>
    public ProwlActionArgType ArgType = ProwlActionArgType.None;

    public bool BoolParam;
    public int IntParam;
    public float FloatParam;
    public string StringParam = string.Empty;
    public EngineObject? ObjectParam;

    public AnimationClipEvent Clone() => (AnimationClipEvent)MemberwiseClone();
}

/// <summary>
/// A unified animation clip: holds an optional encoded Motion skeletal/humanoid clip, the rig it was authored on,
/// AND arbitrary property curve tracks, discrete object/string tracks, and method events ("Animate Anything").
/// </summary>
[CreateAssetMenu("Animation/Animation Clip", Extension = ".animclip", Order = 1140)]
public sealed class AnimationClip : Asset, ISerializable
{
    /// <summary>The rig the clip was authored on. A humanoid clip uses it only to preview; property-only clips need no avatar.</summary>
    public Avatar? Avatar;

    /// <summary>Whether playback wraps or holds the final pose.</summary>
    public bool Loop = true;

    /// <summary>Editor timeline sample rate in frames per second.</summary>
    public float FrameRate = 60f;

    /// <summary>How property track playback behaves when reaching the end of the clip.</summary>
    public AnimationClipWrapMode WrapMode = AnimationClipWrapMode.Loop;

    /// <summary>Whether animated properties keep their final evaluated state or revert to their pre-animated baseline when playback stops or finishes.</summary>
    public AnimationClipCompletionMode CompletionMode = AnimationClipCompletionMode.KeepState;

    /// <summary>
    /// Effective wrap mode reconciling both <see cref="WrapMode"/> and <see cref="Loop"/>
    /// so code that sets either property observes consistent playback behavior.
    /// </summary>
    public AnimationClipWrapMode EffectiveWrapMode
        => !Loop && WrapMode == AnimationClipWrapMode.Loop ? AnimationClipWrapMode.Once : WrapMode;

    /// <summary>True when <see cref="EffectiveWrapMode"/> repeats continuously (<see cref="AnimationClipWrapMode.Loop"/> or <see cref="AnimationClipWrapMode.PingPong"/>).</summary>
    public bool EffectiveLoop
        => EffectiveWrapMode is AnimationClipWrapMode.Loop or AnimationClipWrapMode.PingPong;

    /// <summary>True when at least one track on this clip has its Solo flag enabled.</summary>
    public bool HasSoloTrack
    {
        get
        {
            for (int i = 0; i < CurveTracks.Count; i++)
                if (CurveTracks[i].Solo) return true;
            for (int i = 0; i < ObjectTracks.Count; i++)
                if (ObjectTracks[i].Solo) return true;
            for (int i = 0; i < StringTracks.Count; i++)
                if (StringTracks[i].Solo) return true;
            return false;
        }
    }

    /// <summary>The clip's name in the file it was imported from, which its import settings are keyed on.</summary>
    public string SourceName = string.Empty;

    /// <summary>The clip's Motion markers, handed to Motion when the clip is decoded.</summary>
    public List<ClipEvent> Events = [];

    /// <summary>Scalar curve tracks (floats, ints, bools, enums, and vector/color/quaternion channels) evaluated alongside or instead of skeletal poses.</summary>
    public List<AnimationCurveTrack> CurveTracks = [];

    /// <summary>Discrete object reference tracks (Sprites, Materials, Meshes, etc.).</summary>
    public List<AnimationObjectTrack> ObjectTracks = [];

    /// <summary>Discrete string tracks.</summary>
    public List<AnimationStringTrack> StringTracks = [];

    /// <summary>Component method invocation events fired when playback crosses their timestamp.</summary>
    public List<AnimationClipEvent> PropertyEvents = [];

    /// <summary>Where in the take the clip starts, in seconds, as the import cut it.</summary>
    public float TakeStart;

    private AnimationClipKind _kind = AnimationClipKind.Property;
    private float _duration;
    private int _frameCount;
    private byte[]? _payload;

    [NonSerialized] private MotionClip? _skeletal;
    [NonSerialized] private HumanoidClip? _humanoid;
    [NonSerialized] private Dictionary<MotionAvatar, MotionClip>? _bound;
    [NonSerialized] private Dictionary<MotionSkeleton, MotionClip>? _syntheticMotion;
    [NonSerialized] private bool _decodeFailed;
    [NonSerialized] private int _version;

    public AnimationClip() : base("Animation") { }

    /// <summary>Incremented whenever tracks, keys, or settings are modified so bound animators know to rebind.</summary>
    public int Version => unchecked(_version * 31 + ContentVersion);

    /// <summary>True when this clip carries encoded skeletal or humanoid Motion pose data.</summary>
    public bool HasMotionPayload { get { EnsureLoaded(); return _payload is { Length: > 0 }; } }

    /// <summary>True when this clip carries any property curve, object, string, or method event tracks.</summary>
    public bool HasPropertyTracks
    {
        get
        {
            EnsureLoaded();
            return CurveTracks.Count > 0 || ObjectTracks.Count > 0 || StringTracks.Count > 0 || PropertyEvents.Count > 0;
        }
    }

    public AnimationClipKind Kind
    {
        get
        {
            EnsureLoaded();
            if (_payload == null || _payload.Length == 0)
                return AnimationClipKind.Property;
            return _kind;
        }
    }

    /// <summary>
    /// Clip length in seconds: the maximum of the imported Motion duration and the latest keyframe or event across all property tracks.
    /// </summary>
    public float Duration
    {
        get
        {
            EnsureLoaded();
            return MathF.Max(_duration, CalculatePropertyDuration());
        }
    }

    public int FrameCount
    {
        get
        {
            EnsureLoaded();
            if (_frameCount > 0) return _frameCount;
            float dur = Duration;
            float fps = FrameRate > 0f ? FrameRate : 60f;
            return dur > 0f ? Math.Max(2, (int)MathF.Round(dur * fps) + 1) : 0;
        }
    }

    private float CalculatePropertyDuration()
    {
        float max = 0f;
        for (int i = 0; i < CurveTracks.Count; i++)
        {
            AnimationCurve c = CurveTracks[i].Curve;
            if (c.Count > 0)
                max = MathF.Max(max, c[c.Count - 1].Time);
        }
        for (int i = 0; i < ObjectTracks.Count; i++)
        {
            List<AnimationObjectKeyframe> kfs = ObjectTracks[i].Keyframes;
            for (int j = 0; j < kfs.Count; j++)
                max = MathF.Max(max, kfs[j].Time);
        }
        for (int i = 0; i < StringTracks.Count; i++)
        {
            List<AnimationStringKeyframe> kfs = StringTracks[i].Keyframes;
            for (int j = 0; j < kfs.Count; j++)
                max = MathF.Max(max, kfs[j].Time);
        }
        for (int i = 0; i < PropertyEvents.Count; i++)
            max = MathF.Max(max, MathF.Max(PropertyEvents[i].Time, PropertyEvents[i].EndTime));
        for (int i = 0; i < Events.Count; i++)
            max = MathF.Max(max, Events[i].Time + MathF.Max(0f, Events[i].Length));
        return max;
    }

    /// <summary>Marks the clip's property tracks or settings changed so bound animators rebind on their next tick.</summary>
    public void Invalidate()
    {
        EnsureLoaded();
        _version++;
        _syntheticMotion = null;
    }

    /// <summary>
    /// The clip Motion plays, decoded on first use and bound to <paramref name="target"/> when humanoid.
    /// For a property-only clip played inside an <see cref="AnimationGraph"/> or <see cref="Animator"/>,
    /// pass <paramref name="fallbackSkeleton"/> so a reference-pose carrier clip with the clip's duration and events is synthesized.
    /// </summary>
    public MotionClip? GetClip(MotionAvatar? target, MotionSkeleton? fallbackSkeleton = null)
    {
        EnsureLoaded();
        if (_payload != null && _payload.Length > 0 && !_decodeFailed)
        {
            MotionClip? decoded = _kind == AnimationClipKind.Humanoid ? BindHumanoid(target) : DecodeSkeletal();
            if (decoded != null)
                return decoded;
        }

        // Synthesize a lightweight carrier MotionClip when playing a property-only clip in a Motion Animator or AnimationGraph
        MotionSkeleton? skeleton = fallbackSkeleton ?? target?.Skeleton ?? (Avatar.IsValid() ? Avatar!.Skeleton : null);
        if (skeleton != null && (HasPropertyTracks || Events.Count > 0 || Duration > 0f))
            return GetOrCreateSyntheticMotionClip(skeleton);

        return null;
    }

    private MotionClip GetOrCreateSyntheticMotionClip(MotionSkeleton skeleton)
    {
        _syntheticMotion ??= [];
        if (_syntheticMotion.TryGetValue(skeleton, out MotionClip? existing))
            return existing;

        float dur = MathF.Max(Duration, 1f / MathF.Max(1f, FrameRate));
        var refPose = new Pose(skeleton);
        refPose.SetToReferencePose();
        var synthesized = new Motion.AnimationClip(skeleton, [refPose, refPose], dur, events: MotionEvents(dur));
        _syntheticMotion[skeleton] = synthesized;
        return synthesized;
    }

    private MotionClip? DecodeSkeletal()
    {
        if (_skeletal != null)
            return _skeletal;

        Avatar? avatar = Avatar;
        if (avatar.IsNotValid() || avatar.Skeleton == null)
            return null;

        MotionSkeleton skeleton = avatar.Skeleton;
        _skeletal = Decode(reader => MotionBinary.ReadClip(reader, skeleton, MotionEvents(Duration)));
        return _skeletal;
    }

    private MotionClip? BindHumanoid(MotionAvatar? target)
    {
        _humanoid ??= Decode(reader => MotionBinary.ReadHumanoidClip(reader, MotionEvents(Duration)));
        if (_humanoid == null || target is not { IsHuman: true })
            return null;

        _bound ??= [];
        if (_bound.TryGetValue(target, out MotionClip? bound))
            return bound;

        bound = _humanoid.Bind(target);
        _bound[target] = bound;
        return bound;
    }

    private AnimationEvent[] MotionEvents(float clipDuration)
    {
        float dur = clipDuration > 0f ? clipDuration : _duration;
        var events = new AnimationEvent[Events.Count];
        for (int i = 0; i < events.Length; i++) events[i] = Events[i].ToMotion(dur);
        return events;
    }

    /// <summary>Drops the decoded clip, so the next use picks up the current events.</summary>
    public void EventsChanged()
    {
        _skeletal = null;
        _humanoid = null;
        _bound = null;
        _syntheticMotion = null;
        _version++;
    }

    private T? Decode<T>(Func<BinaryReader, T> read) where T : class
    {
        try
        {
            using var stream = new MemoryStream(_payload!);
            using var reader = new BinaryReader(stream);
            return read(reader);
        }
        catch (Exception ex)
        {
            _decodeFailed = true;
            Debug.LogError($"[AnimationClip] '{Name}' could not be decoded: {ex.Message}");
            return null;
        }
    }

    /// <summary>Builds an asset around a clip authored for one skeleton.</summary>
    public static AnimationClip FromSkeletal(MotionClip clip, Avatar? avatar, string? name = null, IEnumerable<ClipEvent>? events = null)
    {
        ArgumentNullException.ThrowIfNull(clip);
        AnimationClip asset = Create(AnimationClipKind.Skeletal, clip.Duration, clip.FrameCount, avatar, name, events, writer => MotionBinary.Write(writer, clip));
        if (events == null) asset._skeletal = clip;
        return asset;
    }

    /// <summary>Builds an asset around a clip baked into muscle space.</summary>
    public static AnimationClip FromHumanoid(HumanoidClip clip, Avatar? avatar, string? name = null, IEnumerable<ClipEvent>? events = null)
    {
        ArgumentNullException.ThrowIfNull(clip);
        AnimationClip asset = Create(AnimationClipKind.Humanoid, clip.Duration, clip.FrameCount, avatar, name, events, writer => MotionBinary.Write(writer, clip));
        if (events == null) asset._humanoid = clip;
        return asset;
    }

    private static AnimationClip Create(AnimationClipKind kind, float duration, int frames, Avatar? avatar, string? name,
        IEnumerable<ClipEvent>? events, Action<BinaryWriter> write)
    {
        float fps = duration > 1e-5f && frames > 1 ? MathF.Round((frames - 1) / duration) : 60f;
        var asset = new AnimationClip
        {
            Name = name ?? "Animation",
            Avatar = avatar,
            FrameRate = Math.Clamp(fps, 1f, 240f),
            _kind = kind,
            _duration = duration,
            _frameCount = frames,
        };
        if (events != null) asset.Events.AddRange(events);

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            write(writer);
        asset._payload = stream.ToArray();
        return asset;
    }

    // ================================================================
    //  Property Track Manipulation & Unpacking
    // ================================================================

    /// <summary>Finds an existing curve track for the given target and property path, or null if none exists.</summary>
    public AnimationCurveTrack? FindCurveTrack(string relativePath, string componentType, string propertyPath)
    {
        EnsureLoaded();
        relativePath ??= string.Empty;
        componentType ??= string.Empty;
        propertyPath ??= string.Empty;
        for (int i = 0; i < CurveTracks.Count; i++)
        {
            AnimationCurveTrack t = CurveTracks[i];
            if (t.RelativePath == relativePath && t.ComponentType == componentType && t.PropertyPath == propertyPath)
                return t;
        }
        return null;
    }

    /// <summary>Finds or creates a curve track for the given target and property path.</summary>
    public AnimationCurveTrack GetOrCreateCurveTrack(string relativePath, string componentType, string propertyPath)
    {
        if (FindCurveTrack(relativePath, componentType, propertyPath) is { } existing)
            return existing;

        var track = new AnimationCurveTrack(relativePath, componentType, propertyPath);
        CurveTracks.Add(track);
        Invalidate();
        return track;
    }

    /// <summary>Adds or updates a keyframe on the curve track matching the target and property path, preserving custom tangents on existing keys.</summary>
    public AnimationCurveTrack SetCurveKey(string relativePath, string componentType, string propertyPath, float time, float value, float tolerance = 0.0005f)
    {
        AnimationCurveTrack track = GetOrCreateCurveTrack(relativePath, componentType, propertyPath);
        AnimationCurve curve = track.Curve;
        for (int i = 0; i < curve.Count; i++)
        {
            if (MathF.Abs(curve[i].Time - time) <= tolerance)
            {
                Keyframe old = curve[i];
                curve.MoveKey(i, new Keyframe(time, value, old.InTangent, old.OutTangent, old.Interpolation));
                Invalidate();
                return track;
            }
        }

        int inserted = curve.AddKey(new Keyframe(time, value));
        AutoSmoothSingleKey(curve, inserted);
        Invalidate();
        return track;
    }

    /// <summary>Computes a smooth Catmull-Rom/secant tangent for a single keyframe index without overwriting tangents on neighboring keys.</summary>
    public static void AutoSmoothSingleKey(AnimationCurve curve, int index)
    {
        if (index < 0 || index >= curve.Count || curve.Count < 2) return;
        Keyframe cur = curve[index];
        if (cur.Interpolation == CurveInterpolation.Step) return;

        float slope;
        if (index > 0 && index < curve.Count - 1)
        {
            Keyframe prev = curve[index - 1];
            Keyframe next = curve[index + 1];
            float dt = next.Time - prev.Time;
            slope = dt > 1e-6f ? (next.Value - prev.Value) / dt : 0f;
        }
        else if (index > 0)
        {
            Keyframe prev = curve[index - 1];
            float dt = cur.Time - prev.Time;
            slope = dt > 1e-6f ? (cur.Value - prev.Value) / dt : 0f;
        }
        else
        {
            Keyframe next = curve[index + 1];
            float dt = next.Time - cur.Time;
            slope = dt > 1e-6f ? (next.Value - cur.Value) / dt : 0f;
        }

        curve.MoveKey(index, new Keyframe(cur.Time, cur.Value, slope, slope, cur.Interpolation));
    }

    /// <summary>Finds an existing object reference track for the given target and property path, or null.</summary>
    public AnimationObjectTrack? FindObjectTrack(string relativePath, string componentType, string propertyPath)
    {
        EnsureLoaded();
        relativePath ??= string.Empty;
        componentType ??= string.Empty;
        propertyPath ??= string.Empty;
        for (int i = 0; i < ObjectTracks.Count; i++)
        {
            AnimationObjectTrack t = ObjectTracks[i];
            if (t.RelativePath == relativePath && t.ComponentType == componentType && t.PropertyPath == propertyPath)
                return t;
        }
        return null;
    }

    /// <summary>Finds or creates an object reference track for the given target and property path.</summary>
    public AnimationObjectTrack GetOrCreateObjectTrack(string relativePath, string componentType, string propertyPath)
    {
        if (FindObjectTrack(relativePath, componentType, propertyPath) is { } existing)
            return existing;

        var track = new AnimationObjectTrack(relativePath, componentType, propertyPath);
        ObjectTracks.Add(track);
        Invalidate();
        return track;
    }

    /// <summary>Adds or updates a discrete object reference keyframe on the matching track.</summary>
    public AnimationObjectTrack SetObjectKey(string relativePath, string componentType, string propertyPath, float time, EngineObject? value, float tolerance = 0.0005f)
    {
        AnimationObjectTrack track = GetOrCreateObjectTrack(relativePath, componentType, propertyPath);
        track.SetKey(time, value, tolerance);
        Invalidate();
        return track;
    }

    /// <summary>Finds an existing string track for the given target and property path, or null.</summary>
    public AnimationStringTrack? FindStringTrack(string relativePath, string componentType, string propertyPath)
    {
        EnsureLoaded();
        relativePath ??= string.Empty;
        componentType ??= string.Empty;
        propertyPath ??= string.Empty;
        for (int i = 0; i < StringTracks.Count; i++)
        {
            AnimationStringTrack t = StringTracks[i];
            if (t.RelativePath == relativePath && t.ComponentType == componentType && t.PropertyPath == propertyPath)
                return t;
        }
        return null;
    }

    /// <summary>Finds or creates a string track for the given target and property path.</summary>
    public AnimationStringTrack GetOrCreateStringTrack(string relativePath, string componentType, string propertyPath)
    {
        if (FindStringTrack(relativePath, componentType, propertyPath) is { } existing)
            return existing;

        var track = new AnimationStringTrack(relativePath, componentType, propertyPath);
        StringTracks.Add(track);
        Invalidate();
        return track;
    }

    /// <summary>Adds or updates a discrete string keyframe on the matching track.</summary>
    public AnimationStringTrack SetStringKey(string relativePath, string componentType, string propertyPath, float time, string value, float tolerance = 0.0005f)
    {
        AnimationStringTrack track = GetOrCreateStringTrack(relativePath, componentType, propertyPath);
        track.SetKey(time, value, tolerance);
        Invalidate();
        return track;
    }

    /// <summary>Removes a curve track and invalidates the clip.</summary>
    public bool RemoveCurveTrack(AnimationCurveTrack track)
    {
        EnsureLoaded();
        if (!CurveTracks.Remove(track)) return false;
        Invalidate();
        return true;
    }

    /// <summary>Removes an object track and invalidates the clip.</summary>
    public bool RemoveObjectTrack(AnimationObjectTrack track)
    {
        EnsureLoaded();
        if (!ObjectTracks.Remove(track)) return false;
        Invalidate();
        return true;
    }

    /// <summary>Removes a string track and invalidates the clip.</summary>
    public bool RemoveStringTrack(AnimationStringTrack track)
    {
        EnsureLoaded();
        if (!StringTracks.Remove(track)) return false;
        Invalidate();
        return true;
    }

    /// <summary>Maps an elapsed playback time to the clip's local evaluation time according to <see cref="EffectiveWrapMode"/>.</summary>
    public float WrapTime(float time)
    {
        float duration = Duration;
        if (duration <= 1e-6f) return 0f;

        switch (EffectiveWrapMode)
        {
            case AnimationClipWrapMode.Once:
            case AnimationClipWrapMode.ClampForever:
                return Math.Clamp(time, 0f, duration);

            case AnimationClipWrapMode.Loop:
            {
                if (time >= 0f && time <= duration)
                    return time;
                float wrapped = time % duration;
                if (wrapped < 0f) wrapped += duration;
                return wrapped;
            }

            case AnimationClipWrapMode.PingPong:
            {
                float cycle = duration * 2f;
                float t = time % cycle;
                if (t < 0f) t += cycle;
                return t <= duration ? t : cycle - t;
            }

            default:
                return Math.Clamp(time, 0f, duration);
        }
    }

    /// <summary>
    /// Unpacks the imported skeletal/humanoid Motion poses of this clip into editable <see cref="CurveTracks"/>
    /// (LocalPosition, LocalRotation, LocalScale per animated bone).
    /// When <paramref name="targetRoot"/> is supplied, bone names are resolved to their exact relative hierarchy paths under <paramref name="targetRoot"/>.
    /// Returns the number of curve tracks added or updated.
    /// </summary>
    public int UnpackMotionToCurveTracks(GameObject? targetRoot = null, bool clearMotionPayload = false)
    {
        EnsureLoaded();
        Avatar? avatarAsset = Avatar;
        if (avatarAsset.IsNotValid() && targetRoot.IsValid())
        {
            Animator? anim = targetRoot!.GetComponent<Animator>();
            if (anim.IsValid() && anim!.Avatar.IsValid())
                avatarAsset = anim.Avatar;
        }

        if (avatarAsset.IsNotValid()) return 0;
        if (avatarAsset!.State is not (AssetState.Missing or AssetState.Failed))
            avatarAsset.Load();
        if (avatarAsset.Skeleton == null) return 0;

        MotionSkeleton skeleton = avatarAsset.Skeleton;
        MotionClip? motionClip = GetClip(avatarAsset.Runtime);
        if (motionClip == null) return 0;

        AnimatorBinding? hierarchyBinding = targetRoot.IsValid() ? new AnimatorBinding(targetRoot!.Transform, skeleton) : null;

        int frames = Math.Max(2, motionClip.FrameCount);
        float duration = motionClip.Duration;
        var poses = new Pose[frames];
        for (int f = 0; f < frames; f++)
        {
            float norm = frames > 1 ? f / (float)(frames - 1) : 0f;
            var pose = new Pose(skeleton);
            pose.SetToReferencePose(false);
            motionClip.GetPose(norm, pose);
            poses[f] = pose;
        }

        int tracksAdded = 0;

        for (int b = 0; b < skeleton.BoneCount; b++)
        {
            string? relPath = null;
            if (hierarchyBinding != null)
            {
                Transform? bt = hierarchyBinding.BoneTransform(b);
                if (bt != null && !ReferenceEquals(bt, targetRoot!.Transform))
                    relPath = Transform.GetRelativePath(bt, targetRoot.Transform);
                else if (bt != null && ReferenceEquals(bt, targetRoot.Transform))
                    continue; // Root transform is driven by root motion
            }

            if (relPath == null)
            {
                relPath = BuildSkeletonBoneRelativePath(skeleton, b);
                if (string.IsNullOrEmpty(relPath)) continue;
            }

            Transform3D first = poses[0].GetTransform(b);
            Transform3D reference = skeleton.GetBoneParentSpaceTransform(b);
            bool movesPos = Float3.LengthSquared(first.position - reference.position) > 1e-7f;
            bool movesRot = MathF.Abs(Quaternion.Dot(first.rotation, reference.rotation)) < 1f - 1e-6f;
            bool movesScale = Float3.LengthSquared(first.scale - reference.scale) > 1e-7f;

            for (int f = 1; f < frames; f++)
            {
                Transform3D cur = poses[f].GetTransform(b);
                if (!movesPos && Float3.LengthSquared(cur.position - first.position) > 1e-7f) movesPos = true;
                if (!movesRot && MathF.Abs(Quaternion.Dot(cur.rotation, first.rotation)) < 1f - 1e-6f) movesRot = true;
                if (!movesScale && Float3.LengthSquared(cur.scale - first.scale) > 1e-7f) movesScale = true;
            }

            if (!movesPos && !movesRot && !movesScale) continue;

            if (movesPos)
            {
                AnimationCurveTrack tx = GetOrCreateCurveTrack(relPath, "Transform", "LocalPosition.X");
                AnimationCurveTrack ty = GetOrCreateCurveTrack(relPath, "Transform", "LocalPosition.Y");
                AnimationCurveTrack tz = GetOrCreateCurveTrack(relPath, "Transform", "LocalPosition.Z");
                tx.Curve.Clear(); ty.Curve.Clear(); tz.Curve.Clear();
                for (int f = 0; f < frames; f++)
                {
                    float t = frames > 1 ? duration * f / (frames - 1) : 0f;
                    Float3 p = poses[f].GetTransform(b).position;
                    tx.Curve.AddKey(new Keyframe(t, p.X));
                    ty.Curve.AddKey(new Keyframe(t, p.Y));
                    tz.Curve.AddKey(new Keyframe(t, p.Z));
                }
                tx.Curve.SmoothTangents(CurveTangentMode.Auto);
                ty.Curve.SmoothTangents(CurveTangentMode.Auto);
                tz.Curve.SmoothTangents(CurveTangentMode.Auto);
                tracksAdded += 3;
            }

            if (movesRot)
            {
                AnimationCurveTrack rx = GetOrCreateCurveTrack(relPath, "Transform", "LocalRotation.X");
                AnimationCurveTrack ry = GetOrCreateCurveTrack(relPath, "Transform", "LocalRotation.Y");
                AnimationCurveTrack rz = GetOrCreateCurveTrack(relPath, "Transform", "LocalRotation.Z");
                AnimationCurveTrack rw = GetOrCreateCurveTrack(relPath, "Transform", "LocalRotation.W");
                rx.Curve.Clear(); ry.Curve.Clear(); rz.Curve.Clear(); rw.Curve.Clear();
                Quaternion prevQ = poses[0].GetTransform(b).rotation;
                for (int f = 0; f < frames; f++)
                {
                    float t = frames > 1 ? duration * f / (frames - 1) : 0f;
                    Quaternion q = poses[f].GetTransform(b).rotation;
                    if (f > 0 && Quaternion.Dot(prevQ, q) < 0f)
                        q = new Quaternion(-q.X, -q.Y, -q.Z, -q.W);
                    prevQ = q;
                    rx.Curve.AddKey(new Keyframe(t, q.X));
                    ry.Curve.AddKey(new Keyframe(t, q.Y));
                    rz.Curve.AddKey(new Keyframe(t, q.Z));
                    rw.Curve.AddKey(new Keyframe(t, q.W));
                }
                rx.Curve.SmoothTangents(CurveTangentMode.Auto);
                ry.Curve.SmoothTangents(CurveTangentMode.Auto);
                rz.Curve.SmoothTangents(CurveTangentMode.Auto);
                rw.Curve.SmoothTangents(CurveTangentMode.Auto);
                tracksAdded += 4;
            }

            if (movesScale)
            {
                AnimationCurveTrack sx = GetOrCreateCurveTrack(relPath, "Transform", "LocalScale.X");
                AnimationCurveTrack sy = GetOrCreateCurveTrack(relPath, "Transform", "LocalScale.Y");
                AnimationCurveTrack sz = GetOrCreateCurveTrack(relPath, "Transform", "LocalScale.Z");
                sx.Curve.Clear(); sy.Curve.Clear(); sz.Curve.Clear();
                for (int f = 0; f < frames; f++)
                {
                    float t = frames > 1 ? duration * f / (frames - 1) : 0f;
                    Float3 s = poses[f].GetTransform(b).scale;
                    sx.Curve.AddKey(new Keyframe(t, s.X));
                    sy.Curve.AddKey(new Keyframe(t, s.Y));
                    sz.Curve.AddKey(new Keyframe(t, s.Z));
                }
                sx.Curve.SmoothTangents(CurveTangentMode.Auto);
                sy.Curve.SmoothTangents(CurveTangentMode.Auto);
                sz.Curve.SmoothTangents(CurveTangentMode.Auto);
                tracksAdded += 3;
            }
        }

        if (clearMotionPayload && tracksAdded > 0)
        {
            _payload = null;
            _skeletal = null;
            _humanoid = null;
            _bound = null;
            _kind = AnimationClipKind.Property;
        }

        Invalidate();
        return tracksAdded;
    }

    private static string BuildSkeletonBoneRelativePath(MotionSkeleton skeleton, int boneIndex)
    {
        string? name = skeleton.GetBoneID(boneIndex).DebugName;
        if (string.IsNullOrEmpty(name)) return string.Empty;
        if (name.Contains('/')) return name;

        int parent = skeleton.GetParentBoneIndex(boneIndex);
        if (parent < 0) return string.Empty; // Root node itself

        var stack = new Stack<string>();
        for (int curr = boneIndex; curr >= 0; curr = skeleton.GetParentBoneIndex(curr))
        {
            int p = skeleton.GetParentBoneIndex(curr);
            if (p < 0) break; // Stop at root
            string? seg = skeleton.GetBoneID(curr).DebugName;
            if (!string.IsNullOrEmpty(seg))
                stack.Push(seg);
        }
        return string.Join('/', stack);
    }

    /// <summary>
    /// Simplifies every curve track in this clip by removing redundant interior keyframes whose removal keeps the
    /// curve within <paramref name="tolerance"/> of its original value. Ideal for cleaning up dense curves after
    /// <see cref="UnpackMotionToCurveTracks"/>. Returns the total number of keyframes removed.
    /// </summary>
    public int ReduceKeyframes(float tolerance = 0.005f)
    {
        EnsureLoaded();
        tolerance = MathF.Max(1e-6f, tolerance);
        int removedTotal = 0;

        for (int i = 0; i < CurveTracks.Count; i++)
        {
            AnimationCurve curve = CurveTracks[i].Curve;
            if (curve.Count <= 2) continue;

            // Step (constant) curves encode discrete changes (toggles, enums); linear simplification would corrupt them.
            bool hasStep = false;
            for (int k = 0; k < curve.Count; k++)
                if (curve[k].Interpolation == CurveInterpolation.Step) { hasStep = true; break; }
            if (hasStep) continue;

            // Snapshot original curve samples to measure error accurately
            var original = new Keyframe[curve.Count];
            for (int k = 0; k < original.Length; k++)
                original[k] = curve[k];

            bool[] keep = new bool[original.Length];
            keep[0] = true;
            keep[^1] = true;
            ReduceSegmentRdp(original, 0, original.Length - 1, tolerance, keep);

            int keptCount = 0;
            for (int k = 0; k < keep.Length; k++)
                if (keep[k]) keptCount++;

            if (keptCount < original.Length)
            {
                curve.Clear();
                for (int k = 0; k < original.Length; k++)
                    if (keep[k]) curve.AddKey(original[k]);
                curve.SmoothTangents(CurveTangentMode.Auto);
                removedTotal += original.Length - keptCount;
            }
        }

        if (removedTotal > 0)
            Invalidate();

        return removedTotal;
    }

    private static void ReduceSegmentRdp(Keyframe[] keys, int start, int end, float tolerance, bool[] keep)
    {
        if (end <= start + 1) return;

        float t0 = keys[start].Time;
        float v0 = keys[start].Value;
        float t1 = keys[end].Time;
        float v1 = keys[end].Value;
        float span = MathF.Max(1e-6f, t1 - t0);

        int maxIdx = -1;
        float maxErr = 0f;
        for (int i = start + 1; i < end; i++)
        {
            float alpha = (keys[i].Time - t0) / span;
            float interp = v0 + (v1 - v0) * alpha;
            float err = MathF.Abs(keys[i].Value - interp);
            if (err > maxErr)
            {
                maxErr = err;
                maxIdx = i;
            }
        }

        if (maxIdx > start && maxErr > tolerance)
        {
            keep[maxIdx] = true;
            ReduceSegmentRdp(keys, start, maxIdx, tolerance, keep);
            ReduceSegmentRdp(keys, maxIdx, end, tolerance, keep);
        }
    }

    /// <summary>Creates a standalone copy of this <see cref="AnimationClip"/>, optionally unpacking skeletal motion into editable curves.</summary>
    public AnimationClip Duplicate(GameObject? targetRoot = null, bool unpackMotion = false)
    {
        EnsureLoaded();
        var copy = new AnimationClip
        {
            Name = $"{Name}_Copy",
            Avatar = Avatar,
            Loop = Loop,
            FrameRate = FrameRate,
            WrapMode = WrapMode,
            CompletionMode = CompletionMode,
            SourceName = SourceName,
            TakeStart = TakeStart,
            _kind = _kind,
            _duration = _duration,
            _frameCount = _frameCount,
            _payload = _payload != null ? (byte[])_payload.Clone() : null,
        };
        for (int i = 0; i < Events.Count; i++)
            copy.Events.Add(Events[i].Clone());
        for (int i = 0; i < CurveTracks.Count; i++)
            copy.CurveTracks.Add(CurveTracks[i].Clone());
        for (int i = 0; i < ObjectTracks.Count; i++)
            copy.ObjectTracks.Add(ObjectTracks[i].Clone());
        for (int i = 0; i < StringTracks.Count; i++)
            copy.StringTracks.Add(StringTracks[i].Clone());
        for (int i = 0; i < PropertyEvents.Count; i++)
            copy.PropertyEvents.Add(PropertyEvents[i].Clone());

        if (unpackMotion)
            copy.UnpackMotionToCurveTracks(targetRoot, clearMotionPayload: false);

        return copy;
    }

    protected override void OnUnload()
    {
        _skeletal = null;
        _humanoid = null;
        _bound = null;
        _syntheticMotion = null;
    }

    protected override void TakeContent(Asset staging)
    {
        int version = _version;
        base.TakeContent(staging);
        _version = Math.Max(version, _version) + 1;
        _syntheticMotion = null;
    }

    protected internal override long EstimateBytes()
        => (_payload?.Length ?? 0) * 3L + (CurveTracks.Count + ObjectTracks.Count + StringTracks.Count + PropertyEvents.Count) * 128L;

    public void Serialize(ref EchoObject value, SerializationContext ctx)
    {
        SerializeHeader(value);
        value.Add("Kind", new EchoObject((int)_kind));
        value.Add("Loop", new EchoObject(Loop));
        value.Add("FrameRate", new EchoObject(FrameRate));
        value.Add("WrapMode", new EchoObject((int)WrapMode));
        value.Add("CompletionMode", new EchoObject((int)CompletionMode));
        value.Add("SourceName", new EchoObject(SourceName));
        value.Add("Duration", new EchoObject(_duration));
        value.Add("FrameCount", new EchoObject(_frameCount));
        value.Add("Avatar", Serializer.Serialize(typeof(Avatar), Avatar, ctx));
        value.Add("Events", Serializer.Serialize(Events, ctx));
        value.Add("TakeStart", new EchoObject(TakeStart));
        value.Add("Clip", new EchoObject(_payload ?? Array.Empty<byte>()));

        if (CurveTracks.Count > 0)
            value.Add("CurveTracks", Serializer.Serialize(CurveTracks, ctx));
        if (ObjectTracks.Count > 0)
            value.Add("ObjectTracks", Serializer.Serialize(ObjectTracks, ctx));
        if (StringTracks.Count > 0)
            value.Add("StringTracks", Serializer.Serialize(StringTracks, ctx));
        if (PropertyEvents.Count > 0)
            value.Add("PropertyEvents", Serializer.Serialize(PropertyEvents, ctx));
    }

    public void Deserialize(EchoObject value, SerializationContext ctx)
    {
        DeserializeHeader(value);
        _kind = (AnimationClipKind)(value.Get("Kind")?.IntValue ?? 0);
        Loop = value.Get("Loop")?.BoolValue ?? true;
        FrameRate = value.Get("FrameRate")?.FloatValue ?? 60f;
        WrapMode = value.Get("WrapMode") is { } wm
            ? (AnimationClipWrapMode)wm.IntValue
            : (Loop ? AnimationClipWrapMode.Loop : AnimationClipWrapMode.Once);
        CompletionMode = value.Get("CompletionMode") is { } cm
            ? (AnimationClipCompletionMode)cm.IntValue
            : AnimationClipCompletionMode.KeepState;
        SourceName = value.Get("SourceName")?.StringValue ?? string.Empty;
        _duration = value.Get("Duration")?.FloatValue ?? 0f;
        _frameCount = value.Get("FrameCount")?.IntValue ?? 0;
        Avatar = Serializer.Deserialize<Avatar>(value.Get("Avatar"), ctx);
        Events = value.Get("Events") is { } events ? Serializer.Deserialize<List<ClipEvent>>(events, ctx) ?? [] : [];
        TakeStart = value.Get("TakeStart")?.FloatValue ?? 0f;

        CurveTracks = value.Get("CurveTracks") is { } ct ? Serializer.Deserialize<List<AnimationCurveTrack>>(ct, ctx) ?? [] : [];
        ObjectTracks = value.Get("ObjectTracks") is { } ot ? Serializer.Deserialize<List<AnimationObjectTrack>>(ot, ctx) ?? [] : [];
        StringTracks = value.Get("StringTracks") is { } st ? Serializer.Deserialize<List<AnimationStringTrack>>(st, ctx) ?? [] : [];
        PropertyEvents = value.Get("PropertyEvents") is { } pe ? Serializer.Deserialize<List<AnimationClipEvent>>(pe, ctx) ?? [] : [];

        byte[]? payload = value.Get("Clip")?.ByteArrayValue;
        _payload = payload is { Length: > 0 } ? payload : null;
        _skeletal = null;
        _humanoid = null;
        _bound = null;
        _syntheticMotion = null;
        _decodeFailed = false;
        _version++;
    }
}
