// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using Prowl.Runtime.Audio.Native;

namespace Prowl.Runtime.Audio.Effects;

public sealed class ReverbEffect: IAudioEffect
{
    private Reverb reverb;
		
		public float RoomSize
		{
			get => reverb.RoomSize;
			set => reverb.RoomSize = value;
		}

		public float Damping
		{
			get => reverb.Damping;
			set => reverb.Damping = value;
		}

		public float Wet
		{
			get => reverb.Wet;
			set => reverb.Wet = value;
		}

		public float Dry
		{
			get => reverb.Dry;
			set => reverb.Dry = value;
		}

		public float Width
		{
			get => reverb.Width;
			set => reverb.Width = value;
		}

		public float InputWidth
		{
			get => reverb.InputWidth;
			set => reverb.InputWidth = value;
		}

		public float Mode
		{
			get => reverb.Mode;
			set => reverb.Mode = value;
		}

		public UInt64 DecayTimeInFrames
		{
			get => reverb.DecayTimeInFrames;
		}

    public ReverbEffect(UInt32 sampleRate, UInt32 channels)
		{
			reverb = new Reverb(sampleRate, channels);
		}
		
		public void OnProcess(NativeArray<float> framesIn, UInt32 frameCountIn, NativeArray<float> framesOut, ref UInt32 frameCountOut, UInt32 channels)
		{
			reverb.Process(framesIn, framesOut, frameCountIn);
		}

    public void OnDestroy() { }
	}
