// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.PaperUI;

namespace Prowl.Runtime;

/// <summary>
/// A track in a <see cref="Timeline"/>. Each track can manipulate a specific aspect of the game over a period of time.
/// </summary>
public abstract class TimelineTrack
{
    /// <summary>
    /// The name of the track.
    /// </summary>
    public string Name = "New Track";

    /// <summary>
    /// Indicates whether the track is enabled. Disabled tracks are ignored during playback.
    /// </summary>
    public bool Enabled = true;

    /// <summary>
    /// Indicates whether the track should be protected from modifications.
    /// </summary>
    public bool IsReadOnly = false;

    /// <summary>
    /// Draws the UI for the track header (on the left).
    /// </summary>
    /// <param name="paper">The GUI context.</param>
    public virtual void OnTrackHeaderGUI(Paper paper)
    {
    }

    /// <summary>
    /// Draws the UI for the track (the main area).
    /// </summary>
    /// <param name="paper">The GUI context.</param>
    /// <param name="timelineStart">The start time of the timeline, in seconds.</param>
    /// <param name="timelineEnd">The end time of the timeline, in seconds.</param>
    /// <param name="pixelsPerSecond">The number of pixels per second.</param>
    public virtual void OnTrackGUI(Paper paper, float timelineStart, float timelineEnd, float pixelsPerSecond)
    {
    }
}

/// <summary>
/// A track in a <see cref="Timeline"/>. Each track can manipulate a specific aspect of the game over a period of time.
/// </summary>
public abstract class TimelineTrack<TClip> : TimelineTrack where TClip : TimelineClip
{
    private List<TClip> _clips { get; } = [];

    /// <summary>
    /// Gets the list of clips associated with the track.
    /// </summary>
    public IReadOnlyList<TClip> Clips => _clips;

    /// <summary>
    /// Adds a clip to the track.
    /// </summary>
    /// <param name="clip">The clip to play.</param>
    public void AddClip(TClip clip)
    {
        // Returns the index of the item, if it was found, or the bitwise complement otherwise.
        int index = _clips.BinarySearch(clip);
        _clips.Insert(index < 0 ? ~index : index, clip);
    }
}
