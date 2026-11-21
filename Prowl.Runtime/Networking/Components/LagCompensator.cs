// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Courier;
using Prowl.Vector;

namespace Prowl.Runtime.Networking;

/// <summary>
///     Host: where this object was over the last <see cref="HistorySeconds" />, recorded each network tick, so a
///     client's hit can be checked against where the object was when that client saw it.
/// </summary>
[AddComponentMenu("Networking/Lag Compensator")]
public sealed class LagCompensator : Component, INetworkTick
{
    private readonly List<(double Time, Float3 Position, Quaternion Rotation)> _history = new();

    /// <summary>How far back, in seconds, it remembers where the object was.</summary>
    public float HistorySeconds = 1;

    public void OnNetworkTick(uint tick)
    {
        if (Networking.System is not { IsHost: true })
        {
            return;
        }

        double now = Networking.ServerTime;
        _history.Add((now, Transform.Position, Transform.Rotation));

        // One sample older than the window stays, to blend from at its edge.
        int drop = 0;
        while (drop < _history.Count - 1 && now - _history[drop + 1].Time > HistorySeconds)
        {
            drop++;
        }

        _history.RemoveRange(0, drop);
    }

    /// <summary>The world pose at <paramref name="time" /> on the server clock, or false outside what is remembered.</summary>
    public bool TrySample(double time, out Float3 position, out Quaternion rotation)
    {
        position = default;
        rotation = Quaternion.Identity;
        if (_history.Count == 0 || time < _history[0].Time)
        {
            return false;
        }

        for (int i = _history.Count - 1; i >= 0; i--)
        {
            if (_history[i].Time > time)
            {
                continue;
            }

            (double fromTime, Float3 fromPosition, Quaternion fromRotation) = _history[i];
            if (i == _history.Count - 1)
            {
                (position, rotation) = (fromPosition, fromRotation);
                return true;
            }

            (double toTime, Float3 toPosition, Quaternion toRotation) = _history[i + 1];
            float t = (float)((time - fromTime) / Math.Max(toTime - fromTime, 1e-9));
            position = Maths.Lerp(fromPosition, toPosition, t);
            rotation = Quaternion.Slerp(fromRotation, toRotation, t);
            return true;
        }

        return false;
    }

    /// <summary>
    ///     The world pose <paramref name="viewer" /> was shown when it sent what the host is handling now, or false
    ///     outside what is remembered. Call it as the viewer's hit arrives, as from the RPC that reports it.
    /// </summary>
    public bool TrySampleSeenBy(NetPeer viewer, out Float3 position, out Quaternion rotation) =>
        TrySample(SentTimeOf(viewer, SendIntervalTicks()), out position, out rotation);

    // How many ticks apart this object's transform goes out, which is what a viewer's buffer is sized by.
    private int SendIntervalTicks()
    {
        foreach (NetworkTransform transform in GameObject.GetComponents<NetworkTransform>())
        {
            if (ReferenceEquals(transform.Moved, GameObject))
            {
                return transform.Interval;
            }
        }

        return 1;
    }

    /// <summary>
    ///     The server time <paramref name="viewer" /> is showing now: its estimate is half a round trip behind, and its
    ///     interpolation buffer, at least two ticks or the interpolation delay, is behind that.
    /// </summary>
    public static double ViewTimeOf(NetPeer viewer)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        return Networking.ServerTime - RoundTrip(viewer) / 2 - Buffer(1);
    }

    /// <summary>
    ///     The server time <paramref name="viewer" /> was showing when it sent a message that arrives now: what it
    ///     shows now, less the half round trip the message took to get here. <paramref name="sendIntervalTicks" /> is
    ///     how many ticks apart the object looked at sends its transform.
    /// </summary>
    public static double SentTimeOf(NetPeer viewer, int sendIntervalTicks = 1)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        return Networking.ServerTime - RoundTrip(viewer) - Buffer(sendIntervalTicks);
    }

    private static double RoundTrip(NetPeer viewer) => viewer.Connection?.RoundTripTime.TotalSeconds ?? 0;

    // A viewer's interpolation buffer: two sends of the object, or the interpolation delay when that is longer.
    private static double Buffer(int sendIntervalTicks) => Math.Max(Networking.InterpolationDelay.TotalSeconds,
        2 * Networking.TickInterval * Math.Max(1, sendIntervalTicks));
}
