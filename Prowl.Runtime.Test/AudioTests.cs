// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Reflection;

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
    private static float[] Run(AudioEffect effect, float[] input, int channels = Channels)
        => Run(effect, input, new float[input.Length], channels);

    /// <summary>
    /// Same, writing into a buffer the caller supplies. Handing in something other than silence is the
    /// only way to tell "wrote zeroes" apart from "wrote nothing".
    /// </summary>
    private static unsafe float[] Run(AudioEffect effect, float[] input, float[] output, int channels = Channels)
    {
        if (!effect.IsInitialized)
            effect.Initialize(TestSampleRate, channels);

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

    /// <summary>A full scale sine at a given frequency, the same in every channel.</summary>
    private static float[] Sine(float hertz, int frames)
    {
        float[] buffer = new float[frames * Channels];
        float step = 2f * MathF.PI * hertz / TestSampleRate;

        for (int i = 0; i < frames; i++)
        {
            float value = MathF.Sin(step * i);
            buffer[i * Channels] = value;
            buffer[i * Channels + 1] = value;
        }

        return buffer;
    }

    /// <summary>
    /// RMS of one channel over the tail of a block. For a sine this is the amplitude over root two, so
    /// the ratio of two of them is the gain the filter applied.
    /// </summary>
    private static float TailRms(float[] interleaved, int channel)
    {
        int frames = interleaved.Length / Channels;
        int from = frames / 2;
        double sum = 0.0;

        for (int frame = from; frame < frames; frame++)
        {
            float sample = interleaved[frame * Channels + channel];
            sum += sample * sample;
        }

        return MathF.Sqrt((float)(sum / (frames - from)));
    }

    #endregion

    #region AudioBuffer and NativeArray

    // Read copies the whole written span, so a null or undersized destination has to be replaced before
    // the copy rather than being written past the end of.
    [Fact]
    public unsafe void AudioBuffer_Read_ProvidesADestinationBigEnoughForTheCapacity()
    {
        var buffer = new AudioBuffer(8);
        float[] source = new float[8];
        for (int i = 0; i < source.Length; i++)
            source[i] = i + 1;

        fixed (float* pSource = source)
            buffer.Write(new NativeArray<float>(pSource, source.Length));

        float[] output = null!;
        Assert.Equal(8, buffer.Read(ref output));
        Assert.Equal(8, output.Length);
        Assert.Equal(source, output);

        // An array from a smaller capacity, or from before the block size grew.
        float[] undersized = new float[2];
        Assert.Equal(8, buffer.Read(ref undersized));
        Assert.Equal(8, undersized.Length);
        Assert.Equal(source, undersized);
    }

    // One writer on the audio thread and one reader on the game thread, with no lock between them. A
    // block written all of one value has to read back all of one value: anything else is two halves of
    // two different blocks, which is a visualiser drawing audio that was never played.
    [Fact]
    public unsafe void AudioBuffer_UnderAConcurrentWriter_NeverReturnsATornBlock()
    {
        const int Capacity = 4096;
        var buffer = new AudioBuffer(Capacity);
        using var stop = new CancellationTokenSource();

        // A thread rather than a task, so nothing here waits on the pool and the test does not have to
        // block on a Task to shut it down.
        var writer = new Thread(() =>
        {
            float[] block = new float[Capacity];
            float value = 1f;

            fixed (float* pBlock = block)
            {
                var native = new NativeArray<float>(pBlock, block.Length);

                while (!stop.IsCancellationRequested)
                {
                    // Every sample of a block is the same, so a torn read is one that is not uniform.
                    for (int i = 0; i < block.Length; i++)
                        block[i] = value;

                    buffer.Write(native);
                    value = value >= 64f ? 1f : value + 1f;
                }
            }
        });

        writer.Start();

        try
        {
            const int Wanted = 2000;

            float[] output = null!;
            int consistentReads = 0;
            var clock = System.Diagnostics.Stopwatch.StartNew();

            // Counted rather than attempted, so the reader is still going while the writer is, however
            // long the thread took to get started.
            while (consistentReads < Wanted && clock.ElapsedMilliseconds < 10000)
            {
                int length = buffer.Read(ref output);

                if (length == 0)
                    continue;

                consistentReads++;

                for (int i = 1; i < length; i++)
                    Assert.True(output[i] == output[0],
                        $"torn block: sample {i} was {output[i]} where sample 0 was {output[0]}");
            }

            Assert.Equal(Wanted, consistentReads);
        }
        finally
        {
            stop.Cancel();
            writer.Join(TimeSpan.FromSeconds(5));
        }
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
    // reading past its buffer silently touched whatever native memory came next. The setter is the
    // half that matters most: past the end it is a write into memory that belongs to something else.
    [Fact]
    public unsafe void NativeArray_OutOfRangeIndex_Throws()
    {
        // Padding either side, so a write that does get through lands here rather than in the heap.
        float[] data = new float[12];

        fixed (float* pData = data)
        {
            var array = new NativeArray<float>(pData + 4, 4);

            // Written out rather than run through Assert.Throws, which needs a lambda, and a ref struct
            // cannot be captured by one.
            bool readPastEnd = false, readBeforeStart = false, writePastEnd = false;

            try { _ = array[4]; } catch (IndexOutOfRangeException) { readPastEnd = true; }
            try { _ = array[-1]; } catch (IndexOutOfRangeException) { readBeforeStart = true; }
            try { array[4] = 1f; } catch (IndexOutOfRangeException) { writePastEnd = true; }

            Assert.True(readPastEnd, "reading past the end was allowed");
            Assert.True(readBeforeStart, "reading before the start was allowed");
            Assert.True(writePastEnd, "writing past the end was allowed");
        }

        // Nothing outside the window was touched.
        foreach (float sample in data)
            Assert.Equal(0f, sample);
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

    // A format that cannot exist would be written into the wave header and handed to the decoder, which
    // is a worse place to find out about it than the call that made the clip.
    [Theory]
    [InlineData(0, TestSampleRate)]
    [InlineData(-1, TestSampleRate)]
    [InlineData(2, 0)]
    [InlineData(2, -44100)]
    public void Create_RejectsAnImpossibleFormat(int channels, int sampleRate)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AudioClip.Create("Bad", [0f], channels, sampleRate));
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

    // Playback hands the raw pointer to a native decoder that reads it on the audio thread for as
    // long as the voice sounds, and the clip that loaded it can be disposed in the meantime: an asset
    // reimport does exactly that, and so does a clip nothing kept a reference to being collected.
    // Whatever is playing has to be able to hold the buffer up on its own.
    [Fact]
    public void RetainedBuffer_OutlivesTheClipThatLoadedIt()
    {
        var clip = new AudioClip([80, 81, 82, 83]);
        ulong hash = clip.Hash;
        IntPtr handle = clip.Handle;

        Assert.True(AudioContext.RetainClipHandle(hash));
        Assert.Equal(2, AudioContext.GetClipRefCount(hash));

        // The clip goes, the way a reimport takes it away from under a playing source.
        clip.Dispose();

        Assert.Equal(1, AudioContext.GetClipRefCount(hash));

        // Still cached, so the pointer the native side is reading is still the same allocation.
        Assert.True(AudioContext.RetainClipHandle(hash));
        AudioContext.ReleaseClipHandle(hash);

        AudioContext.ReleaseClipHandle(hash);
        Assert.Equal(0, AudioContext.GetClipRefCount(hash));

        // Nothing holds it now, so there is nothing left to take a reference on.
        Assert.False(AudioContext.RetainClipHandle(hash));
        Assert.NotEqual(IntPtr.Zero, handle);
    }

    // A buffer nothing loaded cannot be referenced into existence, and a file backed clip has no
    // hash at all. Both have to answer rather than throw, since playback asks before it decides
    // whether it is playing from memory or from disk.
    [Fact]
    public void RetainingAnUncachedBuffer_ReportsFailure()
    {
        Assert.False(AudioContext.RetainClipHandle(0));
        Assert.False(AudioContext.RetainClipHandle(0xDEADBEEFDEADBEEF));
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
            MaxOneShotVoices = 3,
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
        Assert.Equal(3, restored.MaxOneShotVoices);
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

    // Two ways to get this wrong, and both are silent. A setting that is not marked persists in the
    // scene the editor has open and is gone the next time it loads. Live playback state that is marked
    // gets baked into the asset, which is what made saving a scene mid playback spawn every later
    // instance part way through its clip.
    //
    // Driven off the fields rather than a list written out here, so a field added later is covered by
    // whichever half of this it belongs to instead of by nothing.
    [Fact]
    public void AudioSource_Serializes_ItsSettingsAndNoLiveState()
    {
        EchoObject echo = Serializer.Serialize(new AudioSource());

        FieldInfo[] fields = typeof(AudioSource).GetFields(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

        Assert.NotEmpty(fields);

        foreach (FieldInfo field in fields)
        {
            bool persisted = (field.IsPublic || field.GetCustomAttribute<SerializeFieldAttribute>() != null)
                && field.GetCustomAttribute<SerializeIgnoreAttribute>() == null;

            if (persisted)
                Assert.True(echo.Get(field.Name) is not null, $"'{field.Name}' is a saved setting but was not written");
            else
                Assert.True(echo.Get(field.Name) is null, $"'{field.Name}' is runtime state but was written into the asset");
        }
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

    // DC and Nyquist are the same for every cutoff, so a filter that ignored Frequency and Q entirely
    // would pass both of the theories above. This is the part that says where the corner actually is:
    // a two pole lowpass is down to Q at its own cutoff, near unity well below it, and falls at twelve
    // decibels an octave above it.
    [Theory]
    [InlineData(1000f, 0.707f, 250f, 0.998f)]
    [InlineData(1000f, 0.707f, 1000f, 0.707f)]
    [InlineData(1000f, 0.707f, 4000f, 0.059f)]
    [InlineData(1000f, 4.0f, 1000f, 4.0f)]
    [InlineData(4000f, 0.707f, 1000f, 0.998f)]
    public void Filter_Lowpass_MatchesItsMagnitudeResponse(float cutoff, float q, float hertz, float expectedGain)
    {
        var effect = new FilterEffect { Type = FilterType.Lowpass, Frequency = cutoff, Q = q };

        float[] input = Sine(hertz, 8192);
        float[] output = Run(effect, input);

        // Measured over the tail of the block, so the filter's start up is not in the average.
        float gain = TailRms(output, 0) / TailRms(input, 0);

        Assert.Equal(expectedGain, gain, 0.02f * MathF.Max(expectedGain, 0.5f));
    }

    // The one parameter with no effect at all on a lowpass, and the type that exists to use it. A peak
    // sits at its gain in the middle and at unity either side of it.
    [Theory]
    [InlineData(12f, 3.981f)]
    [InlineData(-12f, 0.251f)]
    [InlineData(0f, 1f)]
    public void Filter_Peak_AppliesItsGainAtTheCentre(float gainDB, float expectedGain)
    {
        var effect = new FilterEffect { Type = FilterType.Peak, Frequency = 1000f, Q = 1f, GainDB = gainDB };

        float[] input = Sine(1000f, 8192);
        float gain = TailRms(Run(effect, input), 0) / TailRms(input, 0);

        Assert.Equal(expectedGain, gain, 0.02f * expectedGain);
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

    // OnValidate fires for every field on the source, and it used to rebuild each effect, which threw
    // away the filter's delay state. Dragging the volume slider clicked once per frame of the drag.
    [Fact]
    public void FilterEffect_OnValidate_KeepsItsRunningState()
    {
        var effect = new FilterEffect { Type = FilterType.Lowpass, Frequency = 500f, Q = 0.707f };

        // Settle on a constant, which a lowpass passes at unity.
        float[] settled = Run(effect, Interleave(1f, 1f, 2048));
        Assert.Equal(1f, settled[^2], 2);

        // Stands in for an unrelated inspector edit somewhere else on the source.
        effect.OnValidate();

        float[] after = Run(effect, Interleave(1f, 1f, 8));

        // Still settled. A rebuilt filter restarts from zero state and answers about 0.001 here.
        Assert.Equal(1f, after[0], 2);
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

        // Filled with something that is not silence first: writing zeroes and writing nothing look the
        // same in a buffer that started empty, and only one of them is what bypassing means.
        float[] destination = Interleave(-0.75f, 0.125f, 16);
        Run(effect, input, destination);

        for (int frame = 0; frame < 16; frame++)
        {
            Assert.Equal(-0.75f, destination[frame * Channels]);
            Assert.Equal(0.125f, destination[frame * Channels + 1]);
        }
    }

    // What bypassing is for, seen from where it matters: the block reaches the output as it went in,
    // rather than the chain promoting a buffer the bypassed stage never wrote.
    [Fact]
    public void EffectChain_ABypassedStage_PassesAudioThrough()
    {
        var effect = new DistortionEffect { Drive = 10f, Blend = 1f };
        var chain = new AudioEffectChain();
        chain.Publish([effect, new DistortionEffect { Blend = 0f }]);

        Assert.NotEqual(0.5f, RunChain(chain, Interleave(0.5f, 0.5f, 16), 16)[0], 3);

        effect.Bypass = true;
        float[] output = RunChain(chain, Interleave(0.5f, 0.5f, 16), 16);

        foreach (float sample in output)
            Assert.Equal(0.5f, sample, 5);
    }

    // OnValidate runs for every field on the owning source, so replacing the delay line whenever it is
    // called would empty the delay each time an unrelated slider moved.
    [Fact]
    public void DelayEffect_OnValidate_KeepsItsBufferedAudio()
    {
        const int delayFrames = 8;
        var effect = new DelayEffect { DelayInSeconds = delayFrames / (float)TestSampleRate, Decay = 0f };

        float[] impulse = new float[delayFrames * Channels];
        impulse[0] = 1f;
        impulse[1] = 1f;

        // The impulse goes in and is still inside the line, not yet due out.
        Run(effect, impulse);

        // Stands in for an unrelated inspector edit somewhere else on the source.
        effect.OnValidate();

        float[] output = Run(effect, new float[delayFrames * Channels]);

        // It comes out on schedule. A fresh line would have swallowed it.
        Assert.Equal(1f, output[0], 4);
    }

    // Changing the length does replace the line, since the geometry is different.
    [Fact]
    public void DelayEffect_ChangingTheLength_Resizes()
    {
        var effect = new DelayEffect { DelayInSeconds = 8f / TestSampleRate, Decay = 0f };
        Run(effect, Interleave(0f, 0f, 8));

        Assert.Equal(8u, effect.DelayInFrames);

        effect.DelayInSeconds = 16f / TestSampleRate;

        Assert.Equal(16u, effect.DelayInFrames);

        // What the reported number is supposed to mean. An impulse now comes back at sixteen frames,
        // and the old line's eight is silent.
        float[] impulse = new float[32 * Channels];
        impulse[0] = 1f;
        impulse[1] = 1f;

        float[] output = Run(effect, impulse);

        Assert.Equal(0f, output[8 * Channels], 4);
        Assert.Equal(1f, output[16 * Channels], 4);
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

    // A waveshaper is only a waveshaper if it is odd symmetric. One that is not adds a DC offset to
    // everything it touches, which is inaudible on its own and eats headroom off everything after it.
    [Fact]
    public void DistortionEffect_IsOddSymmetric()
    {
        var effect = new DistortionEffect { Drive = 6f, Blend = 1f };

        float[] positive = Run(effect, Interleave(0.3f, 0.7f, 4));
        float[] negative = Run(effect, Interleave(-0.3f, -0.7f, 4));

        Assert.Equal(positive[0], -negative[0], 5);
        Assert.Equal(positive[1], -negative[1], 5);
    }

    // Soft clipping means the output stays inside full scale however hard it is driven. Anything that
    // overshoots here is a hard clip further down the chain, or in the device.
    [Theory]
    [InlineData(1f, 1f)]
    [InlineData(4f, 100f)]
    [InlineData(200f, 1000f)]
    public void DistortionEffect_FullyWet_StaysWithinFullScale(float drive, float amplitude)
    {
        var effect = new DistortionEffect { Drive = drive, Blend = 1f };

        float[] output = Run(effect, Interleave(amplitude, -amplitude, 8));

        foreach (float sample in output)
        {
            Assert.True(float.IsFinite(sample), $"shaper produced {sample}");
            Assert.InRange(Math.Abs(sample), 0f, 1f);
        }

        // And the ceiling is not being reached by simply muting it.
        Assert.True(Math.Abs(output[0]) > 0.4f, $"shaper answered {output[0]} for an input of {amplitude}");
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

    // The decay figure divided by the attenuation per pass, which is zero for a room that loses
    // nothing. That is reachable through the property, and the cast of the resulting infinity to an
    // unsigned integer is undefined.
    [Theory]
    [InlineData(0f)]
    [InlineData(0.5f)]
    [InlineData(1f)]
    [InlineData(2f)]
    [InlineData(-1f)]
    public void ReverbEffect_DecayTime_StaysFiniteAtEveryRoomSize(float roomSize)
    {
        var effect = new ReverbEffect { RoomSize = roomSize };
        effect.Initialize(TestSampleRate, Channels);
        effect.RoomSize = roomSize;

        ulong decay = effect.DecayTimeInFrames;

        // An hour of tail is not a real answer, and neither is a wrapped one.
        Assert.InRange(decay, 0ul, (ulong)TestSampleRate * 3600);
    }

    // Freezing holds the tail forever, which this reports as zero rather than as a number.
    [Fact]
    public void ReverbEffect_WhenFrozen_ReportsNoDecay()
    {
        var effect = new ReverbEffect { RoomSize = 0.5f };
        effect.Initialize(TestSampleRate, Channels);

        Assert.True(effect.DecayTimeInFrames > 0);

        effect.Freeze = true;
        Assert.Equal(0ul, effect.DecayTimeInFrames);
    }

    // The reverb only supports one or two channels, so on anything else it has to pass audio through
    // rather than throwing out of a constructor on the audio setup path.
    [Fact]
    public void ReverbEffect_OnAnUnsupportedChannelCount_PassesThrough()
    {
        var effect = new ReverbEffect();
        effect.Initialize(TestSampleRate, 6);

        // Through a chain, which is the only place "passes through" means anything. Calling the effect
        // directly just shows that it did not write, which is not the same claim.
        var chain = new AudioEffectChain();
        chain.Publish([effect]);

        float[] input = new float[6 * 4];
        for (int i = 0; i < input.Length; i++) input[i] = 0.25f;

        float[] output = RunChain(chain, input, 4, channels: 6);

        foreach (float sample in output)
            Assert.Equal(0.25f, sample, 5);
    }

    /// <summary>Runs a chain over one block, the way the audio callback does.</summary>
    private static unsafe float[] RunChain(AudioEffectChain chain, float[] input, int outFrames, out uint written, int channels = Channels)
    {
        float[] output = new float[outFrames * channels];

        fixed (float* pIn = input, pOut = output)
            written = chain.Process(pIn, (uint)(input.Length / channels), pOut, (uint)outFrames, (uint)channels);

        Assert.True(written <= outFrames, $"chain reported {written} frames into a {outFrames} frame buffer");
        return output;
    }

    private static float[] RunChain(AudioEffectChain chain, float[] input, int outFrames, int channels = Channels)
        => RunChain(chain, input, outFrames, out _, channels);

    /// <summary>An effect that runs but writes nothing, which is what every built-in one does when it
    /// is handed a format it is not configured for.</summary>
    private sealed class InertEffect : AudioEffect
    {
        protected override void OnProcess(NativeArray<float> framesIn, uint frameCountIn, NativeArray<float> framesOut, ref uint frameCountOut, uint channels) { }
    }

    /// <summary>Reports the format it was bound to, and how many times.</summary>
    private sealed class FormatProbeEffect : AudioEffect
    {
        public int ObservedRate;
        public int ObservedChannels;
        public int Initializations;

        protected override void OnInitialize()
        {
            ObservedRate = SampleRate;
            ObservedChannels = Channels;
            Initializations++;
        }

        protected override void OnProcess(NativeArray<float> framesIn, uint frameCountIn, NativeArray<float> framesOut, ref uint frameCountOut, uint channels) { }
    }

    // Initialize is documented as safe to call again, which is what a device change relies on: every
    // effect gets rebound to the new format rather than being left sized for the old one.
    [Fact]
    public void Effect_Initialize_RebindsToANewFormat()
    {
        var effect = new FormatProbeEffect();

        effect.Initialize(44100, 2);

        Assert.True(effect.IsInitialized);
        Assert.Equal(44100, effect.ObservedRate);
        Assert.Equal(2, effect.ObservedChannels);

        effect.Initialize(22050, 1);

        Assert.Equal(22050, effect.ObservedRate);
        Assert.Equal(1, effect.ObservedChannels);
        Assert.Equal(2, effect.Initializations);
    }

    // A format that makes no sense would size the DSP state to nothing, so it is floored rather than
    // taken at face value.
    [Fact]
    public void Effect_Initialize_FloorsAnImpossibleFormat()
    {
        var effect = new FormatProbeEffect();

        effect.Initialize(0, 0);

        Assert.True(effect.ObservedRate >= 1);
        Assert.True(effect.ObservedChannels >= 1);
    }

    /// <summary>An effect that claims it produced more frames than it was given room for.</summary>
    private sealed class GreedyEffect : AudioEffect
    {
        protected override void OnProcess(NativeArray<float> framesIn, uint frameCountIn, NativeArray<float> framesOut, ref uint frameCountOut, uint channels)
        {
            for (int i = 0; i < framesOut.Length; i++)
                framesOut[i] = 1f;

            frameCountOut = frameCountIn * 8;
        }
    }

    // The chain used to pass the node graph's own input and output back and forth, which needed the
    // input to fit the output for one copy and the opposite for the other. It only ever worked because
    // the two counts happened to be equal, and any block where they were not threw into silence.
    [Theory]
    [InlineData(64, 64)]
    [InlineData(64, 32)]
    [InlineData(32, 64)]
    public void EffectChain_HandlesMismatchedFrameCounts(int inFrames, int outFrames)
    {
        var chain = new AudioEffectChain();
        chain.Publish([new DistortionEffect { Blend = 0f }]);

        float[] output = RunChain(chain, Interleave(0.5f, 0.5f, inFrames), outFrames, out uint written);

        // A one to one chain cannot produce more than it was given, and must not claim to.
        int expected = Math.Min(inFrames, outFrames);
        Assert.Equal((uint)expected, written);

        // Fully dry distortion is unity gain, so every frame it reported carries the input. Asserting
        // the whole buffer, not just the frames it reported: anything past them has to be left alone
        // rather than filled with whatever the chain happened to have lying around.
        for (int i = 0; i < output.Length; i++)
            Assert.Equal(i < expected * Channels ? 0.5f : 0f, output[i], 4);
    }

    // A block with nowhere to write leaves the working buffers empty, and a fixed statement over an
    // empty array yields a null pointer, which is not something an effect should ever be handed.
    [Fact]
    public unsafe void EffectChain_WithAnEmptyBlock_DoesNothing()
    {
        var chain = new AudioEffectChain();
        chain.Publish([new DistortionEffect()]);

        float[] nothing = [];
        uint written;

        fixed (float* pointer = nothing)
            written = chain.Process(pointer, 0, pointer, 0, (uint)Channels);

        Assert.Equal(0u, written);
    }

    // Every parameter carries a 0 to 1 range in the inspector, and the properties are the script path
    // to the same values, so they cannot be the one way in that ignores it.
    [Fact]
    public void ReverbEffect_ParametersClamp()
    {
        var effect = new ReverbEffect
        {
            RoomSize = 5f,
            Damping = -2f,
            Wet = 9f,
            Dry = -1f,
            Width = 3f,
            InputWidth = -4f,
        };

        Assert.Equal(1f, effect.RoomSize);
        Assert.Equal(0f, effect.Damping);
        Assert.Equal(1f, effect.Wet);
        Assert.Equal(0f, effect.Dry);
        Assert.Equal(1f, effect.Width);
        Assert.Equal(0f, effect.InputWidth);
    }

    // An effect is free to write nothing, and several built-in ones do exactly that when handed a
    // format they are not configured for. The chain promoted the buffer it had not written, so the
    // stage after it read whatever was in there from a previous block.
    [Fact]
    public void EffectChain_AnEffectThatWritesNothing_PassesAudioThrough()
    {
        var chain = new AudioEffectChain();
        chain.Publish([new InertEffect()]);

        float[] first = RunChain(chain, Interleave(0.5f, 0.5f, 16), 16);

        foreach (float sample in first)
            Assert.Equal(0.5f, sample, 5);

        // The second block is the one that catches a stale buffer: it would carry the first block's
        // audio rather than its own.
        float[] second = RunChain(chain, Interleave(-0.25f, -0.25f, 16), 16);

        foreach (float sample in second)
            Assert.Equal(-0.25f, sample, 5);
    }

    // Same again with a real stage after the inert one, since that is what actually reads the buffer
    // the inert effect left behind.
    [Fact]
    public void EffectChain_AnInertStage_DoesNotPoisonTheNextOne()
    {
        var chain = new AudioEffectChain();
        chain.Publish([new InertEffect(), new DistortionEffect { Blend = 0f }]);

        RunChain(chain, Interleave(0.75f, 0.75f, 16), 16);
        float[] output = RunChain(chain, Interleave(-0.5f, -0.5f, 16), 16);

        foreach (float sample in output)
            Assert.Equal(-0.5f, sample, 5);
    }

    // With nothing in it the block still has to reach the output, or a source with no effects goes
    // silent the moment it gets an effect node.
    [Fact]
    public void EffectChain_WithNoEffects_PassesTheBlockThrough()
    {
        var chain = new AudioEffectChain();

        float[] output = RunChain(chain, Interleave(0.25f, -0.25f, 32), 32, out uint written);

        Assert.Equal(32u, written);

        for (int frame = 0; frame < 32; frame++)
        {
            Assert.Equal(0.25f, output[frame * Channels], 5);
            Assert.Equal(-0.25f, output[frame * Channels + 1], 5);
        }
    }

    // Stages run in the order they were published. A chain that ran them backwards would still sound
    // like something, which is why nothing else here would notice.
    [Fact]
    public void EffectChain_RunsItsStagesInOrder()
    {
        // A gain either side of a shaper. Shaping full scale and then halving is a quarter, halving and
        // then shaping is not, because a waveshaper is not linear. Two linear stages would commute and
        // could not tell the two orders apart at all.
        AudioEffect Shaper() => new DistortionEffect { Drive = 1f, Blend = 1f };
        AudioEffect Halve() => new DistortionEffect { Blend = 0f, Volume = 0.5f };

        var shapeFirst = new AudioEffectChain();
        shapeFirst.Publish([Shaper(), Halve()]);

        var halveFirst = new AudioEffectChain();
        halveFirst.Publish([Halve(), Shaper()]);

        Assert.Equal(0.25f, RunChain(shapeFirst, Interleave(1f, 1f, 8), 8)[0], 3);
        Assert.Equal(0.295f, RunChain(halveFirst, Interleave(1f, 1f, 8), 8)[0], 3);
    }

    // An effect is allowed to change the frame count, but not to talk the chain into writing past the
    // buffer the graph handed it.
    [Fact]
    public void EffectChain_ClampsAnEffectThatOverstatesItsOutput()
    {
        var chain = new AudioEffectChain();
        chain.Publish([new GreedyEffect()]);

        float[] output = RunChain(chain, Interleave(1f, 1f, 16), 16, out uint written);

        Assert.Equal(16u, written);
        Assert.Equal(1f, output[^1], 5);
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

    // Only a hand edited or corrupted asset can contain routing that never reaches the root, and a
    // loop would be attached as a loop in the native node graph.
    [Fact]
    public void Mixer_StraightensOutARoutingLoopOnLoad()
    {
        var mixer = new AudioMixer();
        mixer.AddGroup("Music");
        mixer.AddGroup("SFX");

        EchoObject echo = Serializer.Serialize(mixer);
        EchoObject groups = echo.Get("_groups")!.Get("$values")!;

        // Point the two at each other, so neither reaches the master.
        groups[1]["_parentIndex"] = new EchoObject(2);
        groups[2]["_parentIndex"] = new EchoObject(1);

        var restored = Serializer.Deserialize<AudioMixer>(echo)!;

        foreach (AudioMixerGroup group in restored.Groups)
            Assert.True(ReachesRoot(group), $"'{group.GroupName}' never reaches the master");
    }

    [Fact]
    public void Mixer_DropsRoutingToAGroupThatIsNotThere()
    {
        var mixer = new AudioMixer();
        mixer.AddGroup("Music");

        EchoObject echo = Serializer.Serialize(mixer);
        echo.Get("_groups")!.Get("$values")![1]["_parentIndex"] = new EchoObject(97);

        var restored = Serializer.Deserialize<AudioMixer>(echo)!;

        Assert.Null(restored.FindGroup("Music").Parent);
    }

    /// <summary>Walks up from a group, giving up once it has taken more steps than there are groups.</summary>
    private static bool ReachesRoot(AudioMixerGroup group)
    {
        int steps = 0;

        while (group.Parent.IsValid())
        {
            if (++steps > 16) return false;
            group = group.Parent;
        }

        return true;
    }

    // Groups are sub-assets, so an AudioSource can hold a reference straight to one. Destroying the
    // object on removal left those sources pointing at a destroyed asset instead of at nothing.
    [Fact]
    public void RemoveGroup_LeavesTheGroupObjectAlive()
    {
        var mixer = new AudioMixer();
        AudioMixerGroup music = mixer.AddGroup("Music");

        Assert.True(mixer.RemoveGroup(music));

        Assert.True(music.IsValid());
        Assert.Null(mixer.FindGroup("Music"));

        // Detached, so it cannot claim whichever group has taken its old index.
        Assert.Null(music.Parent);
    }

    // Everything eventually feeds the root, so removing it would leave the tree with no outlet.
    [Fact]
    public void RemoveGroup_WillNotRemoveTheMaster()
    {
        var mixer = new AudioMixer();

        Assert.False(mixer.RemoveGroup(mixer.Master));
        Assert.Single(mixer.Groups);
    }

    // A slider works in linear gain and a mixer works in decibels, so the pair has to round trip. On
    // its own that proves very little: the two are inverses of each other whatever constant they use,
    // so a level meter built on 10 log10 would round trip perfectly and read every level wrong. The
    // anchors are what pin it to the amplitude definition, where -6 dB is half.
    [Theory]
    [InlineData(0f, 1f)]
    [InlineData(-6.0206f, 0.5f)]
    [InlineData(-20f, 0.1f)]
    [InlineData(6.0206f, 2f)]
    public void VolumeConversion_MatchesTheDecibelDefinition(float decibels, float linear)
    {
        Assert.Equal(linear, AudioMixerGroup.DecibelsToLinear(decibels), 4);
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

    // Assigning a clip used to start playing it, but only when PlayOnStart happened to be set, which is
    // a flag documented as controlling OnEnable. What that flag is worth here is limited: without a
    // device nothing sounds either way, so this covers the half that is observable, that the assignment
    // lands whatever the flag says and that assigning the same clip again is not an event.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AssigningAClip_SwapsIt_WhicheverWayPlayOnStartIsSet(bool playOnStart)
    {
        var source = CreateSource();
        var first = new AudioClip([1, 2, 3, 4]);
        var second = new AudioClip([5, 6, 7, 8]);

        source.PlayOnStart = playOnStart;

        source.Clip = first;
        Assert.Same(first, source.Clip);

        source.Clip = second;
        Assert.Same(second, source.Clip);

        source.Clip = null;
        Assert.Null(source.Clip);

        first.Dispose();
        second.Dispose();
    }

    // An inverted attenuation range is not a curve, and nothing stopped one being set.
    [Fact]
    public void Distances_StayInOrderWhicheverEndIsSet()
    {
        var source = CreateSource();

        source.MinDistance = 1f;
        source.MaxDistance = 10f;

        // Pushing the near end past the far end carries the far end with it rather than being clamped
        // away, so widening the range works from either side.
        source.MinDistance = 50f;
        Assert.Equal(50f, source.MinDistance);
        Assert.True(source.MaxDistance >= source.MinDistance);

        source.MaxDistance = 2f;
        Assert.True(source.MaxDistance >= source.MinDistance);

        source.MinDistance = -5f;
        Assert.Equal(0f, source.MinDistance);
    }

    // The inspector writes the backing fields directly and never goes through the properties, so the
    // range has to be straightened out on validate too, device or no device.
    [Fact]
    public void Distances_AreStraightenedOutOnValidate()
    {
        var source = new AudioSource();
        EchoObject echo = Serializer.Serialize(source);
        echo["_minDistance"] = new EchoObject(80f);
        echo["_maxDistance"] = new EchoObject(4f);

        var restored = Serializer.Deserialize<AudioSource>(echo)!;

        Assert.True(restored.MaxDistance >= restored.MinDistance,
            $"min {restored.MinDistance} exceeded max {restored.MaxDistance}");
    }

    // Reading Clip resolves the reference, which loads the asset. Assigning and reading through ClipRef
    // is the way past that, so nothing on this path may end up holding a loaded instance.
    [Fact]
    public void ClipRef_RoundTripsWithoutResolving()
    {
        var source = CreateSource();
        var reference = new AssetRef<AudioClip>(Guid.NewGuid());

        source.ClipRef = reference;

        Assert.Equal(reference.AssetID, source.ClipRef.AssetID);

        // ResWeak is whatever the reference already has in hand. Anything here means the assignment or
        // the read went to the database for a guid that belongs to no asset.
        Assert.Null(source.ClipRef.ResWeak);
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
    // disable exactly or the warnings that lean on it are noise. It counts enabled components, not
    // native listeners, which is what makes it answerable in a run with no device at all.
    [Fact]
    public void ListenerCount_TracksEnableAndDisable()
    {
        int before = AudioListener.ActiveCount;

        var scene = CreateScene(enable: true);
        var go = CreateGameObject("Ears");
        var listener = go.AddComponent<AudioListener>();
        scene.Add(go);

        Assert.Equal(before + 1, AudioListener.ActiveCount);

        listener.Enabled = false;
        Assert.Equal(before, AudioListener.ActiveCount);

        listener.Enabled = true;
        Assert.Equal(before + 1, AudioListener.ActiveCount);

        // Destroying an enabled listener has to give its count back the same way disabling one does.
        go.Destroy();
        EngineObject.ProcessDestroyed();

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

    // A cap of zero would mean a source that can never play a one shot, and the acquire path would
    // have to guess what was meant. One is the floor.
    [Theory]
    [InlineData(0)]
    [InlineData(-4)]
    public void MaxOneShotVoices_NeverDropsBelowOne(int requested)
    {
        var source = CreateSource();

        source.MaxOneShotVoices = requested;

        Assert.Equal(1, source.MaxOneShotVoices);
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

    // Components subscribe to the device closing so they can let their native objects go while those
    // objects still answer. A missed unsubscribe would keep every AudioSource that has ever been
    // enabled alive on a static event for the life of the process.
    [Fact]
    public void Components_DoNotOutliveThemselvesOnTheDeviceClosingEvent()
    {
        int before = DeviceClosingSubscribers();

        var scene = CreateScene(enable: true);
        var go = CreateGameObject("Speaker");
        go.AddComponent<AudioSource>();
        go.AddComponent<AudioListener>();
        scene.Add(go);

        Assert.Equal(before + 2, DeviceClosingSubscribers());

        go.Destroy();
        EngineObject.ProcessDestroyed();

        Assert.Equal(before, DeviceClosingSubscribers());
    }

    private static int DeviceClosingSubscribers()
    {
        var field = typeof(AudioContext).GetField("DeviceClosing",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

        return (field?.GetValue(null) as Action)?.GetInvocationList().Length ?? 0;
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

    // Pausing play mode stops every component updating but says nothing to the audio engine, so the
    // music used to carry on over a frozen game. The game loop drives the pause half of this, and
    // game code owns the other half, and neither may clear the other's suspension.
    [Fact]
    public void Suspension_TracksItsTwoReasonsSeparately()
    {
        Assert.False(AudioContext.IsSuspended);

        try
        {
            AudioContext.Suspended = true;
            Assert.True(AudioContext.IsSuspended);

            // The editor resuming must not undo a suspension the game asked for.
            AudioContext.SuspendedByPause = true;
            AudioContext.SuspendedByPause = false;
            Assert.True(AudioContext.IsSuspended);

            AudioContext.Suspended = false;
            Assert.False(AudioContext.IsSuspended);

            // And the same the other way round.
            AudioContext.SuspendedByPause = true;
            Assert.True(AudioContext.IsSuspended);
            AudioContext.Suspended = false;
            Assert.True(AudioContext.IsSuspended);
        }
        finally
        {
            AudioContext.Suspended = false;
            AudioContext.SuspendedByPause = false;
        }

        Assert.False(AudioContext.IsSuspended);
    }

    // A headless run has no device to stop, and a suspension asked for before one opens has to be
    // remembered rather than thrown away, since that is what applying project settings looks like.
    [Fact]
    public void Suspension_WithoutADevice_IsRememberedRatherThanThrowing()
    {
        Assert.False(AudioContext.IsInitialized);

        try
        {
            AudioContext.Suspended = true;
            Assert.True(AudioContext.Suspended);
        }
        finally
        {
            AudioContext.Suspended = false;
        }
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
