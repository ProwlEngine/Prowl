// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Vector;

using static Prowl.Runtime.Networking.NetworkSystem;

namespace Prowl.Runtime.Networking;

internal readonly record struct TransformSample(double Time, Float3 Position, Quaternion Rotation, Float3 Scale);

/// <summary>
///     What one <see cref="NetworkTransform" /> syncs: what the object's controller last sent of it, and what everyone
///     else has received and shows. <see cref="TransformSystem" /> carries the records; this decides what goes in them.
/// </summary>
internal sealed class TransformSync(NetworkTransform? settings = null)
{
    private const int MaxSamples = 32;

    // The channels to send and take, and whether to blend, from the component it belongs to, or everything without one.
    private TransformChannels Synced => ReferenceEquals(settings, null) ? TransformChannels.Pose : settings.Channels;
    private bool Blends => ReferenceEquals(settings, null) || settings.Interpolate;
    private int Interval => ReferenceEquals(settings, null) ? 1 : settings.Interval;

    private void Apply(GameObject go, Float3 position, Quaternion rotation, Float3 scale)
    {
        if (!ReferenceEquals(settings, null))
        {
            settings.Apply(position, rotation, scale);
            return;
        }

        go.Transform.LocalPosition = position;
        go.Transform.LocalRotation = rotation;
        go.Transform.LocalScale = scale;
    }

    // History taken under other settings would bring back poses the object no longer follows.
    private (TransformChannels Synced, bool Blends) _settings = (TransformChannels.Pose, true);

    private void NoteSettings()
    {
        (TransformChannels, bool) current = (Synced, Blends);
        if (current != _settings)
        {
            _settings = current;
            ClearHistory();
        }
    }

    // Quiet ticks before the final pose goes out, exact, and is owed until acknowledged.
    private const int SettleTicks = 3;

    // Ticks a channel is still sent after it stops changing, while the rest of the object moves on.
    private const int LingerTicks = 3;

    // Ticks a teleport tags what goes out, so a receiver that lost the first tagged update still jumps.
    private const int TeleportTicks = 32;

    private readonly TransformTimeline _timeline = new();

    // Sending, while this peer controls the object.
    private bool _hasSent;

    // The Transform version and channels it was last found at rest with, so a resting object costs one comparison.
    private uint _restVersion;
    private TransformChannels _restSynced;
    private double _lastSentTime;
    private int _lingerPosition, _lingerRotation, _lingerScale;
    private int _quietTicks;
    private Float3 _sentPosition;
    private Quaternion _sentRotation;
    private Float3 _sentScale;
    private byte _teleport;
    private int _teleportTicks;

    // The teleport sequence number this peer last jumped for, or -1 for none.
    private int _receivedTeleport = -1;

    // Receiving, while someone else does. Oldest first.
    public List<TransformSample> Samples { get; } = new();

    public bool HasReceived { get; private set; }

    /// <summary>True while received samples still need to be played back.</summary>
    public bool HasPendingInterpolation { get; private set; }

    public uint LastReceivedTick { get; private set; }

    /// <summary>True once the object has come to rest and its exact pose has gone out.</summary>
    public bool Settled { get; private set; }

    /// <summary>Starts over, as for a new controller.</summary>
    public void Reset()
    {
        _hasSent = false;
        _teleportTicks = 0;
        _receivedTeleport = -1;
        HasReceived = false;
        ClearHistory();
    }

    /// <summary>The object already holds its controller's pose as of <paramref name="tick" />, so older updates are dropped.</summary>
    public void ReceivedAt(uint tick)
    {
        HasReceived = true;
        LastReceivedTick = tick;
    }

    /// <summary>Forgets interpolation history, which would otherwise pull the object back to older poses.</summary>
    public void ClearHistory()
    {
        Samples.Clear();
        _timeline.Clear();
        HasPendingInterpolation = false;
    }

    /// <summary>The object was just moved somewhere else at once: the next updates go whole, telling receivers to jump.</summary>
    public void Teleport()
    {
        _teleport++;
        _teleportTicks = TeleportTicks;
        _hasSent = false;
    }

