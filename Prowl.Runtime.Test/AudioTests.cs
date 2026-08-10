// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Runtime.Audio;
using Prowl.Runtime.Audio.Effects;
using Prowl.Runtime.Audio.Native;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Everything audio that can be checked without an output device: buffers, clip data, serialization,
/// the mixer tree, the effect DSP, and how the components behave when there is no device at all.
/// </summary>
/// <remarks>
/// Two things are worth knowing before adding to this. The native library loads in the test process,
/// so anything that only decodes (clip introspection, import conversion) runs for real rather than
/// against a stub. Anything that needs a device does not: no context is ever initialized here, which
/// is exactly the headless case the components have to survive, so those tests assert inertness.
///
/// Derives from RuntimeTestBase for the scene and play mode setup the component tests need. The pure
/// data tests do not care either way.
/// </remarks>
public class AudioTests : RuntimeTestBase
{
    private const int Channels = 2;
    private const int Frames = 512;
    private const int TestSampleRate = 44100;

    #region Helpers

    /// <summary>An effect that only records whether its owner destroyed it.</summary>
    private sealed class CountingEffect : AudioEffect
    {
        public int Destroyed;

        protected override void OnProcess(NativeArray<float> framesIn, uint frameCountIn, NativeArray<float> framesOut, ref uint frameCountOut, uint channels) { }
        public override void OnDestroy() => Destroyed++;
    }

    private AudioSource CreateSource()
    {
        var scene = CreateScene(enable: true);
        var go = CreateGameObject("Speaker");
        var source = go.AddComponent<AudioSource>();
        scene.Add(go);
        return source;
    }

