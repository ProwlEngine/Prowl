// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Runtime.Networking;

/// <summary>
///     Plays a controller's clock back a buffer behind it: two send intervals plus the measured jitter, or a minimum
///     delay if longer. Playback speeds up or slows down a little to hold that buffer, and jumps only when far behind.
/// </summary>
internal sealed class TransformTimeline
{
    private const double BufferIntervals = 2;
    private const double CatchUpSpeed = 1.04, SlowDownSpeed = 0.96;

    // Send intervals of drift before the speed changes, and beyond the buffer before playback jumps instead.
    private const double DriftIntervals = 1, JumpIntervals = 10;

    private bool _initialized, _playing;
    private double _jitter;
    private double _lastArrival;
    private double _lastNow;
    private double _lastSent;
    private double _offset;
    private double _playTime;

    public void Clear()
    {
        _initialized = false;
        _playing = false;
        _jitter = 0;
    }

    public bool Observe(double sent, double arrival)
    {
        if (_initialized && sent <= _lastSent)
        {
            return false;
        }

        double offset = arrival - sent;
        if (!_initialized)
        {
            _initialized = true;
            _offset = offset;
        }
        else
        {
            double variation = Math.Abs(arrival - _lastArrival - (sent - _lastSent));
            // Grow promptly on a late packet, then shrink gradually as the link settles.
            _jitter = Math.Max(Math.Min(variation, 0.5), _jitter * 0.9);
            // The least delayed sample estimates the clock offset. Permit slow upward drift
            // so a controller clock running slightly slower doesn't exhaust the buffer over time.
            _offset = Math.Min(offset, _offset + (sent - _lastSent) * 0.001);
        }

        _lastSent = sent;
        _lastArrival = arrival;
        return true;
    }

    /// <summary>The controller time to show at <paramref name="now" />. Never goes back, nor past the newest sample.</summary>
    public double RenderTime(double now, double sendInterval, double minimumDelay)
    {
        double buffer = Math.Max(minimumDelay, sendInterval * BufferIntervals + Math.Min(_jitter * 2, 0.5));
        double target = now - _offset - buffer;
        if (!_playing)
        {
            _playing = true;
            _playTime = target;
        }
        else
        {
            double elapsed = Math.Max(0, now - _lastNow);
            double drift = target - (_playTime + elapsed);
            if (drift > sendInterval * JumpIntervals + buffer)
            {
                _playTime = target;
            }
            else if (drift >= -(sendInterval * JumpIntervals + buffer))
            {
                double speed = drift > sendInterval * DriftIntervals ? CatchUpSpeed :
                    drift < -sendInterval * DriftIntervals ? SlowDownSpeed : 1;
                _playTime += elapsed * speed;
            }
        }

        _lastNow = now;
        return _playTime = Math.Min(_playTime, _lastSent);
    }
}
