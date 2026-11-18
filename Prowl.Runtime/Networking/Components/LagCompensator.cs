// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Courier;
using Prowl.Vector;

namespace Prowl.Runtime.Networking;

/// <summary>
///     Host: where this object was over the last <see cref="HistorySeconds" />, recorded each network tick, so a
///     client's hit can be checked against where the object was when that client saw it, as Mirror's LagCompensator.
/// </summary>
public sealed class LagCompensator : Component, INetworkTick
{
    private readonly List<(double Time, Float3 Position, Quaternion Rotation)> _history = new();

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

    /// <summary>The world pose <paramref name="viewer" /> was shown, or false outside what is remembered.</summary>
    public bool TrySampleSeenBy(NetPeer viewer, out Float3 position, out Quaternion rotation) =>
        TrySample(ViewTimeOf(viewer), out position, out rotation);

    /// <summary>
    ///     The server time <paramref name="viewer" /> is showing now: its estimate is half a round trip behind, and its
    ///     interpolation buffer, at least two ticks or the interpolation delay, is behind that.
    /// </summary>
    public static double ViewTimeOf(NetPeer viewer)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        double halfTrip = (viewer.Connection?.RoundTripTime.TotalSeconds ?? 0) / 2;
        double buffer = Math.Max(Networking.InterpolationDelay.TotalSeconds, 2 * Networking.TickInterval);
        return Networking.ServerTime - halfTrip - buffer;
    }
}
