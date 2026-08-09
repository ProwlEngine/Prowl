// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Runtime.CompilerServices;
using Prowl.Runtime.Audio.Native;
using Prowl.Vector;

namespace Prowl.Runtime.Audio.Effects;

	public sealed class DistortionEffect : IAudioEffect
	{
    private float drive;
    private float range;
    private float blend;
    private float volume;

		public float Drive
		{
			get => drive;
			set => drive = value;
		}

		public float Range
		{
			get => range;
			set => range = value;
		}

		public float Blend
		{
			get => blend;
			set => blend = value;
		}

		public float Volume
		{
			get => volume;
			set => volume = value;
		}

		public DistortionEffect()
		{
			drive = 1.0f;
			range = 1.0f;
			blend = 1.0f;
			volume = 1.0f;
		}

		public void OnProcess(NativeArray<float> framesIn, UInt32 frameCountIn, NativeArray<float> framesOut, ref UInt32 frameCountOut, UInt32 channels)
		{
			int count = (int)(frameCountIn * channels);

			for (int i = 0; i < count; i++)
			{
				framesOut[i] = Distort(framesIn[i], drive, range, blend, volume);
			}
		}

		// The blend crossfades the shaped signal against the untouched one, so the dry end of it has to
		// come out at the level it went in. The halving that used to sit outside the blend took 6 dB
		// off the signal even with the effect fully dry.
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private float Distort(float x, float drive, float range, float blend, float volume)
		{
			float xClean = x;
			x *= drive * range;
			return ((((2.0f / Maths.PI) * Maths.Atan(x)) * blend) + (xClean * (1.0f - blend))) * volume;
		}
		
		public void OnDestroy() { }
	}
