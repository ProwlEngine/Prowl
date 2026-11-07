// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Runtime.Resources;

namespace Prowl.Runtime;

public sealed class TimelineAudioClip : TimelineClip
{
    /// <summary>
    /// The audio clip to play.
    /// </summary>
    [field: SerializeField, Tooltip("The audio to play.")]
    public required AudioClip Audio;

    /// <summary>
    /// Whether the audio clip should be looped.
    /// </summary>
    [field: SerializeField, Tooltip("Indicates whether the clip should be looped.")]
    public bool Loop;

    /// <summary>
    /// The volume of the clip, as a fraction between 0 and 1.
    /// </summary>
    [field: SerializeField, Range(0, 1), Tooltip("The volume of the clip.")]
    public float Volume = 1.0f;
}

/// <summary>
/// A track that plays audio clips in a <see cref="Timeline"/>.
/// </summary>
public sealed class TimelineAudioTrack : TimelineTrack<TimelineAudioClip>
{
    /// <summary>
    /// The volume of the track, as a fraction between 0 and 1.
    /// </summary>
    [field: SerializeField, Range(0, 1), Tooltip("The overall volume of the track.")]
    public float Volume = 1.0f;
}
