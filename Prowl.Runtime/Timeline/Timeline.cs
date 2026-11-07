// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

namespace Prowl.Runtime;

/// <summary></summary>
[CreateAssetMenu("Timeline", Extension = ".timeline", Order = 1205)]
public sealed class Timeline : Asset
{
    private TimeSpan _duration;

    public Timeline() : base("Timeline") { }

    /// <summary>
    /// The total duration of the timeline. Assigning the duration sets <see cref="AutoDuration"/> to
    /// <see langword="false"/>.
    /// </summary>
    public TimeSpan Duration
    {
        get => _duration;
        set
        {
            if (AutoDuration == true)
                throw new InvalidOperationException($"Cannot set the duration when {nameof(AutoDuration)} is true.");
            _duration = value;
        }
    }

    /// <summary>
    /// If <see langword="true"/>, the duration of the timeline is based on the last clip. If <see langword="false"/>, the
    /// <see cref="Duration"/> property must be set manually.
    /// </summary>
    public bool AutoDuration
    {
        get;
        set
        {
            field = value;
            if (value)
                SetDurationBasedOnTrackContent();
        }
    }

    /// <summary>
    /// Calculates the duration of the timeline.
    /// </summary>
    /// <returns> The duration of the timeline. </returns>
    private void SetDurationBasedOnTrackContent()
    {
        //TimeSpan result = TimeSpan.Zero;
        //foreach (var track in Tracks)
        //{
        //    if (track.Duration > result)
        //        result = track.Duration;
        //}
        //return result;
    }

    /// <summary>
    /// Indicates whether the timeline is currently playing.
    /// </summary>
    public bool IsPlaying { get; set; }

    /// <summary>
    /// 
    /// </summary>
    public float PlaybackOffset { get; set; }

    /// <summary>
    /// A list of tracks.
    /// </summary>
    public List<TimelineTrack> Tracks { get; } = [];

    public void DuplicateTrack(TimelineTrack track)
    {
        //if (track.Timeline != this)
        //    throw new ArgumentException("The track to duplicate must have been created by this timeline.", nameof(track));
        throw new NotImplementedException();
    }

    public void DeleteTrack(TimelineTrack track)
    {
        //if (track.Timeline != this)
        //    throw new ArgumentException("The track to delete must have been created by this timeline.", nameof(track));
        Tracks.Remove(track);
    }
}
