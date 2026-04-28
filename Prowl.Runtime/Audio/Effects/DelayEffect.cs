// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Runtime.CompilerServices;
using Prowl.Runtime.Audio.Native;
using Prowl.Vector;

namespace Prowl.Runtime.Audio.Effects;

	public sealed class DelayEffect : IAudioEffect
	{
		private Int32 channels;
		private Int32 sampleRate;
		private bool delayStart;       /* Set to true to delay the start of the output; false otherwise. */
		private float wet;                  /* 0..1. Default = 1. */
		private float dry;                  /* 0..1. Default = 1. */
		private float decay;                /* 0..1. Default = 0 (no feedback). Feedback decay. Use this for echo. */
		private Int32 cursor;               /* Feedback is written to this cursor. Always equal or in front of the read cursor. */
		private Int32 bufferSizeInFrames;
		private Int32 actualBufferSize;
		private float[] buffer;
		private readonly object lockObject = new object();

		public float Wet
		{
			get => wet;
			set => wet = value;
		}

		public float Dry
		{
			get => dry;
			set => dry = value;
		}

		public float Decay
		{
			get => decay;
			set
			{
				decay = value;
				delayStart = (decay == 0) ? true : false;
			}
		}

		public UInt32 DelayInFrames
		{
			get => (UInt32)bufferSizeInFrames;
			set
			{
				lock (lockObject)
				{
					bufferSizeInFrames = (Int32)value;

					if (bufferSizeInFrames < 1)
					{
						bufferSizeInFrames = 1;
					}

					actualBufferSize = (Int32)GetNextPowerOfTwo((UInt32)(bufferSizeInFrames * channels));

					if (actualBufferSize > buffer.Length)
					{
						buffer = new float[actualBufferSize];
					}

					cursor = cursor % bufferSizeInFrames;
				}
			}
		}

		public float DelayInSeconds
		{
			get => (float)bufferSizeInFrames / sampleRate;
			set
			{
				lock (lockObject)
				{
					bufferSizeInFrames = (Int32)Maths.Ceiling(value * sampleRate);

					if (bufferSizeInFrames < 1)
					{
						bufferSizeInFrames = 1;
					}

					actualBufferSize = (Int32)GetNextPowerOfTwo((UInt32)(bufferSizeInFrames * channels));

					if (actualBufferSize > buffer.Length)
					{
						buffer = new float[actualBufferSize];
					}

					cursor = cursor % bufferSizeInFrames;
				}
			}
		}

		public DelayEffect(UInt32 sampleRate, UInt32 channels, UInt32 delayInFrames, float decay)
		{
			this.sampleRate = (Int32)sampleRate;
			this.channels = (Int32)channels;
			delayStart = (decay == 0) ? true : false;   /* Delay the start if it looks like we're not configuring an echo. */
			wet = 1.0f;
			dry = 1.0f;
			this.decay = decay;
			bufferSizeInFrames = (Int32)delayInFrames;
			actualBufferSize = (Int32)GetNextPowerOfTwo((UInt32)(bufferSizeInFrames * channels));
			buffer = new float[actualBufferSize];
		}

		public DelayEffect(UInt32 sampleRate, UInt32 channels, float delayInSeconds, float decay)
		{
			this.sampleRate = (Int32)sampleRate;
			this.channels = (Int32)channels;
			delayStart = (decay == 0) ? true : false;   /* Delay the start if it looks like we're not configuring an echo. */
			wet = 1.0f;
			dry = 1.0f;
			this.decay = decay;
			bufferSizeInFrames = (Int32)Maths.Ceiling(delayInSeconds * sampleRate);
			actualBufferSize = (Int32)GetNextPowerOfTwo((UInt32)(bufferSizeInFrames * channels));
			buffer = new float[actualBufferSize];
		}

		public unsafe void OnProcess(NativeArray<float> framesIn, UInt32 frameCountIn, NativeArray<float> framesOut, ref UInt32 frameCountOut, UInt32 channels)
		{
			Int32 iFrame;
			Int32 iChannel;

			float* pFramesOutF32 = (float*)framesOut.Pointer;
			float* pFramesInF32 = (float*)framesIn.Pointer;

			for (iFrame = 0; iFrame < frameCountIn; iFrame += 1)
			{
				for (iChannel = 0; iChannel < this.channels; iChannel += 1)
				{
					Int32 iBuffer = (cursor * this.channels) + iChannel;

					if (delayStart)
					{
						/* Delayed start. */

						/* Read */
						pFramesOutF32[iChannel] = buffer[iBuffer] * wet;

						/* Feedback */
						buffer[iBuffer] = (buffer[iBuffer] * decay) + (pFramesInF32[iChannel] * dry);
					}
					else
					{
						/* Immediate start */

						/* Feedback */
						buffer[iBuffer] = (buffer[iBuffer] * decay) + (pFramesInF32[iChannel] * dry);

						/* Read */
						pFramesOutF32[iChannel] = buffer[iBuffer] * wet;
					}
				}

				cursor = (cursor + 1) % bufferSizeInFrames;

				pFramesOutF32 += this.channels;
				pFramesInF32 += this.channels;
			}
		}

		public void OnDestroy() { }
		
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private UInt32 GetNextPowerOfTwo(UInt32 value)
		{
			value--;
			value |= value >> 1;
			value |= value >> 2;
			value |= value >> 4;
			value |= value >> 8;
			value |= value >> 16;
			value++;
			return value;
		}
	}
