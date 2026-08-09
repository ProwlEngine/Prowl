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

    /// <summary>Runs an effect over interleaved frames and returns what it wrote.</summary>
    private static unsafe float[] Run(IAudioEffect effect, float[] input, int channels = Channels)
    {
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
        var effect = new FilterEffect(FilterType.Lowpass, 500f, 0.707f, 1f);

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
        var effect = new FilterEffect(FilterType.Lowpass, 500f, 0.707f, 1f);

        float[] output = Run(effect, Interleave(1f, 0f));

        for (int i = 0; i < Frames; i++)
            Assert.Equal(0f, output[i * Channels + 1]);

        // The driven channel still has to be doing something, or the assertion above is vacuous.
        Assert.True(output[^2] > 0.5f);
    }

    // Same failure in the phaser, which additionally advanced its sweep once per sample per channel,
    // so a stereo source swept at double the configured rate.
    [Fact]
    public void PhaserEffect_KeepsChannelsIndependent()
    {
        var effect = new PhaserEffect(44100);

        float[] output = Run(effect, Interleave(1f, 0f));

        for (int i = 0; i < Frames; i++)
            Assert.True(Math.Abs(output[i * Channels + 1]) < 1e-6f,
                $"silent channel picked up {output[i * Channels + 1]} at frame {i}");

        Assert.True(Math.Abs(output[^2]) > 0.1f);
    }
}
