// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Clip introspection and PCM access. These decode for real through the native decoder, which needs no
/// audio device, so the numbers here are the decoder's own rather than assumptions about it.
/// </summary>
public class AudioClipDataTests
{
    private static float[] Ramp(int frames, int channels)
    {
        var samples = new float[frames * channels];

        for (int i = 0; i < samples.Length; i++)
            samples[i] = i / (float)samples.Length;

        return samples;
    }

    [Fact]
    public void Create_ReportsTheFormatItWasGiven()
    {
        using var clip = AudioClip.Create("Tone", Ramp(480, 2), channels: 2, sampleRate: 48000);

        Assert.Equal(2, clip.Channels);
        Assert.Equal(48000, clip.SampleRate);
        Assert.Equal(480ul, clip.SampleCount);
        Assert.Equal(0.01f, clip.LengthInSeconds, 5);
    }

    // SampleCount is in frames while the decoder counts total samples, so a stereo clip would read as
    // twice its real length if that conversion were missed.
    [Fact]
    public void SampleCount_IsFramesNotSamples()
    {
        using var mono = AudioClip.Create("Mono", Ramp(100, 1), channels: 1, sampleRate: 44100);
        using var stereo = AudioClip.Create("Stereo", Ramp(100, 2), channels: 2, sampleRate: 44100);

        Assert.Equal(100ul, mono.SampleCount);
        Assert.Equal(100ul, stereo.SampleCount);
        Assert.Equal(mono.LengthInSeconds, stereo.LengthInSeconds, 5);
    }

    [Fact]
    public void GetSampleData_RoundTripsWhatWasCreated()
    {
        float[] samples = Ramp(64, 2);

        using var clip = AudioClip.Create("Round Trip", samples, channels: 2, sampleRate: 44100);
        float[] decoded = clip.GetSampleData();

        Assert.Equal(samples.Length, decoded.Length);

        for (int i = 0; i < samples.Length; i++)
            Assert.Equal(samples[i], decoded[i], 5);
    }

    // A clip with nothing in it must answer rather than throw, since a missing file or a failed
    // import both land here.
    [Fact]
    public void EmptyClip_ReportsNothingRatherThanThrowing()
    {
        using var clip = new AudioClip([1, 2, 3, 4]);

        Assert.Equal(0, clip.Channels);
        Assert.Equal(0, clip.SampleRate);
        Assert.Equal(0ul, clip.SampleCount);
        Assert.Equal(0f, clip.LengthInSeconds);
        Assert.Empty(clip.GetSampleData());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_RejectsAnImpossibleFormat(int channels)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AudioClip.Create("Bad", [0f], channels, 44100));
    }
}