    /// <summary>
    ///     What to send this tick, or null for nothing. <paramref name="previousTick" /> is when the last tick went
    ///     out, so an object that starts moving after resting can say how long it stood still.
    /// </summary>
    public TransformUpdate? Capture(GameObject go, double now, double previousTick)
    {
        Transform t = go.Transform;
        TransformChannels synced = Synced;

        // At rest and untouched since it was last looked at: nothing to compare.
        uint version = t.Version;
        if (_hasSent && Settled && _teleportTicks == 0 && version == _restVersion && synced == _restSynced)
        {
            return null;
        }

        Float3 position = t.LocalPosition;
        Quaternion rotation = t.LocalRotation;
        Float3 scale = t.LocalScale;
        TransformChannels changed = _hasSent ? Changes(synced, position, rotation, scale) : synced;

        TransformChannels channels;
        float stillFor = 0;
        if (changed != 0)
        {
            channels = changed | Lingering(changed);
            if (_hasSent && Settled && previousTick > _lastSentTime)
            {
                channels |= TransformChannels.Resumed;
                stillFor = (float)(now - previousTick);
            }

            _quietTicks = 0;
            Settled = false;
        }
        else if (!Settled && ++_quietTicks >= SettleTicks)
        {
            // Updates while moving can be lost, so the pose it comes to rest at must arrive, exactly.
            channels = synced | TransformChannels.Exact;
            Settled = true;
            _lingerPosition = _lingerRotation = _lingerScale = 0;
        }
        else
        {
            if (Settled)
            {
                _restVersion = version;
                _restSynced = synced;
            }

            return null;
        }

        if (_teleportTicks > 0)
        {
            // The pose it comes to rest at arrives for certain, so the tag ends with it.
            _teleportTicks = Settled ? 0 : _teleportTicks - 1;
            channels = (channels | TransformChannels.Teleport) & ~TransformChannels.Resumed;
            stillFor = 0;
        }

        _hasSent = true;
        _lastSentTime = now;
        if ((channels & TransformChannels.Position) != 0)
        {
            _sentPosition = position;
        }

        if ((channels & TransformChannels.Rotation) != 0)
        {
            _sentRotation = rotation;
        }

        if ((channels & TransformChannels.Scale) != 0)
        {
            _sentScale = scale;
        }

        return new TransformUpdate(channels, stillFor, position, rotation, scale, _teleport);
    }

    /// <summary>
    ///     Takes an update from the object's controller, or returns null if it's older than one already here. What
    ///     comes back is what was taken: only channels the object syncs, the rest filled in from before.
    /// </summary>
    public TransformUpdate? Receive(GameObject go, uint tick, double sentAt, double now, TransformUpdate update)
    {
        NoteSettings();

        // Unreliable, so older updates can arrive after newer ones.
        if (HasReceived && (int)(tick - LastReceivedTick) <= 0)
        {
            return null;
        }

        bool jump = (update.Channels & TransformChannels.Teleport) != 0 && update.Teleport != _receivedTeleport;
        if (jump)
        {
            ClearHistory();
            _receivedTeleport = update.Teleport;
        }

        if (!_timeline.Observe(sentAt, now))
        {
            // A moving update out of time order is dropped. The pose it came to rest at is sent once and must be
            // taken: its tick already says it is the newer one, so it goes in at the latest time held.
            if ((update.Channels & TransformChannels.Exact) == 0)
            {
                return null;
            }

            sentAt = Samples.Count > 0 ? Math.Max(sentAt, Samples[^1].Time) : sentAt;
        }

        // Use this peer's own component settings, never channels chosen by the sender.
        TransformChannels channels = update.Channels & (Synced | TransformChannels.Exact |
                                                        TransformChannels.Resumed | TransformChannels.Teleport);

        // Channels left out are as they were in the latest update, or as the object is if there's none.
        Transform current = go.Transform;
        TransformSample latest = Samples.Count > 0
            ? Samples[^1]
            : new TransformSample(sentAt, current.LocalPosition, current.LocalRotation, current.LocalScale);
        Float3 position = (channels & TransformChannels.Position) != 0 ? update.Position : latest.Position;
        Quaternion rotation = (channels & TransformChannels.Rotation) != 0
            ? Quaternion.Normalize(update.Rotation)
            : latest.Rotation;
        Float3 scale = (channels & TransformChannels.Scale) != 0 ? update.Scale : latest.Scale;

        // Held where it rested until just before it moved, rather than blended over the whole time it sat still.
        if ((channels & TransformChannels.Resumed) != 0 && Samples.Count > 0 && sentAt - update.StillFor > latest.Time)
        {
            AddSample(latest with { Time = sentAt - update.StillFor });
        }

        HasReceived = true;
        LastReceivedTick = tick;
        AddSample(new TransformSample(sentAt, position, rotation, scale));
        HasPendingInterpolation = !jump && Blends;
        if (jump || !Blends)
        {
            Apply(go, position, rotation, scale);
        }

        return update with { Channels = channels, Position = position, Rotation = rotation, Scale = scale };
    }

