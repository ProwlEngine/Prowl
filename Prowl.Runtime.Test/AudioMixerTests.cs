// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Runtime.Audio;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// The mixer is pure data until a device opens, so everything except the native routing is testable:
/// the group tree, the decibel conversion, and the identities that saved references are keyed on.
/// </summary>
public class AudioMixerTests
{
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
        sfx.AddEffect(new Audio.Effects.FilterEffect { Type = Audio.Effects.FilterType.Lowpass, Frequency = 3000f });

        var restored = Serializer.Deserialize<AudioMixer>(Serializer.Serialize(mixer))!;

        AudioMixerGroup restoredSfx = restored.FindGroup("SFX");
        Assert.Single(restoredSfx.Effects);

        var filter = Assert.IsType<Audio.Effects.FilterEffect>(restoredSfx.Effects[0]);
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

    private sealed class CountingEffect : Audio.Effects.AudioEffect
    {
        public int Destroyed;

        public override void OnProcess(Audio.Native.NativeArray<float> framesIn, uint frameCountIn, Audio.Native.NativeArray<float> framesOut, ref uint frameCountOut, uint channels) { }
        public override void OnDestroy() => Destroyed++;
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
}
