// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

//  Verblib version 0.5 - 2022-10-25
//
//  Philip Bennefall - philip@blastbay.com
//
//  This reverb is based on Freeverb, a public domain reverb written by Jezar at Dreampoint.
//
//  IMPORTANT: The reverb currently only works with 1 or 2 channels, at sample rates of 22050 HZ and above.

using System;
using System.Runtime.CompilerServices;
using Prowl.Runtime.Audio.Native;
using Prowl.Vector;

namespace Prowl.Runtime.Audio.Effects;

	public class Reverb
	{
		private const int MAX_SAMPLERATE_MULTIPLIER = 4;
		private const float SILENCE_THRESHOLD = 80.0f; /* In dB (absolute). */
		private const int NUM_COMBS = 8;
		private const int NUM_ALLPASSES = 4;
		private const float MUTED = 0.0f;
		private const float FIXED_GAIN = 0.015f;
		private const float SCALE_WET = 3.0f;
		private const float SCALE_DRY = 2.0f;
		private const float SCALE_DAMP = 0.8f;
		private const float SCALE_ROOM = 0.28f;
		private const float OFFSET_ROOM = 0.7f;
		private const float INITIAL_ROOM = 0.5f;
		private const float INITIAL_DAMP = 0.25f;
		private const float INITIAL_WET = 1.0f / SCALE_WET;
		private const float INITIAL_DRY = 0.0f;
		private const float INITIAL_WIDTH = 1.0f;
		private const float INITIAL_INPUT_WIDTH = 0.0f;
		private const float INITIAL_MODE = 0.0f;
		private const float FREEZE_MODE = 0.5f;
		private const int STEREO_SPREAD = 23;

		private const int COMBTUNING_L1 = 1116;
		private const int COMBTUNING_R1 = (1116 + STEREO_SPREAD);
		private const int COMBTUNING_L2 = 1188;
		private const int COMBTUNING_R2 = (1188 + STEREO_SPREAD);
		private const int COMBTUNING_L3 = 1277;
		private const int COMBTUNING_R3 = (1277 + STEREO_SPREAD);
		private const int COMBTUNING_L4 = 1356;
		private const int COMBTUNING_R4 = (1356 + STEREO_SPREAD);
		private const int COMBTUNING_L5 = 1422;
		private const int COMBTUNING_R5 = (1422 + STEREO_SPREAD);
		private const int COMBTUNING_L6 = 1491;
		private const int COMBTUNING_R6 = (1491 + STEREO_SPREAD);
		private const int COMBTUNING_L7 = 1557;
		private const int COMBTUNING_R7 = (1557 + STEREO_SPREAD);
		private const int COMBTUNING_L8 = 1617;
		private const int COMBTUNING_R8 = (1617 + STEREO_SPREAD);
		private const int ALLPASSTUNING_L1 = 556;
		private const int ALLPASSTUNING_R1 = (556 + STEREO_SPREAD);
		private const int ALLPASSTUNING_L2 = 441;
		private const int ALLPASSTUNING_R2 = (441 + STEREO_SPREAD);
		private const int ALLPASSTUNING_L3 = 341;
		private const int ALLPASSTUNING_R3 = (341 + STEREO_SPREAD);
		private const int ALLPASSTUNING_L4 = 225;
		private const int ALLPASSTUNING_R4 = (225 + STEREO_SPREAD);

		private UInt32 channels;
		private float gain;
		private float roomsize, roomsize1;
		private float damp, damp1;
		private float wet, wet1, wet2;
		private float dry;
		private float width;
		private float input_width;
		private float mode;

		private CombFilter[] combL;
		private CombFilter[] combR;

		private AllPassFilter[] allpassL;
		private AllPassFilter[] allpassR;

		public float RoomSize
		{
			get
			{
				return (roomsize - OFFSET_ROOM) / SCALE_ROOM;
			}
			set
			{
				roomsize = (value * SCALE_ROOM) + OFFSET_ROOM;
				Update();
			}
		}

		public float Damping
		{
			get
			{
				return damp / SCALE_DAMP;
			}
			set
			{
				damp = value * SCALE_DAMP;
				Update();
			}
		}

		public float Wet
		{
			get
			{
				return wet / SCALE_WET;
			}
			set
			{
				wet = value * SCALE_WET;
				Update();
			}
		}

		public float Dry
		{
			get
			{
				return dry / SCALE_DRY;
			}
			set
			{
				dry = value * SCALE_DRY;
			}
		}

		public float Width
		{
			get
			{
				return width;
			}
			set
			{
				width = value;
				Update();
			}
		}

		public float InputWidth
		{
			get
			{
				return input_width;
			}
			set
			{
				input_width = value;
			}
		}

		public float Mode
		{
			get
			{
				if (mode >= FREEZE_MODE)
				{
					return 1.0f;
				}
				return 0.0f;
			}
			set
			{
				mode = value;
				Update();
			}
		}

		public UInt64 DecayTimeInFrames
		{
			get
			{
				float decay;

				if (mode >= FREEZE_MODE)
				{
					return 0; /* Freeze mode creates an infinite decay. */
				}

				decay = SILENCE_THRESHOLD / Maths.Abs(-20.0f * Maths.Log(1.0f / roomsize1));
				decay *= (float)(combR[7].bufsize * 2);
				return (UInt64)decay;
			}
		}

		public Reverb(UInt32 sampleRate, UInt32 channels)
		{
			if (channels != 1 && channels != 2)
			{
				throw new ArgumentException("Currently supports only 1 or 2 channels");
			}
			if (sampleRate < 22050)
			{
				throw new ArgumentException("The minimum supported sample rate is 22050 HZ");
			}
			else if (sampleRate > 44100 * MAX_SAMPLERATE_MULTIPLIER)
			{
				throw new ArgumentException("The sample rate is too high");
			}

			this.channels = channels;

			combL = new CombFilter[NUM_COMBS];
			combR = new CombFilter[NUM_COMBS];

			/* Allpass filters */
			allpassL = new AllPassFilter[NUM_ALLPASSES];
			allpassR = new AllPassFilter[NUM_ALLPASSES];

			/* Tie the components to their buffers. */
			combL[0].Initialize(COMBTUNING_L1 * MAX_SAMPLERATE_MULTIPLIER, GetScaledBufferSize(sampleRate, COMBTUNING_L1));
			combR[0].Initialize(COMBTUNING_R1 * MAX_SAMPLERATE_MULTIPLIER, GetScaledBufferSize(sampleRate, COMBTUNING_R1));
			combL[1].Initialize(COMBTUNING_L2 * MAX_SAMPLERATE_MULTIPLIER, GetScaledBufferSize(sampleRate, COMBTUNING_L2));
			combR[1].Initialize(COMBTUNING_R2 * MAX_SAMPLERATE_MULTIPLIER, GetScaledBufferSize(sampleRate, COMBTUNING_R2));
			combL[2].Initialize(COMBTUNING_L3 * MAX_SAMPLERATE_MULTIPLIER, GetScaledBufferSize(sampleRate, COMBTUNING_L3));
			combR[2].Initialize(COMBTUNING_R3 * MAX_SAMPLERATE_MULTIPLIER, GetScaledBufferSize(sampleRate, COMBTUNING_R3));
			combL[3].Initialize(COMBTUNING_L4 * MAX_SAMPLERATE_MULTIPLIER, GetScaledBufferSize(sampleRate, COMBTUNING_L4));
			combR[3].Initialize(COMBTUNING_R4 * MAX_SAMPLERATE_MULTIPLIER, GetScaledBufferSize(sampleRate, COMBTUNING_R4));
			combL[4].Initialize(COMBTUNING_L5 * MAX_SAMPLERATE_MULTIPLIER, GetScaledBufferSize(sampleRate, COMBTUNING_L5));
			combR[4].Initialize(COMBTUNING_R5 * MAX_SAMPLERATE_MULTIPLIER, GetScaledBufferSize(sampleRate, COMBTUNING_R5));
			combL[5].Initialize(COMBTUNING_L6 * MAX_SAMPLERATE_MULTIPLIER, GetScaledBufferSize(sampleRate, COMBTUNING_L6));
			combR[5].Initialize(COMBTUNING_R6 * MAX_SAMPLERATE_MULTIPLIER, GetScaledBufferSize(sampleRate, COMBTUNING_R6));
			combL[6].Initialize(COMBTUNING_L7 * MAX_SAMPLERATE_MULTIPLIER, GetScaledBufferSize(sampleRate, COMBTUNING_L7));
			combR[6].Initialize(COMBTUNING_R7 * MAX_SAMPLERATE_MULTIPLIER, GetScaledBufferSize(sampleRate, COMBTUNING_R7));
			combL[7].Initialize(COMBTUNING_L8 * MAX_SAMPLERATE_MULTIPLIER, GetScaledBufferSize(sampleRate, COMBTUNING_L8));
			combR[7].Initialize(COMBTUNING_R8 * MAX_SAMPLERATE_MULTIPLIER, GetScaledBufferSize(sampleRate, COMBTUNING_R8));

			allpassL[0].Initialize(ALLPASSTUNING_L1 * MAX_SAMPLERATE_MULTIPLIER, GetScaledBufferSize(sampleRate, ALLPASSTUNING_L1));
			allpassR[0].Initialize(ALLPASSTUNING_R1 * MAX_SAMPLERATE_MULTIPLIER, GetScaledBufferSize(sampleRate, ALLPASSTUNING_R1));
			allpassL[1].Initialize(ALLPASSTUNING_L2 * MAX_SAMPLERATE_MULTIPLIER, GetScaledBufferSize(sampleRate, ALLPASSTUNING_L2));
			allpassR[1].Initialize(ALLPASSTUNING_R2 * MAX_SAMPLERATE_MULTIPLIER, GetScaledBufferSize(sampleRate, ALLPASSTUNING_R2));
			allpassL[2].Initialize(ALLPASSTUNING_L3 * MAX_SAMPLERATE_MULTIPLIER, GetScaledBufferSize(sampleRate, ALLPASSTUNING_L3));
			allpassR[2].Initialize(ALLPASSTUNING_R3 * MAX_SAMPLERATE_MULTIPLIER, GetScaledBufferSize(sampleRate, ALLPASSTUNING_R3));
			allpassL[3].Initialize(ALLPASSTUNING_L4 * MAX_SAMPLERATE_MULTIPLIER, GetScaledBufferSize(sampleRate, ALLPASSTUNING_L4));
			allpassR[3].Initialize(ALLPASSTUNING_R4 * MAX_SAMPLERATE_MULTIPLIER, GetScaledBufferSize(sampleRate, ALLPASSTUNING_R4));

			/* Set default values. */
			for (int i = 0; i < NUM_ALLPASSES; i++)
			{
				allpassL[i].feedback = 0.5f;
				allpassR[i].feedback = 0.5f;
			}

			Wet = INITIAL_WET;
			RoomSize = INITIAL_ROOM;
			Dry = INITIAL_DRY;
			Damping = INITIAL_DAMP;
			Width = INITIAL_WIDTH;
			InputWidth = INITIAL_INPUT_WIDTH;
			Mode = INITIAL_MODE;

			/* The buffers will be full of rubbish - so we MUST mute them. */
			Mute();
		}

		public void Process(NativeArray<float> inputBuffer, NativeArray<float> outputBuffer, UInt32 frames)
		{
			unsafe
			{
				float* pInput = (float*)inputBuffer.Pointer;
				float* pOutput = (float*)outputBuffer.Pointer;
				Process(pInput, pOutput, frames);
			}
		}

		public unsafe void Process(float* inputBuffer, float* outputBuffer, UInt32 frames)
		{
			int i;
			float outL, outR, input;

			if (channels == 1)
			{
				while (frames-- > 0)
				{
					outL = 0.0f;
					input = (inputBuffer[0] * 2.0f) * gain;

					/* Accumulate comb filters in parallel. */
					for (i = 0; i < NUM_COMBS; i++)
					{
						outL += combL[i].Process(input);
					}

					/* Feed through allpasses in series. */
					for (i = 0; i < NUM_ALLPASSES; i++)
					{
						outL = allpassL[i].Process(outL);
					}

					/* Calculate output REPLACING anything already there. */
					outputBuffer[0] = outL * wet1 + inputBuffer[0] * dry;

					/* Increment sample pointers. */
					++inputBuffer;
					++outputBuffer;
				}
			}
			else if (channels == 2)
			{
				if (input_width > 0.0f) /* Stereo input is widened or narrowed. */
				{

					/*
					* The stereo mid/side code is derived from:
					* https://www.musicdsp.org/en/latest/Effects/256-stereo-width-control-obtained-via-transfromation-matrix.html
					* The description of the code on the above page says:
					*
					* This work is hereby placed in the public domain for all purposes, including
					* use in commercial applications.
					*/

					float tmp = 1.0f / (float)Maths.Max(1 + input_width, 2);
					float coef_mid = 1 * tmp;
					float coef_side = input_width * tmp;

					while (frames-- > 0)
					{
						float mid = (inputBuffer[0] + inputBuffer[1]) * coef_mid;
						float side = (inputBuffer[1] - inputBuffer[0]) * coef_side;
						float input_left = (mid - side) * (gain * 2.0f);
						float input_right = (mid + side) * (gain * 2.0f);

						outL = outR = 0.0f;

						/* Accumulate comb filters in parallel. */
						for (i = 0; i < NUM_COMBS; i++)
						{
							outL += combL[i].Process(input_left);
							outR += combR[i].Process(input_right);
						}

						/* Feed through allpasses in series. */
						for (i = 0; i < NUM_ALLPASSES; i++)
						{
							outL = allpassL[i].Process(outL);
							outR = allpassR[i].Process(outR);
						}

						/* Calculate output REPLACING anything already there. */
						outputBuffer[0] = outL * wet1 + outR * wet2 + inputBuffer[0] * dry;
						outputBuffer[1] = outR * wet1 + outL * wet2 + inputBuffer[1] * dry;

						/* Increment sample pointers. */
						inputBuffer += 2;
						outputBuffer += 2;
					}
				}
				else /* Stereo input is summed to mono. */
				{
					while (frames-- > 0)
					{
						outL = outR = 0.0f;
						input = (inputBuffer[0] + inputBuffer[1]) * gain;

						/* Accumulate comb filters in parallel. */
						for (i = 0; i < NUM_COMBS; i++)
						{
							outL += combL[i].Process(input);
							outR += combR[i].Process(input);
						}

						/* Feed through allpasses in series. */
						for (i = 0; i < NUM_ALLPASSES; i++)
						{
							outL = allpassL[i].Process(outL);
							outR = allpassR[i].Process(outR);
						}

						/* Calculate output REPLACING anything already there. */
						outputBuffer[0] = outL * wet1 + outR * wet2 + inputBuffer[0] * dry;
						outputBuffer[1] = outR * wet1 + outL * wet2 + inputBuffer[1] * dry;

						/* Increment sample pointers. */
						inputBuffer += 2;
						outputBuffer += 2;
					}
				}
			}
		}

		private void Update()
		{
			/* Recalculate internal values after parameter change. */
			int i;

			wet1 = wet * (width / 2.0f + 0.5f);
			wet2 = wet * ((1.0f - width) / 2.0f);

			if (mode >= FREEZE_MODE)
			{
				roomsize1 = 1.0f;
				damp1 = 0.0f;
				gain = MUTED;
			}
			else
			{
				roomsize1 = roomsize;
				damp1 = damp;
				gain = FIXED_GAIN;
			}

			for (i = 0; i < NUM_COMBS; i++)
			{
				combL[i].feedback = roomsize1;
				combR[i].feedback = roomsize1;
				combL[i].SetDamp(damp1);
				combR[i].SetDamp(damp1);
			}
		}

		private void Mute()
		{
			int i;
			if (Mode >= FREEZE_MODE)
			{
				return;
			}

			for (i = 0; i < NUM_COMBS; i++)
			{
				combL[i].Mute();
				combR[i].Mute();
			}
			for (i = 0; i < NUM_ALLPASSES; i++)
			{
				allpassL[i].Mute();
				allpassR[i].Mute();
			}
		}

		private int GetScaledBufferSize(UInt32 sampleRate, UInt32 value)
		{
			float result = (float)sampleRate;
			result /= 44100.0f;
			result = (float)value * result;
			if (result < 1.0)
			{
				result = 1.0f;
			}
			return (int)result;
		}

		private struct AllPassFilter
		{
			public float[] buffer;
			public float feedback;
			public int bufsize;
			public int bufidx;

			public void Initialize(int bufferSize, int size)
			{
				buffer = new float[bufferSize];
				bufsize = size;
				bufidx = 0;
			}

			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			public float Process(float input)
			{
				float output;
				float bufout;

				bufout = buffer[bufidx];
				bufout = Undenormalize(bufout);

				output = -input + bufout;
				buffer[bufidx] = input + (bufout * feedback);

				if (++bufidx >= bufsize)
				{
					bufidx = 0;
				}

				return output;
			}

			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			private float Undenormalize(float sample)
			{
				sample += 1.0f;
				sample -= 1.0f;
				return sample;
			}

			public void Mute()
			{
				int i;
				for (i = 0; i < bufsize; i++)
				{
					buffer[i] = 0.0f;
				}
			}
		}

		private struct CombFilter
		{
			public float[] buffer;
			public float feedback;
			public float filterstore;
			public float damp1;
			public float damp2;
			public int bufsize;
			public int bufidx;

			public void Initialize(int bufferSize, int size)
			{
				buffer = new float[bufferSize];
				bufsize = size;
				filterstore = 0.0f;
				bufidx = 0;
			}

			public void Mute()
			{
				int i;
				for (i = 0; i < bufsize; i++)
				{
					buffer[i] = 0.0f;
				}
			}

			public void SetDamp(float val)
			{
				damp1 = val;
				damp2 = 1.0f - val;
			}

			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			public float Process(float input)
			{
				float output;

				output = buffer[bufidx];
				output = Undenormalize(output);

				filterstore = (output * damp2) + (filterstore * damp1);
				filterstore = Undenormalize(filterstore);

				buffer[bufidx] = input + (filterstore * feedback);

				if (++bufidx >= bufsize)
				{
					bufidx = 0;
				}

				return output;
			}

			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			private float Undenormalize(float sample)
			{
				sample += 1.0f;
				sample -= 1.0f;
				return sample;
			}
		}
	}
