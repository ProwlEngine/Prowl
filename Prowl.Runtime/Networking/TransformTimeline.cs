// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Runtime.Networking;

/// <summary>Maps local render time to a controller's monotonic clock without changing snapshot spacing.</summary>
internal sealed class TransformTimeline
{
    private bool _initialized;
    private double _offset;
    private double _lastSent;
    private double _lastArrival;
    private double _jitter;
    private double _lastRender = double.NegativeInfinity;

    public void Clear()
    {
        _initialized = false;
        _jitter = 0;
        _lastRender = double.NegativeInfinity;
    }

    public bool Observe(double sent, double arrival)
    {
        if (_initialized && sent <= _lastSent)
            return false;
        double offset = arrival - sent;
        if (!_initialized)
        {
            _initialized = true;
            _offset = offset;
        }
        else
        {
            double variation = Math.Abs((arrival - _lastArrival) - (sent - _lastSent));
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

    public double RenderTime(double now, double delay)
    {
        double extraDelay = delay > 0 ? Math.Min(_jitter * 2, 0.5) : 0;
        double target = Math.Min(_lastSent, now - _offset - Math.Max(0, delay) - extraDelay);
        // Increasing the jitter buffer may pause playback briefly, but must never rewind it.
        return _lastRender = Math.Max(_lastRender, target);
    }
}
