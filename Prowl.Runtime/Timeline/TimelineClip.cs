// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Runtime;

public abstract class TimelineClip : IComparable<TimelineClip>
{
    /// <summary>
    /// The time to start playing the clip, in seconds.
    /// </summary>
    [Tooltip("The time to start playing the clip, in seconds.")]
    public double StartTime;

    /// <summary>
    /// The time the clip ends, in seconds.
    /// </summary>
    public double EndTime => StartTime + Duration;

    [Tooltip("The length of the clip, in seconds. This can be different from the audio length.")]
    public double Duration;

    [Tooltip("The length of time, in seconds, in which to ramp up the volume at the start of the clip. Ignored if this clip is overlapping with a prior clip.")]
    public double EaseInDuration;

    [Tooltip("The length of time, in seconds, in which to ramp down the volume at the end of the clip. Ignored if this clip is overlapping with a subsequent clip.")]
    public double EaseOutDuration;

    /// <summary>
    /// Speeds up or slows down playback of the audio.
    /// </summary>
    public float SpeedMultiplier = 1.0f;

    /// <inheritdoc/>
    public int CompareTo(TimelineClip? other)
    {
        if (other == null) return 1;
        return StartTime.CompareTo(other.StartTime);
    }
}