    /// <summary>Binds an effect to a stereo chain and runs it over interleaved frames.</summary>
    private static unsafe float[] Run(AudioEffect effect, float[] input, int channels = Channels)
    {
        if (!effect.IsInitialized)
            effect.Initialize(TestSampleRate, channels);

        float[] output = new float[input.Length];

        fixed (float* pIn = input, pOut = output)
        {
            var framesIn = new NativeArray<float>(pIn, input.Length);
            var framesOut = new NativeArray<float>(pOut, output.Length);
            uint frameCount = (uint)(input.Length / channels);
            uint frameCountOut = frameCount;

            // Process, not OnProcess: that is what a chain calls, and it is where bypassing lives.
            effect.Process(framesIn, frameCount, framesOut, ref frameCountOut, (uint)channels);
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

    /// <summary>Alternating full scale samples, which is a signal at exactly the Nyquist frequency.</summary>
    private static float[] Nyquist(int frames = Frames)
    {
        float[] buffer = new float[frames * Channels];

        for (int i = 0; i < frames; i++)
        {
            float value = (i % 2 == 0) ? 1.0f : -1.0f;
            buffer[i * Channels] = value;
            buffer[i * Channels + 1] = value;
        }

        return buffer;
    }

    private static float[] Ramp(int frames, int channels)
    {
        var samples = new float[frames * channels];

        for (int i = 0; i < samples.Length; i++)
            samples[i] = i / (float)samples.Length;

        return samples;
    }

    #endregion

    #region AudioBuffer and NativeArray

    // Read(ref output) must allocate the output buffer when it is null instead of dereferencing null.
    [Fact]
    public void AudioBuffer_Read_AllocatesWhenOutputNull()
    {
        var buffer = new AudioBuffer(8192);
        float[] output = null!;

        var ex = Record.Exception(() => buffer.Read(ref output));

        Assert.Null(ex);
        Assert.NotNull(output);
        Assert.Equal(8192, output.Length);
    }

    // Write sized its destination from the source, so the bounds check compared the source against
    // itself and always passed. Anything longer than the capacity was memcpy'd past the end of the
    // managed array. The clamped return value is the observable half of that.
    [Fact]
    public unsafe void AudioBuffer_Write_ClampsToCapacity()
    {
        var buffer = new AudioBuffer(8);
        float[] source = new float[32];
        for (int i = 0; i < source.Length; i++)
            source[i] = i;

        int written;
        fixed (float* pSource = source)
            written = buffer.Write(new NativeArray<float>(pSource, source.Length));

        Assert.Equal(8, written);

        float[] output = null!;
        int read = buffer.Read(ref output);

        Assert.Equal(8, read);
        for (int i = 0; i < 8; i++)
            Assert.Equal(i, output[i]);
    }

    // The indexer built an IndexOutOfRangeException and dropped it on the floor, so a user effect
    // reading past its buffer silently touched whatever native memory came next.
    [Fact]
    public unsafe void NativeArray_OutOfRangeIndex_Throws()
    {
        float[] data = new float[4];
        bool threw = false;

        fixed (float* pData = data)
        {
            var array = new NativeArray<float>(pData, data.Length);

            try { _ = array[4]; }
            catch (IndexOutOfRangeException) { threw = true; }
        }

        Assert.True(threw);
    }

    #endregion

    #region AudioClip data and format

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
        using var mono = AudioClip.Create("Mono", Ramp(100, 1), channels: 1, sampleRate: TestSampleRate);
        using var stereo = AudioClip.Create("Stereo", Ramp(100, 2), channels: 2, sampleRate: TestSampleRate);

        Assert.Equal(100ul, mono.SampleCount);
        Assert.Equal(100ul, stereo.SampleCount);
        Assert.Equal(mono.LengthInSeconds, stereo.LengthInSeconds, 5);
    }

    [Fact]
    public void GetSampleData_RoundTripsWhatWasCreated()
    {
        float[] samples = Ramp(64, 2);

        using var clip = AudioClip.Create("Round Trip", samples, channels: 2, sampleRate: TestSampleRate);
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
        Assert.Throws<ArgumentOutOfRangeException>(() => AudioClip.Create("Bad", [0f], channels, TestSampleRate));
    }

    #endregion

    #region AudioClip shared buffers

    // Clips with identical data share one native buffer. A deserialized clip used to adopt that
    // buffer without counting the reference, so disposing any other holder freed it underneath the
    // survivors, leaving them pointing at released memory.
    [Fact]
    public void AudioClip_Deserialize_CountsItsReferenceOnASharedBuffer()
    {
        byte[] data = [11, 22, 33, 44, 55];

        var first = new AudioClip(data);
        ulong hash = first.Hash;
        Assert.Equal(1, AudioContext.GetClipRefCount(hash));

        var deserialized = Serializer.Deserialize<AudioClip>(Serializer.Serialize(first))!;

        Assert.Equal(first.Handle, deserialized.Handle);
        Assert.Equal(2, AudioContext.GetClipRefCount(hash));

        // The buffer belongs to the survivor now, not to the clip that allocated it.
        first.Dispose();
        Assert.Equal(1, AudioContext.GetClipRefCount(hash));

        deserialized.Dispose();
        Assert.Equal(0, AudioContext.GetClipRefCount(hash));
    }

    // The reference count has to hold whichever order holders arrive and leave in, since nothing
    // constrains a scene to dispose its clips in the order it loaded them.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AudioClip_SharedBuffer_CountsHoldersInEitherDisposalOrder(bool disposeFirstLast)
    {
        byte[] data = [90, 91, 92, 93, 94, 95];

        var first = new AudioClip(data);
        var second = new AudioClip(data);
        var third = Serializer.Deserialize<AudioClip>(Serializer.Serialize(first))!;

        ulong hash = first.Hash;

        Assert.Equal(3, AudioContext.GetClipRefCount(hash));
        Assert.Equal(first.Handle, second.Handle);
        Assert.Equal(first.Handle, third.Handle);

        if (disposeFirstLast)
        {
            third.Dispose();
            second.Dispose();
            Assert.Equal(1, AudioContext.GetClipRefCount(hash));
            first.Dispose();
        }
        else
        {
            first.Dispose();
            third.Dispose();
            Assert.Equal(1, AudioContext.GetClipRefCount(hash));
            second.Dispose();
        }

        Assert.Equal(0, AudioContext.GetClipRefCount(hash));
    }

    // Disposing twice must not take a second reference off a buffer someone else is still holding.
    [Fact]
    public void AudioClip_DisposingTwice_OnlyReleasesOnce()
    {
        byte[] data = [70, 71, 72];

        var holder = new AudioClip(data);
        var extra = new AudioClip(data);
        ulong hash = holder.Hash;

        Assert.Equal(2, AudioContext.GetClipRefCount(hash));

        extra.Dispose();
        extra.Dispose();

        Assert.Equal(1, AudioContext.GetClipRefCount(hash));
        Assert.NotEqual(IntPtr.Zero, holder.Handle);

        holder.Dispose();
        Assert.Equal(0, AudioContext.GetClipRefCount(hash));
    }

    // Deserializing into a clip that already held a buffer used to overwrite the handle, orphaning
    // the previous one with a reference that nothing could ever drop.
    [Fact]
    public void AudioClip_Deserialize_ReleasesThePreviousBuffer()
    {
        var original = new AudioClip([60, 61, 62]);
        var replacement = new AudioClip([70, 71, 72, 73]);

        ulong originalHash = original.Hash;
        Assert.Equal(1, AudioContext.GetClipRefCount(originalHash));

        original.Deserialize(Serializer.Serialize(replacement), new SerializationContext());

        Assert.Equal(0, AudioContext.GetClipRefCount(originalHash));
        Assert.Equal(replacement.Handle, original.Handle);

        original.Dispose();
        replacement.Dispose();
    }

    // The stored byte count and the stored bytes are two separate keys. Trusting the count let a
    // truncated or tampered asset hand the decoder a length past the end of the allocation.
    [Fact]
    public void AudioClip_Deserialize_SizesFromTheActualBytes()
    {
        byte[] data = [1, 1, 2, 3, 5, 8, 13];

        var clip = new AudioClip(data);
        EchoObject echo = Serializer.Serialize(clip);
        echo["DataSize"] = new EchoObject((long)999_999);

        var restored = Serializer.Deserialize<AudioClip>(echo)!;

        Assert.Equal((ulong)data.Length, restored.DataSize);

        clip.Dispose();
        restored.Dispose();
    }

    // A key that is not there used to throw out of the middle of the load, which costs the whole
    // scene rather than the one clip.
    [Fact]
    public void AudioClip_Deserialize_ToleratesMissingKeys()
    {
        var empty = EchoObject.NewCompound();

        var clip = Serializer.Deserialize(empty, typeof(AudioClip)) as AudioClip;

        Assert.NotNull(clip);
        Assert.Equal(IntPtr.Zero, clip!.Handle);
        Assert.Equal(0ul, clip.DataSize);
        Assert.Equal(string.Empty, clip.FilePath);

        clip.Dispose();
    }

    // The bytes are enough to identify the buffer on their own, so a compound written without the
    // hash still has to load rather than resolving to a clip with no data.
    [Fact]
    public void AudioClip_Deserialize_RecoversAMissingHash()
    {
        byte[] data = [41, 42, 43, 44, 45, 46];

        var source = new AudioClip(data);
        EchoObject echo = Serializer.Serialize(source);
        Assert.True(echo.Remove("HashCode"));

        var restored = Serializer.Deserialize<AudioClip>(echo)!;

        Assert.NotEqual(IntPtr.Zero, restored.Handle);
        Assert.Equal((ulong)data.Length, restored.DataSize);
        Assert.Equal(source.Handle, restored.Handle);

        source.Dispose();
        restored.Dispose();
    }

    #endregion

    #region AudioSource serialization

    // AudioSource keeps its settings in private fields, so they only persist because they carry
    // [SerializeField]. Dropping one is invisible until a scene reloads without its audio settings.
    [Fact]
    public void AudioSource_RoundTripsItsSettings()
    {
        var source = new AudioSource
        {
            PlayOnStart = true,
            Loop = true,
            Volume = 0.25f,
            Pitch = 1.5f,
            Pan = -0.5f,
            PanMode = PanMode.Pan,
            Spatial = false,
            DopplerFactor = 2f,
            MinDistance = 3f,
            MaxDistance = 40f,
            AttenuationModel = AttenuationModel.Exponential,
        };

        var restored = Serializer.Deserialize<AudioSource>(Serializer.Serialize(source));

        Assert.NotNull(restored);
        Assert.True(restored!.PlayOnStart);
        Assert.True(restored.Loop);
        Assert.Equal(0.25f, restored.Volume);
        Assert.Equal(1.5f, restored.Pitch);
        Assert.Equal(-0.5f, restored.Pan);
        Assert.Equal(PanMode.Pan, restored.PanMode);
        Assert.False(restored.Spatial);
        Assert.Equal(2f, restored.DopplerFactor);
        Assert.Equal(3f, restored.MinDistance);
        Assert.Equal(40f, restored.MaxDistance);
        Assert.Equal(AttenuationModel.Exponential, restored.AttenuationModel);
    }

    // A scene written before a field existed has no key for it. Field deserialization leaves the
    // constructor's value alone, which is what keeps an old scene at full volume rather than silent.
    [Fact]
    public void AudioSource_Deserialize_KeepsDefaultsForMissingKeys()
    {
        var restored = Serializer.Deserialize(EchoObject.NewCompound(), typeof(AudioSource)) as AudioSource;

        Assert.NotNull(restored);
        Assert.Equal(1f, restored!.Volume);
        Assert.Equal(1f, restored.Pitch);
        Assert.Equal(0f, restored.Pan);
        Assert.True(restored.Spatial);
        Assert.Equal(1f, restored.MinDistance);
        Assert.Equal(10f, restored.MaxDistance);
        Assert.False(restored.Loop);
        Assert.False(restored.PlayOnStart);
    }

    // Live playback position used to be written into the serialized form, so saving a scene while
    // play mode was running baked whatever sample the music was on into the asset, and every later
    // load started there. On a prefab every instance spawned mid clip.
    [Fact]
    public void AudioSource_DoesNotSerializePlaybackPosition()
    {
        EchoObject echo = Serializer.Serialize(new AudioSource());

        Assert.Null(echo.Get("_savedCursor"));
        Assert.Null(echo.Get("_wasPlaying"));
    }

    // Effects used to be script-only and unserialized, so a chain built at runtime was gone the next
    // time the scene loaded and could never be authored in the first place.
    [Fact]
    public void AudioSource_RoundTripsItsEffectChain()
    {
        var source = new AudioSource();
        source.AddEffect(new FilterEffect { Type = FilterType.Highpass, Frequency = 800f, Q = 1.5f });
        source.AddEffect(new DistortionEffect { Drive = 3f, Blend = 0.25f });

        var restored = Serializer.Deserialize<AudioSource>(Serializer.Serialize(source))!;

        Assert.Equal(2, restored.EffectCount);

        var filter = Assert.IsType<FilterEffect>(restored.Effects[0]);
        Assert.Equal(FilterType.Highpass, filter.Type);
        Assert.Equal(800f, filter.Frequency);
        Assert.Equal(1.5f, filter.Q);

        var distortion = Assert.IsType<DistortionEffect>(restored.Effects[1]);
        Assert.Equal(3f, distortion.Drive);
        Assert.Equal(0.25f, distortion.Blend);

        // Parameters alone are not enough, the DSP state has to be rebuilt on the way in or the
        // effect is inert until something else happens to touch it.
        Assert.True(filter.IsInitialized);
    }

    #endregion

    #region Effect DSP

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

    // Each filter type has a response at DC that follows from its coefficients, so this catches a
    // coefficient set being wired to the wrong type as well as an outright arithmetic mistake.
    [Theory]
    [InlineData(FilterType.Lowpass, 1f)]
    [InlineData(FilterType.Highpass, 0f)]
    [InlineData(FilterType.Bandpass, 0f)]
    [InlineData(FilterType.Notch, 1f)]
    [InlineData(FilterType.Peak, 1f)]
    [InlineData(FilterType.Lowshelf, 1f)]
    [InlineData(FilterType.Highshelf, 1f)]
    public void Filter_HasTheExpectedGainAtDC(FilterType type, float expected)
    {
        // The shelf and peak types are flat at 0 dB, which is what makes them 1 here.
        var effect = new FilterEffect { Type = type, Frequency = 500f, Q = 0.707f, GainDB = 0f };

        float[] output = Run(effect, Interleave(1f, 1f, 2048));

        Assert.Equal(expected, output[^2], 2);
    }

    // The other end of the spectrum, where a lowpass and a bandpass have to reach zero and a highpass
    // has to reach unity. Alternating full scale samples are a signal exactly at Nyquist.
    [Theory]
    [InlineData(FilterType.Lowpass, 0f)]
    [InlineData(FilterType.Highpass, 1f)]
    [InlineData(FilterType.Bandpass, 0f)]
    [InlineData(FilterType.Notch, 1f)]
    [InlineData(FilterType.Peak, 1f)]
    [InlineData(FilterType.Lowshelf, 1f)]
    [InlineData(FilterType.Highshelf, 1f)]
    public void Filter_HasTheExpectedGainAtNyquist(FilterType type, float expected)
    {
        var effect = new FilterEffect { Type = type, Frequency = 500f, Q = 0.707f, GainDB = 0f };

        float[] output = Run(effect, Nyquist(2048));

        Assert.Equal(expected, Math.Abs(output[^2]), 2);
    }

    // A delay line's whole job: an impulse comes back out one delay length later, and nowhere else.
    [Fact]
    public void DelayEffect_ReturnsAnImpulseOneDelayLater()
    {
        const int delayFrames = 8;
        var effect = new DelayEffect { DelayInSeconds = delayFrames / (float)TestSampleRate, Decay = 0f };

        float[] input = new float[64 * Channels];
        input[0] = 1f;
        input[1] = 1f;

        float[] output = Run(effect, input);

        Assert.Equal((uint)delayFrames, effect.DelayInFrames);
        Assert.Equal(1f, output[delayFrames * Channels], 4);
        Assert.Equal(1f, output[delayFrames * Channels + 1], 4);

        for (int frame = 0; frame < 64; frame++)
        {
            if (frame == delayFrames) continue;
            Assert.Equal(0f, output[frame * Channels], 4);
        }
    }

    // A zero length delay allocated a zero length buffer and then took the cursor modulo zero on the
    // audio thread. One frame is the floor.
    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(1f / TestSampleRate)]
    public void DelayEffect_ZeroLengthDelay_ClampsToOneFrame(float seconds)
    {
        var effect = new DelayEffect { DelayInSeconds = seconds, Decay = 0f };

        float[] output = Run(effect, Interleave(1f, 1f));

        Assert.Equal(1u, effect.DelayInFrames);
        Assert.Equal(0f, output[0]);
        Assert.Equal(1f, output[^1], 4);
    }

