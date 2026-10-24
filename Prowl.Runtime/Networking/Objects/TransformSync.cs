// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Vector;

using static Prowl.Runtime.Networking.NetworkSystem;

namespace Prowl.Runtime.Networking;

internal readonly record struct TransformSample(double Time, Float3 Position, Quaternion Rotation, Float3 Scale);

/// <summary>
///     One networked object's transform: what its controller last sent, and what everyone else has received and
///     shows from it. The messages that carry it belong to <see cref="NetworkSystem" />; this decides what goes in them.
/// </summary>
internal sealed class TransformSync
{
    private const int MaxSamples = 32;

    // Quiet ticks before the final pose goes out reliably.
    private const int SettleTicks = 3;

    // Ticks a channel is still sent after it stops changing, while the rest of the object moves on.
    private const int LingerTicks = 3;

    // Smaller changes don't count as movement, so a body jittering in place still settles. They accumulate
    // against the last pose sent, so slow motion is still sent once it adds up.
    private const float PositionEpsilon = 1e-4f;
    private const float RotationEpsilon = 1e-5f;
    private const float ScaleEpsilon = 1e-4f;

    private readonly TransformTimeline _timeline = new();

    // Sending, while this peer controls the object.
    private bool _hasSent;
    private double _lastSentTime;
    private int _lingerPosition, _lingerRotation, _lingerScale;
    private int _quietTicks;
    private Float3 _sentPosition;
    private Quaternion _sentRotation;
    private Float3 _sentScale;

    // Receiving, while someone else does. Oldest first.
    public List<TransformSample> Samples { get; } = new();

    public bool HasReceived { get; private set; }

    public uint LastReceivedTick { get; private set; }

    /// <summary>True once the object has come to rest and its exact pose has gone out reliably.</summary>
    public bool Settled { get; private set; }

    /// <summary>Starts over, as for a new controller or new channel flags.</summary>
    public void Reset()
    {
        _hasSent = false;
        HasReceived = false;
        ClearHistory();
    }

    /// <summary>Forgets interpolation history, which would otherwise pull the object back to older poses.</summary>
    public void ClearHistory()
    {
        Samples.Clear();
        _timeline.Clear();
    }

    /// <summary>
    ///     What to send this tick, or null for nothing. <paramref name="previousTick" /> is when the last tick went
    ///     out, so an object that starts moving after resting can say how long it stood still.
    /// </summary>
    public TransformUpdate? Capture(GameObject go, double now, double previousTick)
    {
        Transform t = go.Transform;
        Float3 position = t.LocalPosition;
        Quaternion rotation = t.LocalRotation;
        Float3 scale = t.LocalScale;
        TransformChannels synced = SyncedChannels(go);
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
            channels = synced | TransformChannels.ExactRotation;
            Settled = true;
            _lingerPosition = _lingerRotation = _lingerScale = 0;
        }
        else
        {
            return null;
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

        return new TransformUpdate(channels, stillFor, position, rotation, scale);
    }

    /// <summary>
    ///     Takes an update from the object's controller, or returns null if it's older than one already here. What
    ///     comes back is what was taken: only channels the object syncs, the rest filled in from before.
    /// </summary>
    public TransformUpdate? Receive(GameObject go, uint tick, double sentAt, double now, TransformUpdate update)
    {
        // Unreliable, so older updates can arrive after newer ones.
        if ((HasReceived && (int)(tick - LastReceivedTick) <= 0) || !_timeline.Observe(sentAt, now))
        {
            return null;
        }

        // Use the host-supplied object policy, never channels chosen by the sender.
        TransformChannels channels = update.Channels &
                                     (SyncedChannels(go) | TransformChannels.ExactRotation | TransformChannels.Resumed);

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
        if ((go.Network.Flags & NetworkFlags.NoInterpolation) != 0)
        {
            ApplyNetworkTransform(go, position, rotation, scale);
        }

        return update with { Channels = channels, Position = position, Rotation = rotation, Scale = scale };
    }

    /// <summary>Jumps to a pose that arrived reliably (scene catch-up), unless a newer update is already here.</summary>
    public void Snap(GameObject go, uint tick, Float3 position, Quaternion rotation, Float3 scale)
    {
        if (HasReceived && (int)(tick - LastReceivedTick) < 0)
        {
            return;
        }

        ClearHistory();
        HasReceived = true;
        LastReceivedTick = tick;
        ApplyNetworkTransform(go, position, rotation, scale);
    }

    /// <summary>Shows the object where its controller had it, InterpolationDelay ago.</summary>
    public void Interpolate(GameObject go, double now)
    {
        if (Samples.Count == 0 || (go.Network.Flags & NetworkFlags.NoInterpolation) != 0)
        {
            return;
        }

        double renderTime = _timeline.RenderTime(now, Networking.InterpolationDelay.TotalSeconds);

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

        ApplyNetworkTransform(go, Maths.Lerp(from.Position, to.Position, t),
            Quaternion.Slerp(from.Rotation, to.Rotation, t), Maths.Lerp(from.Scale, to.Scale, t));
    }

    private void AddSample(TransformSample sample)
    {
        if (Samples.Count == MaxSamples)
        {
            Samples.RemoveAt(0);
        }

        Samples.Add(sample);
    }

    private TransformChannels Changes(TransformChannels synced, Float3 position, Quaternion rotation, Float3 scale) =>
        ((synced & TransformChannels.Position) != 0 && Moved(position, _sentPosition, PositionEpsilon)
            ? TransformChannels.Position
            : 0) |
        ((synced & TransformChannels.Rotation) != 0 && Moved(rotation, _sentRotation)
            ? TransformChannels.Rotation
            : 0) |
        ((synced & TransformChannels.Scale) != 0 && Moved(scale, _sentScale, ScaleEpsilon)
            ? TransformChannels.Scale
            : 0);

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

    internal bool Moved(Quaternion a, Quaternion b) =>
        MathF.Abs(a.X - b.X) > RotationEpsilon || MathF.Abs(a.Y - b.Y) > RotationEpsilon ||
        MathF.Abs(a.Z - b.Z) > RotationEpsilon || MathF.Abs(a.W - b.W) > RotationEpsilon;
}
