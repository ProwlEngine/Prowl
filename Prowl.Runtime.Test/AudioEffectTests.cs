// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Audio.Effects;
using Prowl.Runtime.Audio.Native;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Effects run on the audio thread over interleaved frames, so the things worth pinning are the ones
/// that are inaudible in a unit sense but obvious in a listening sense: that an effect writes its
/// output at all, and that one channel cannot leak into another.
/// </summary>
public class AudioEffectTests
{
    private const int Channels = 2;
    private const int Frames = 512;

    /// <summary>Binds an effect to a stereo 44100 chain and runs it over interleaved frames.</summary>
    private static unsafe float[] Run(AudioEffect effect, float[] input, int channels = Channels)
    {
        if (!effect.IsInitialized)
            effect.Initialize(44100, channels);

        float[] output = new float[input.Length];

        fixed (float* pIn = input, pOut = output)
        {
            var framesIn = new NativeArray<float>(pIn, input.Length);
            var framesOut = new NativeArray<float>(pOut, output.Length);
            uint frameCount = (uint)(input.Length / channels);
            uint frameCountOut = frameCount;
            effect.OnProcess(framesIn, frameCount, framesOut, ref frameCountOut, (uint)channels);
        }

        return output;
    }

    private static float[] Interleave(float left, float right, int frames = Frames)
    {
        float[] buffer = new float[frames * Channels];

        for (int i = 0; i < frames; i++)
        {
            buffer[i * Channels] = left;
            buffer[i * Channels + 1] = right;
        }

        return buffer;
    }

    // Filter.Process declared its output first and FilterEffect passed the input first, so the filter
    // read the output buffer and wrote the input one. The effect was audibly a no-op.
    [Fact]
    public void FilterEffect_WritesItsOutput()
    {
        var effect = new FilterEffect { Type = FilterType.Lowpass, Frequency = 500f, Q = 0.707f };

        float[] output = Run(effect, Interleave(1f, 1f));

        // A lowpass passes DC, so a constant input settles at the same constant.
        Assert.Equal(1f, output[^1], 3);
        Assert.Equal(1f, output[^2], 3);
    }

    // One biquad state pair shared by every channel is not a stereo filter, it is one filter fed two
    // interleaved signals. A silent channel is the clearest way to see the other one bleeding in.
    [Fact]
    public void FilterEffect_KeepsChannelsIndependent()
    {
        var effect = new FilterEffect { Type = FilterType.Lowpass, Frequency = 500f, Q = 0.707f };

        float[] output = Run(effect, Interleave(1f, 0f));

        for (int i = 0; i < Frames; i++)
            Assert.Equal(0f, output[i * Channels + 1]);

        // The driven channel still has to be doing something, or the assertion above is vacuous.
        Assert.True(output[^2] > 0.5f);
    }

    // A zero length delay allocated a zero length buffer and then took the cursor modulo zero on the
    // audio thread. One frame is the floor.
    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(1f / 44100f)]
    public void DelayEffect_ZeroLengthDelay_ClampsToOneFrame(float seconds)
    {
        var effect = new DelayEffect { DelayInSeconds = seconds, Decay = 0f };

        float[] output = Run(effect, Interleave(1f, 1f));

        Assert.Equal(1u, effect.DelayInFrames);
        Assert.Equal(0f, output[0]);
        Assert.Equal(1f, output[^1], 4);
    }

    // The blend is a crossfade against the untouched signal, so at fully dry the effect has to be
    // unity gain. A halving outside the blend took 6 dB off just for having the effect in the chain.
    [Fact]
    public void DistortionEffect_FullyDry_IsUnityGain()
    {
        var effect = new DistortionEffect { Blend = 0f };

        float[] output = Run(effect, Interleave(0.5f, -0.25f));

        Assert.Equal(0.5f, output[0], 5);
        Assert.Equal(-0.25f, output[1], 5);
    }

    // Same failure in the phaser, which additionally advanced its sweep once per sample per channel,
    // so a stereo source swept at double the configured rate.
    [Fact]
    public void PhaserEffect_KeepsChannelsIndependent()
    {
        var effect = new PhaserEffect();

        float[] output = Run(effect, Interleave(1f, 0f));

        for (int i = 0; i < Frames; i++)
            Assert.True(Math.Abs(output[i * Channels + 1]) < 1e-6f,
                $"silent channel picked up {output[i * Channels + 1]} at frame {i}");

        Assert.True(Math.Abs(output[^2]) > 0.1f);
    }
}
