// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.Runtime.Audio.Null;

public class NullAudioSource : ActiveAudio
{
    public override Double3 Direction { get; set; }

    public override float Gain { get; set; }
    public override float Pitch { get; set; }
    public override float MaxDistance { get; set; }
    public override bool Looping { get; set; }

    public override Double3 Position { get; set; }

    public override AudioPositionKind PositionKind { get; set; }

    public override float PlaybackPosition { get { return 1f; } set { } }

    public override bool IsPlaying => false;

    public override void Dispose()
    {
    }

    public override void Play(AudioBuffer buffer)
    {
    }

    public override void Stop()
    {
    }
}
