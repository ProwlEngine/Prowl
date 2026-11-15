// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Runtime.Networking;

/// <summary>A client's estimate of the host's time, from the tick, time and time rate the host sends.</summary>
internal sealed class NetworkClock
{
    // Further off than this and the estimate jumps instead of easing, such as after a lost run of samples.
    private const double SnapSeconds = 0.25;
    private const double Easing = 0.1;

    private bool _synced;
    private double _anchorLocal;
    private double _anchorHost;
    private double _rate = 1;
    private double _floor = double.NegativeInfinity;
    private uint _tick;
    private double _time;

    internal bool Synced => _synced;

    internal void Observe(uint tick, double time, double rate, double now, double roundTrip)
    {
        if (!double.IsFinite(time) || !double.IsFinite(rate) || (_synced && (tick < _tick || (tick == _tick && time < _time))))
        {
            return;
        }

        rate = Math.Max(0, rate);
        double sample = time + rate * Math.Max(0, roundTrip) / 2;
        double predicted = Estimate(now);
        if (!_synced || Math.Abs(sample - predicted) > SnapSeconds)
        {
            _anchorHost = sample;
            _floor = double.NegativeInfinity;
            _synced = true;
        }
        else
        {
            _anchorHost = predicted + (sample - predicted) * Easing;
        }

        _anchorLocal = now;
        _rate = rate;
        _tick = tick;
        _time = time;
    }

    /// <summary>The host's time now. It never runs backwards, except to jump back to a host far off the estimate.</summary>
    internal double ServerTime(double now) => _synced ? _floor = Math.Max(_floor, Estimate(now)) : 0;

    internal uint Tick(double now, double interval)
    {
        if (!_synced)
        {
            return 0;
        }

        double ahead = Math.Floor((ServerTime(now) - _time) / interval);
        return _tick + (uint)Math.Clamp(ahead, 0, uint.MaxValue - _tick);
    }

    private double Estimate(double now) => _anchorHost + (now - _anchorLocal) * _rate;
}
