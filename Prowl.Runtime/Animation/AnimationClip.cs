// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Echo;
using Prowl.Motion;

using MotionAvatar = Prowl.Motion.Avatar;
using MotionClip = Prowl.Motion.AnimationClipBase;

namespace Prowl.Runtime;

/// <summary>What a clip stores, which decides which rigs can play it.</summary>
public enum AnimationClipKind
{
    /// <summary>Bone transforms for one skeleton. Plays on that rig, or on one matching it by bone name.</summary>
    Skeletal,
    /// <summary>Muscle space, baked through a humanoid avatar. Plays on any humanoid rig.</summary>
    Humanoid,
}

/// <summary>An animation clip: the encoded Motion clip, the rig it was authored on, and its settings.</summary>
public sealed class AnimationClip : EngineObject, ISerializable
{
    /// <summary>The rig the clip was authored on. A humanoid clip uses it only to preview.</summary>
    public AssetRef<Avatar> Avatar;

    /// <summary>Whether playback wraps or holds the final pose.</summary>
    public bool Loop = true;

    /// <summary>The clip's name in the file it was imported from, which its import settings are keyed on.</summary>
    public string SourceName = string.Empty;

    /// <summary>The clip's markers, handed to Motion when the clip is decoded.</summary>
    public List<ClipEvent> Events = new();

    /// <summary>Where in the take the clip starts, in seconds, as the import cut it.</summary>
    public float TakeStart;

    private AnimationClipKind _kind;
    private float _duration;
    private int _frameCount;
    private byte[]? _payload;

    [NonSerialized] private MotionClip? _skeletal;
    [NonSerialized] private HumanoidClip? _humanoid;
    [NonSerialized] private Dictionary<MotionAvatar, MotionClip>? _bound;
    [NonSerialized] private bool _decodeFailed;

    public AnimationClip() : base("Animation") { }

    public AnimationClipKind Kind { get { EnsureNotDisposed(); return _kind; } }

    /// <summary>Clip length in seconds, readable without decoding the payload.</summary>
    public float Duration { get { EnsureNotDisposed(); return _duration; } }

    public int FrameCount { get { EnsureNotDisposed(); return _frameCount; } }

    /// <summary>
    /// The clip Motion plays, decoded on first use and bound to <paramref name="target"/> when humanoid.
    /// Null while a skeletal clip's rig is still loading, or when the payload is missing.
    /// </summary>
    public MotionClip? GetClip(MotionAvatar? target)
    {
        EnsureNotDisposed();
        if (_payload == null || _decodeFailed)
            return null;

        return _kind == AnimationClipKind.Humanoid ? BindHumanoid(target) : DecodeSkeletal();
    }

    private MotionClip? DecodeSkeletal()
    {
        if (_skeletal != null)
            return _skeletal;

        Avatar avatar = Avatar.Res;
        if (avatar.IsNotValid() || avatar.Skeleton == null)
            return null;

        Motion.Skeleton skeleton = avatar.Skeleton;
        _skeletal = Decode(reader => MotionBinary.ReadClip(reader, skeleton, MotionEvents()));
        return _skeletal;
    }

    private MotionClip? BindHumanoid(MotionAvatar? target)
    {
        _humanoid ??= Decode(reader => MotionBinary.ReadHumanoidClip(reader, MotionEvents()));
        if (_humanoid == null || target is not { IsHuman: true })
            return null;

        _bound ??= new Dictionary<MotionAvatar, MotionClip>();
        if (_bound.TryGetValue(target, out MotionClip? bound))
            return bound;

        bound = _humanoid.Bind(target);
        _bound[target] = bound;
        return bound;
    }

    private AnimationEvent[] MotionEvents()
    {
        var events = new AnimationEvent[Events.Count];
        for (int i = 0; i < events.Length; i++) events[i] = Events[i].ToMotion(_duration);
        return events;
    }

    /// <summary>Drops the decoded clip, so the next use picks up the current events.</summary>
    public void EventsChanged()
    {
        _skeletal = null;
        _humanoid = null;
        _bound = null;
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
    public static AnimationClip FromSkeletal(MotionClip clip, AssetRef<Avatar> avatar, string? name = null, IEnumerable<ClipEvent>? events = null)
    {
        ArgumentNullException.ThrowIfNull(clip);
        AnimationClip asset = Create(AnimationClipKind.Skeletal, clip.Duration, clip.FrameCount, avatar, name, events, writer => MotionBinary.Write(writer, clip));
        if (events == null) asset._skeletal = clip;
        return asset;
    }

    /// <summary>Builds an asset around a clip baked into muscle space.</summary>
    public static AnimationClip FromHumanoid(HumanoidClip clip, AssetRef<Avatar> avatar, string? name = null, IEnumerable<ClipEvent>? events = null)
    {
        ArgumentNullException.ThrowIfNull(clip);
        AnimationClip asset = Create(AnimationClipKind.Humanoid, clip.Duration, clip.FrameCount, avatar, name, events, writer => MotionBinary.Write(writer, clip));
        if (events == null) asset._humanoid = clip;
        return asset;
    }

    private static AnimationClip Create(AnimationClipKind kind, float duration, int frames, AssetRef<Avatar> avatar, string? name,
        IEnumerable<ClipEvent>? events, Action<BinaryWriter> write)
    {
        var asset = new AnimationClip
        {
            Name = name ?? "Animation",
            Avatar = avatar,
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

    protected override void OnDispose()
    {
        _skeletal = null;
        _humanoid = null;
        _bound = null;
    }

    public void Serialize(ref EchoObject value, SerializationContext ctx)
    {
        SerializeHeader(value);
        value.Add("Kind", new EchoObject((int)_kind));
        value.Add("Loop", new EchoObject(Loop));
        value.Add("SourceName", new EchoObject(SourceName));
        value.Add("Duration", new EchoObject(_duration));
        value.Add("FrameCount", new EchoObject(_frameCount));
        value.Add("Avatar", Serializer.Serialize(Avatar, ctx));
        value.Add("Events", Serializer.Serialize(Events, ctx));
        value.Add("TakeStart", new EchoObject(TakeStart));
        value.Add("Clip", new EchoObject(_payload ?? Array.Empty<byte>()));
    }

    public void Deserialize(EchoObject value, SerializationContext ctx)
    {
        DeserializeHeader(value);
        _kind = (AnimationClipKind)(value.Get("Kind")?.IntValue ?? 0);
        Loop = value.Get("Loop")?.BoolValue ?? true;
        SourceName = value.Get("SourceName")?.StringValue ?? string.Empty;
        _duration = value.Get("Duration")?.FloatValue ?? 0f;
        _frameCount = value.Get("FrameCount")?.IntValue ?? 0;
        Avatar = Serializer.Deserialize<AssetRef<Avatar>>(value.Get("Avatar"), ctx);
        Events = value.Get("Events") is { } events ? Serializer.Deserialize<List<ClipEvent>>(events, ctx) ?? new() : new();
        TakeStart = value.Get("TakeStart")?.FloatValue ?? 0f;

        byte[]? payload = value.Get("Clip")?.ByteArrayValue;
        _payload = payload is { Length: > 0 } ? payload : null;
        _skeletal = null;
        _humanoid = null;
        _bound = null;
        _decodeFailed = false;
    }
}
