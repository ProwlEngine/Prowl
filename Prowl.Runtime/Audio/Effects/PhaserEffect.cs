// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using Prowl.Runtime.Audio.Native;

namespace Prowl.Runtime.Audio.Effects;

	public sealed class PhaserEffect : IAudioEffect
	{
		private Phaser phaser;
		
		public float Depth
		{
			get => phaser.Depth;
			set => phaser.Depth = value;
		}

		public float Feedback
		{
			get => phaser.Feedback;
			set => phaser.Feedback = value;
		}

		public float Minimum
		{
			get => phaser.Minimum;
			set => phaser.Minimum = value;
		}

		public float Maximum
		{
			get => phaser.Maximum;
			set => phaser.Maximum = value;
		}

		public float Rate
		{
			get => phaser.Rate;
			set => phaser.Rate = value;
		}

		public PhaserEffect(UInt32 sampleRate)
		{
			phaser = new Phaser();
			phaser.Depth = 1.0f;
			phaser.Feedback = 0.7f;
			phaser.Minimum = 440.0f;
			phaser.Maximum = 1600.0f;
			phaser.Rate = 5.0f;
			phaser.SampleRate = (float)sampleRate;
		}

		public void OnProcess(NativeArray<float> framesIn, uint frameCountIn, NativeArray<float> framesOut, ref uint frameCountOut, uint channels)
		{
        for (UInt32 i = 0; i < frameCountIn; i++)
        {
            for (UInt32 ch = 0; ch < channels; ch++)
            {
                int index = (int)(i * channels + ch);
                framesOut[index] = phaser.Process(framesIn[index]);
            }
        }
		}
		
		public void OnDestroy() {}
	}