    // Bypass used to be applied by filtering the effect out when the chain snapshot was built, so
    // setting it from gameplay did nothing until something else happened to republish. It worked from
    // the inspector, which is the worst place for a bug to work.
    [Fact]
    public void BypassedEffect_LeavesTheBufferUntouched()
    {
        var effect = new DistortionEffect { Drive = 10f, Blend = 1f };
        float[] input = Interleave(0.5f, 0.5f, 16);

        float[] shaped = Run(effect, input);
        Assert.NotEqual(0.5f, shaped[0], 3);

        // Set after the effect has already run once, the way a gameplay toggle would.
        effect.Bypass = true;
        float[] bypassed = Run(effect, input);

        // Untouched means the previous stage's output carries on unchanged, which here is silence.
        foreach (float sample in bypassed)
            Assert.Equal(0f, sample);
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

    // Same channel smearing as the filter, and the phaser additionally advanced its sweep once per
    // sample per channel, so a stereo source swept at double the configured rate.
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

    // The reverb only supports one or two channels, so on anything else it has to pass audio through
    // rather than throwing out of a constructor on the audio setup path.
    [Fact]
    public void ReverbEffect_OnAnUnsupportedChannelCount_PassesThrough()
    {
        var effect = new ReverbEffect();
        effect.Initialize(TestSampleRate, 6);

        float[] input = new float[6 * 4];
        for (int i = 0; i < input.Length; i++) input[i] = 0.25f;

        float[] output = Run(effect, input, channels: 6);

        // Untouched output means the previous stage's signal carries on unchanged.
        foreach (float sample in output)
            Assert.Equal(0f, sample);
    }

    #endregion

    #region Mixer

    [Fact]
    public void NewMixer_StartsWithAMasterGroup()
    {
        var mixer = new AudioMixer();

        Assert.Single(mixer.Groups);
        Assert.NotNull(mixer.Master);
        Assert.Equal("Master", mixer.Master.GroupName);
        Assert.Null(mixer.Master.Parent);
    }

    [Fact]
    public void AddGroup_DefaultsToFeedingTheMaster()
    {
        var mixer = new AudioMixer();

        AudioMixerGroup music = mixer.AddGroup("Music");
        AudioMixerGroup footsteps = mixer.AddGroup("Footsteps", music);

        Assert.Same(mixer.Master, music.Parent);
        Assert.Same(music, footsteps.Parent);
        Assert.Same(music, mixer.FindGroup("Music"));
    }

    // Removing a bus must not orphan what fed into it, or those sources go silent rather than moving
    // up to the parent bus.
    [Fact]
    public void RemoveGroup_RepointsChildrenAtTheirGrandparent()
    {
        var mixer = new AudioMixer();
        AudioMixerGroup music = mixer.AddGroup("Music");
        AudioMixerGroup stingers = mixer.AddGroup("Stingers", music);

        Assert.True(mixer.RemoveGroup(music));

        Assert.Same(mixer.Master, stingers.Parent);
        Assert.Null(mixer.FindGroup("Music"));
    }

    // Everything eventually feeds the root, so removing it would leave the tree with no outlet.
    [Fact]
    public void RemoveGroup_WillNotRemoveTheMaster()
    {
        var mixer = new AudioMixer();

        Assert.False(mixer.RemoveGroup(mixer.Master));
        Assert.Single(mixer.Groups);
    }

    // A slider works in linear gain and a mixer works in decibels, so the pair has to round trip.
    [Theory]
    [InlineData(0f)]
    [InlineData(-6f)]
    [InlineData(-20f)]
    [InlineData(6f)]
    public void VolumeConversion_RoundTrips(float decibels)
    {
        float linear = AudioMixerGroup.DecibelsToLinear(decibels);

        Assert.Equal(decibels, AudioMixerGroup.LinearToDecibels(linear), 3);
    }

    [Fact]
    public void VolumeConversion_TreatsTheFloorAsSilence()
    {
        Assert.Equal(0f, AudioMixerGroup.DecibelsToLinear(AudioMixerGroup.MinVolumeDB));
        Assert.Equal(0f, AudioMixerGroup.DecibelsToLinear(-200f));
        Assert.Equal(AudioMixerGroup.MinVolumeDB, AudioMixerGroup.LinearToDecibels(0f));
    }

    // Group identities key every saved reference. If they moved with a group's position, inserting a
    // bus would silently repoint sources at whichever group took its index.
    [Fact]
    public void GroupIdentities_AreStableAcrossInsertionAndRename()
    {
        var mixer = new AudioMixer();
        AudioMixerGroup music = mixer.AddGroup("Music");
        string identity = music.Identity;

        mixer.AddGroup("SFX");
        music.GroupName = "Soundtrack";

        Assert.Equal(identity, music.Identity);
        Assert.NotEqual(mixer.Master.Identity, music.Identity);
    }

    // A bus carries its own effect chain, so a reverb can sit on the whole SFX bus rather than being
    // duplicated onto every source feeding it.
    [Fact]
    public void GroupEffects_RoundTripWithTheMixer()
    {
        var mixer = new AudioMixer();
        AudioMixerGroup sfx = mixer.AddGroup("SFX");
        sfx.AddEffect(new FilterEffect { Type = FilterType.Lowpass, Frequency = 3000f });

        var restored = Serializer.Deserialize<AudioMixer>(Serializer.Serialize(mixer))!;

        AudioMixerGroup restoredSfx = restored.FindGroup("SFX");
        Assert.Single(restoredSfx.Effects);

        var filter = Assert.IsType<FilterEffect>(restoredSfx.Effects[0]);
        Assert.Equal(3000f, filter.Frequency);

        // Bound to the audio format on the way in, or the effect is inert until something else runs.
        restoredSfx.RefreshEffects();
        Assert.True(filter.IsInitialized);
    }

    [Fact]
    public void GroupEffects_AreDestroyedWhenRemoved()
    {
        var mixer = new AudioMixer();
        AudioMixerGroup sfx = mixer.AddGroup("SFX");
        var effect = new CountingEffect();

        sfx.AddEffect(effect);
        Assert.Single(sfx.Effects);

        sfx.RemoveEffect(effect);

        Assert.Empty(sfx.Effects);
        Assert.Equal(1, effect.Destroyed);
    }

    [Fact]
    public void Mixer_RoundTripsItsTree()
    {
        var mixer = new AudioMixer();
        AudioMixerGroup music = mixer.AddGroup("Music");
        music.VolumeDB = -12f;
        music.Mute = true;
        mixer.AddGroup("Stingers", music);

        var restored = Serializer.Deserialize<AudioMixer>(Serializer.Serialize(mixer))!;

        Assert.Equal(3, restored.Groups.Count);

        AudioMixerGroup restoredMusic = restored.FindGroup("Music");
        Assert.NotNull(restoredMusic);
        Assert.Equal(-12f, restoredMusic.VolumeDB);
        Assert.True(restoredMusic.Mute);
        Assert.Same(restored.Master, restoredMusic.Parent);

        // Bound on the way in, or Parent would answer null on everything.
        Assert.Same(restoredMusic, restored.FindGroup("Stingers").Parent);
    }

    #endregion

    #region Components without a device

    // OnDisable used to destroy every effect and leave them in the list, so toggling a component ran
    // the same effects again after they had been told they were finished.
    [Fact]
    public void Effects_SurviveDisableAndEnable()
    {
        var source = CreateSource();
        var effect = new CountingEffect();
        source.AddEffect(effect);

        source.Enabled = false;
        source.Enabled = true;

        Assert.Equal(0, effect.Destroyed);
        Assert.Equal(1, source.EffectCount);
    }

    // ClearEffects emptied the list without telling the effects, so the two removal paths disagreed
    // about whether an effect ever gets destroyed.
    [Fact]
    public void Effects_AreDestroyedWhenRemoved()
    {
        var source = CreateSource();
        var removed = new CountingEffect();
        var cleared = new CountingEffect();
        source.AddEffect(removed);
        source.AddEffect(cleared);

        source.RemoveEffect(removed);

        Assert.Equal(1, removed.Destroyed);
        Assert.Equal(0, cleared.Destroyed);
        Assert.Equal(1, source.EffectCount);

        source.ClearEffects();

        Assert.Equal(1, cleared.Destroyed);
        Assert.Equal(0, source.EffectCount);
    }

    // Removing an effect that was never attached must not destroy it.
    [Fact]
    public void Effects_RemovingAnUnattachedEffect_DoesNothing()
    {
        var source = CreateSource();
        var stranger = new CountingEffect();

        source.RemoveEffect(stranger);

        Assert.Equal(0, stranger.Destroyed);
    }

    [Fact]
    public void Effects_AreDestroyedWithTheSource()
    {
        var source = CreateSource();
        var effect = new CountingEffect();
        source.AddEffect(effect);

        source.Destroy();
        EngineObject.ProcessDestroyed();

        Assert.Equal(1, effect.Destroyed);
    }

    [Fact]
    public void PauseAndStop_WithoutADevice_LeaveNoStuckState()
    {
        var source = CreateSource();

        source.Pause();
        Assert.False(source.IsPaused);

        source.Resume();
        source.Stop();

        Assert.False(source.IsPaused);
        Assert.Equal(0f, source.PlaybackTime);
        Assert.Equal(0f, source.Duration);
        Assert.Equal(0f, source.NormalizedTime);
    }

    // Seeking a source with nothing loaded has no length to seek within, so it must answer zero
    // rather than dividing by one.
    [Fact]
    public void NormalizedTime_WithNoClip_StaysAtZero()
    {
        var source = CreateSource();

        source.NormalizedTime = 0.5f;
        source.PlaybackTime = 2f;

        Assert.Equal(0f, source.NormalizedTime);
    }

    // Spatial audio is only defined with exactly one listener, so the count has to track enable and
    // disable exactly or the warnings that lean on it are noise.
    [Fact]
    public void ListenerCount_TracksEnableAndDisable()
    {
        int before = AudioListener.ActiveCount;

        var scene = CreateScene(enable: true);
        var go = CreateGameObject("Ears");
        var listener = go.AddComponent<AudioListener>();
        scene.Add(go);

        // No device in a test run, so nothing is counted. What matters is that it comes back to where
        // it started rather than drifting.
        listener.Enabled = false;
        listener.Enabled = true;
        listener.Enabled = false;

        Assert.Equal(before, AudioListener.ActiveCount);
    }

    // One shots layer on pooled voices now, so none of this may touch the main playback or throw when
    // there is no device to make a voice on.
    [Fact]
    public void PlayOneShot_WithoutADevice_DoesNothing()
    {
        var source = CreateSource();
        var clip = new AudioClip([1, 2, 3, 4]);

        source.PlayOneShot(clip);
        source.PlayOneShot(clip, 0.5f);
        source.StopOneShots();

        Assert.Equal(0, source.ActiveOneShotCount);

        clip.Dispose();
    }

    // The temporary object PlayClipAtPoint spawns is cleaned up by the End event, which never fires
    // without a device, so it must not spawn one at all rather than leaking it every call.
    [Fact]
    public void PlayClipAtPoint_WithoutADevice_SpawnsNothing()
    {
        CreateScene(enable: true);
        var clip = new AudioClip([5, 6, 7, 8]);

        Assert.Null(AudioSource.PlayClipAtPoint(clip, new Float3(1, 2, 3)));

        clip.Dispose();
    }

    // A headless run never initializes the context, and neither does a failed device open. Handing
    // that null context to the native layer is what the guards exist to stop, so the components have
    // to come up inert and every entry point has to stay callable.
    [Fact]
    public void AudioComponents_WithoutADevice_StayInertAndUsable()
    {
        Assert.False(AudioContext.IsInitialized);

        var scene = CreateScene(enable: true);
        var go = CreateGameObject("Speaker");
        var source = go.AddComponent<AudioSource>();
        var listener = go.AddComponent<AudioListener>();
        scene.Add(go);

        Update(scene);

        source.Clip = new AudioClip([1, 2, 3, 4]);
        source.Play();
        source.PlayProcedural();
        source.Stop();

        Update(scene);

        Assert.Equal(IntPtr.Zero, listener.Handle);
        Assert.False(source.IsPlaying);
        Assert.Equal(0ul, source.Cursor);
        Assert.Equal(0ul, source.Length);

        source.Clip!.Dispose();
    }

    #endregion

    #region AudioContext

    // Prowl is left handed with +Z forward, the audio engine is right handed with -Z forward. Both
    // components have to mirror on the same axis, and only that axis, or one of left/right and
    // vertical comes out inverted.
    [Fact]
    public void ToAudioSpace_MirrorsForwardOnly()
    {
        Assert.Equal(new Float3(0, 0, -1), AudioContext.ToAudioSpace(Float3.UnitZ));
        Assert.Equal(Float3.UnitY, AudioContext.ToAudioSpace(Float3.UnitY));
        Assert.Equal(Float3.UnitX, AudioContext.ToAudioSpace(Float3.UnitX));
    }

    // Deinitialize used to free every shared clip buffer, which left live clips holding dangling
    // pointers and is what made reopening the device impossible. Clip data is not device owned.
    [Fact]
    public void Deinitialize_LeavesClipDataAlone()
    {
        var clip = new AudioClip([21, 22, 23, 24]);
        ulong hash = clip.Hash;

        Assert.Equal(1, AudioContext.GetClipRefCount(hash));

        AudioContext.Deinitialize();

        Assert.Equal(1, AudioContext.GetClipRefCount(hash));
        Assert.NotEqual(IntPtr.Zero, clip.Handle);

        clip.Dispose();
        Assert.Equal(0, AudioContext.GetClipRefCount(hash));
    }

    // Project settings are applied from paths that never wanted audio: a headless build, a dedicated
    // server. Restart used to fall straight through to Initialize when nothing was open, so applying
    // settings in any of them opened a device on a machine that might not have one.
    [Fact]
    public void Restart_WithoutADevice_DoesNotOpenOne()
    {
        Assert.False(AudioContext.IsInitialized);

        AudioContext.Restart(48000, 2, 1024);

        Assert.False(AudioContext.IsInitialized);
    }

    // The volume is set from project settings, which can be applied before the device opens and in
    // runs where it never opens at all. It has to survive that rather than being dropped.
    [Fact]
    public void MasterVolume_IsRememberedWithoutADevice()
    {
        float previous = AudioContext.MasterVolume;
        try
        {
            AudioContext.MasterVolume = 0.35f;
            Assert.Equal(0.35f, AudioContext.MasterVolume);
        }
        finally
        {
            AudioContext.MasterVolume = previous;
        }
    }

    #endregion
}