    /// <summary>Shows the object where its controller had it, InterpolationDelay ago.</summary>
    public void Interpolate(GameObject go, double now)
    {
        NoteSettings();
        if (!HasPendingInterpolation || Samples.Count == 0 || !Blends)
        {
            return;
        }

        double sendInterval = Interval * Networking.TickInterval;
        double renderTime = _timeline.RenderTime(now, sendInterval, Networking.InterpolationDelay.TotalSeconds);

        // Drop samples nothing will blend from again: everything before the last one at or before renderTime.
        int keepFrom = 0;
        while (keepFrom + 1 < Samples.Count && Samples[keepFrom + 1].Time <= renderTime)
        {
            keepFrom++;
        }

        if (keepFrom > 0)
        {
            Samples.RemoveRange(0, keepFrom);
        }

        TransformSample from = Samples[0];
        TransformSample to = Samples.Count > 1 ? Samples[1] : from;

        float t = to.Time > from.Time
            ? (float)Math.Clamp((renderTime - from.Time) / (to.Time - from.Time), 0, 1)
            : 1;

        Apply(go, Maths.Lerp(from.Position, to.Position, t),
            Quaternion.Slerp(from.Rotation, to.Rotation, t), Maths.Lerp(from.Scale, to.Scale, t));

        // Keep the final sample for future updates, but stop applying it once playback catches up.
        HasPendingInterpolation = renderTime < Samples[^1].Time;
    }

    private void AddSample(TransformSample sample)
    {
        if (Samples.Count == MaxSamples)
        {
            Samples.RemoveAt(0);
        }

        Samples.Add(sample);
    }

    // Compare with the last sent pose so slow movement accumulates, while bounded physics jitter can settle.
    private TransformChannels Changes(TransformChannels synced, Float3 position, Quaternion rotation, Float3 scale)
    {
        NetworkConfiguration configuration = Networking.TransformConfiguration;
        return ((synced & TransformChannels.Position) != 0 && Moved(position, _sentPosition, configuration.TransformPositionThreshold)
            ? TransformChannels.Position
            : 0) |
        ((synced & TransformChannels.Rotation) != 0 && Moved(rotation, _sentRotation, configuration.TransformRotationThresholdDegrees)
            ? TransformChannels.Rotation
            : 0) |
        ((synced & TransformChannels.Scale) != 0 && Moved(scale, _sentScale, configuration.TransformScaleThreshold)
            ? TransformChannels.Scale
            : 0);
    }

    /// <summary>The channels that changed recently but not this tick, counting down how long each has left.</summary>
    private TransformChannels Lingering(TransformChannels changed) =>
        Linger(ref _lingerPosition, TransformChannels.Position, changed) |
        Linger(ref _lingerRotation, TransformChannels.Rotation, changed) |
        Linger(ref _lingerScale, TransformChannels.Scale, changed);

    internal TransformChannels Linger(ref int ticks, TransformChannels channel, TransformChannels changed)
    {
        if ((changed & channel) != 0)
        {
            ticks = LingerTicks;
            return 0;
        }

        if (ticks <= 0)
        {
            return 0;
        }

        ticks--;
        return channel;
    }

    internal bool Moved(Float3 a, Float3 b, float epsilon) =>
        MathF.Abs(a.X - b.X) > epsilon || MathF.Abs(a.Y - b.Y) > epsilon || MathF.Abs(a.Z - b.Z) > epsilon;

    private static bool Moved(Quaternion a, Quaternion b, float degrees)
    {
        a = Quaternion.Normalize(a);
        b = Quaternion.Normalize(b);
        // q and -q describe the same rotation. Use the shorter chord, avoiding acos precision loss near rest.
        float dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z, dw = a.W - b.W;
        float sx = a.X + b.X, sy = a.Y + b.Y, sz = a.Z + b.Z, sw = a.W + b.W;
        float chord = 2 * MathF.Sin(degrees * Maths.Deg2Rad * 0.25f);
        return MathF.Min(dx * dx + dy * dy + dz * dz + dw * dw,
            sx * sx + sy * sy + sz * sz + sw * sw) > chord * chord;
    }
}
