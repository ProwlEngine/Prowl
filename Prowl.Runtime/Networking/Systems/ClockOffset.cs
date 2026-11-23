// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Runtime.Networking;

/// <summary>
///     How far behind this peer's clock another's runs: the least delay seen from it lately. It forgets what it saw
///     after two windows, so the clocks may drift, and one message claiming an impossible time can't set it for good.
/// </summary>
internal struct ClockOffset
{
    internal const double WindowSeconds = 5;

    private double _current, _previous, _windowStart;
    private bool _started;

    /// <summary>Takes a message sent at <paramref name="sentAt" /> on the other clock and received at <paramref name="now" /> on this one.</summary>
    internal double Take(double now, double sentAt)
    {
        double delay = now - sentAt;
        if (!_started)
        {
            (_current, _previous, _windowStart, _started) = (delay, delay, now, true);
        }
        else if (now - _windowStart >= 2 * WindowSeconds)
        {
            (_previous, _current, _windowStart) = (delay, delay, now);
        }
        else if (now - _windowStart >= WindowSeconds)
        {
            (_previous, _current, _windowStart) = (_current, delay, now);
        }
        else
        {
            _current = Math.Min(_current, delay);
        }

        return Math.Min(_current, _previous);
    }
}
