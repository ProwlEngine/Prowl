// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Prowl.Runtime;

/// <summary>
/// Paces a loop to a target frame rate. <see cref="NextWait"/> is the whole schedule and takes the
/// time as an argument, so it can be reasoned about and tested without a clock.
/// </summary>
internal sealed class FrameLimiter
{
    private double _deadline;
    private int _targetFrameRate;

    /// <summary>Frames per second to pace to. Zero or less runs unlimited.</summary>
    public int TargetFrameRate
    {
        get => _targetFrameRate;
        set
        {
            value = Math.Max(0, value);
            if (_targetFrameRate == value) return;

            _targetFrameRate = value;
            _deadline = 0.0;
            RaiseTimerResolution(value > 0);
        }
    }

    /// <summary>Forgets the schedule so the next frame starts a fresh one.</summary>
    public void Reset() => _deadline = 0.0;

    /// <summary>Blocks until the next frame is due, which is nothing at all when unlimited.</summary>
    public void Wait()
    {
        long freq = Stopwatch.Frequency;
        double now = Stopwatch.GetTimestamp() / (double)freq;
        double wait = NextWait(now);
        if (wait <= 0.0) return;

        // Sleep lands within a timer tick of what it asks for, so it gives back the bulk of the wait
        // and the last tick's worth is spun out to land on the deadline itself.
        const double SpinSeconds = 0.0015;
        int sleepMs = (int)((wait - SpinSeconds) * 1000.0);
        if (sleepMs > 0) Thread.Sleep(sleepMs);

        double until = now + wait;
        while (Stopwatch.GetTimestamp() / (double)freq < until)
            Thread.SpinWait(64);
    }

    /// <summary>
    /// How long to wait, in seconds, before the next frame may start. Zero means it is already due.
    /// This advances the schedule, so call it exactly once per frame.
    /// </summary>
    public double NextWait(double now)
    {
        if (_targetFrameRate <= 0)
        {
            _deadline = 0.0;
            return 0.0;
        }

        double period = 1.0 / _targetFrameRate;

        // The first frame is what starts the schedule.
        if (_deadline <= 0.0) _deadline = now;

        _deadline += period;

        // The frame that just ended is already past its slot, so the next one runs straight away and
        // the schedule starts again from here. Carrying the debt forward instead would fire the
        // frames it missed back to back.
        if (_deadline <= now)
        {
            _deadline = now;
            return 0.0;
        }

        return _deadline - now;
    }

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint TimeBeginPeriod(uint milliseconds);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static extern uint TimeEndPeriod(uint milliseconds);

    private bool _timerRaised;

    /// <summary>
    /// Windows schedules a sleep against a timer that ticks every 15.6ms by default, which is most
    /// of a frame, so every deadline slept to would be overshot. Held only while a limit is actually
    /// set, since a fine timer costs power.
    /// </summary>
    private void RaiseTimerResolution(bool raised)
    {
        if (raised == _timerRaised) return;
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        try
        {
            if (raised) TimeBeginPeriod(1);
            else TimeEndPeriod(1);
            _timerRaised = raised;
        }
        catch { /* winmm is not there, so the spin carries the whole wait */ }
    }
}
