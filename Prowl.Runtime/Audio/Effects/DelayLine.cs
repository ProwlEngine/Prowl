// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Runtime.CompilerServices;

namespace Prowl.Runtime.Audio.Effects;

	public sealed class DelayLine
	{
		private UInt32 inPoint;
		private UInt32 outPoint;
		private float delay;
		private float alpha;
		private float omAlpha;
		private float nextOutput;
		private bool doNextOut;
		private float[] lastFrame;
		private float[] inputs;
		private float gain;

		public float Delay
		{
			get
			{
				return delay;
			}
			set
			{
				if (value + 1 > inputs.Length)
				{ // The value is too big.
					return;
				}

				if (value < 0)
				{
					return;
				}

				delay = value;

				float outPointer = inPoint - delay;  // read chases write

				while (outPointer < 0)
					outPointer += inputs.Length; // modulo maximum length

				outPoint = (UInt32)outPointer;   // integer part

				alpha = outPointer - outPoint; // fractional part
				omAlpha = 1.0f - alpha;

				if (outPoint == inputs.Length)
					outPoint = 0;
				doNextOut = true;
			}
		}

		public UInt32 MaximumDelay
		{
			get => (UInt32)inputs.Length;
			set
			{
				if (value < inputs.Length)
					return;
				inputs = new float[value + 1];
			}
		}

		public DelayLine(float delay = 0.0f, UInt32 maxDelay = 4095)
		{
			if (delay < 0.0f)
				delay = 0.0f;

			if (delay > maxDelay)
				delay = (float)maxDelay;

			inputs = new float[maxDelay + 1];
			lastFrame = new float[2];

			gain = 1.0f;
			inPoint = 0;
			Delay = delay;
			doNextOut = true;
		}

		public void Clear()
		{
			for (int i = 0; i < inputs.Length; i++)
				inputs[i] = 0.0f;
			for (int i = 0; i < lastFrame.Length; i++)
				lastFrame[i] = 0.0f;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public float Tick(float input)
		{
			inputs[inPoint++] = input * gain;

			// Increment input pointer modulo length.
			if (inPoint == inputs.Length)
				inPoint = 0;

			lastFrame[0] = GetNextOut();
			doNextOut = true;

			// Increment output pointer modulo length.
			if (++outPoint == inputs.Length)
				outPoint = 0;

			return lastFrame[0];
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private float GetNextOut()
		{
			if (doNextOut)
			{
				// First 1/2 of interpolation
				nextOutput = inputs[outPoint] * omAlpha;
				// Second 1/2 of interpolation
				if (outPoint + 1 < inputs.Length)
					nextOutput += inputs[outPoint + 1] * alpha;
				else
					nextOutput += inputs[0] * alpha;
				doNextOut = false;
			}

			return nextOutput;
		}
	}
