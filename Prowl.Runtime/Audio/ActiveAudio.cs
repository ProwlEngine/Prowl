// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Runtime.Audio;

public delegate void AudioSourcePlaybackCompletedHandler(ActiveAudio source);

public enum AudioPositionKind { AbsoluteWorld, ListenerRelative }

public abstract class ActiveAudio : IDisposable
{
    public abstract float Gain { get; set; }
    public abstract float Pitch { get; set; }
    public abstract float MaxDistance { get; set; }
    public abstract bool Looping { get; set; }
    public abstract Vector3 Position { get; set; }
    public abstract Vector3 Direction { get; set; }
    public abstract AudioPositionKind PositionKind { get; set; }
    public abstract void Dispose();
    public abstract void Play(AudioBuffer buffer);
    public abstract void Stop();
    public abstract float PlaybackPosition { get; set; }
    public abstract bool IsPlaying { get; }
}
