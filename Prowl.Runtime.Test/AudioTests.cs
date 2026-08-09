// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Runtime.Audio;
using Prowl.Runtime.Resources;

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
}
