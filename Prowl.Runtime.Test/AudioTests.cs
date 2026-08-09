// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Runtime.Audio;
using Prowl.Runtime.Audio.Native;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>Tests for audio buffer/data handling (no native device required).</summary>
public class AudioTests
{
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
        source.AddEffect(new Audio.Effects.FilterEffect { Type = Audio.Effects.FilterType.Highpass, Frequency = 800f, Q = 1.5f });
        source.AddEffect(new Audio.Effects.DistortionEffect { Drive = 3f, Blend = 0.25f });

        var restored = Serializer.Deserialize<AudioSource>(Serializer.Serialize(source))!;

        Assert.Equal(2, restored.EffectCount);

        var filter = Assert.IsType<Audio.Effects.FilterEffect>(restored.Effects[0]);
        Assert.Equal(Audio.Effects.FilterType.Highpass, filter.Type);
        Assert.Equal(800f, filter.Frequency);
        Assert.Equal(1.5f, filter.Q);

        var distortion = Assert.IsType<Audio.Effects.DistortionEffect>(restored.Effects[1]);
        Assert.Equal(3f, distortion.Drive);
        Assert.Equal(0.25f, distortion.Blend);

        // Parameters alone are not enough, the DSP state has to be rebuilt on the way in or the
        // effect is inert until something else happens to touch it.
        Assert.True(filter.IsInitialized);
    }

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
}

/// <summary>Audio components in a scene with no audio device, which is every headless run.</summary>
public class AudioComponentTests : RuntimeTestBase
{
    /// <summary>An effect that only records whether the source destroyed it.</summary>
    private sealed class CountingEffect : Audio.Effects.AudioEffect
    {
        public int Destroyed;

        public override void OnProcess(NativeArray<float> framesIn, uint frameCountIn, NativeArray<float> framesOut, ref uint frameCountOut, uint channels) { }
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
}
