// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

public class VolumeTests : RuntimeTestBase
{
    private readonly Scene _scene;
    private readonly VolumeStack _stack = new();

    public VolumeTests() => _scene = CreateScene(enable: true);

    public override void Dispose()
    {
        _stack.Dispose();
        base.Dispose();
    }

    private static VolumeProfile BloomProfile(float intensity, bool overridden = true)
    {
        var profile = new VolumeProfile();
        profile.Add<Bloom>().Intensity = new VolumeParameter<float>(intensity, overridden);
        return profile;
    }

    private Volume AddVolume(VolumeProfile profile, float priority = 0f, float weight = 1f, bool global = true, Float3 position = default)
    {
        GameObject go = CreateGameObject("Volume");
        go.Transform.Position = position;
        Volume volume = go.AddComponent<Volume>();
        volume.SharedProfile = profile;
        volume.Priority = priority;
        volume.Weight = weight;
        volume.IsGlobal = global;
        _scene.Add(go);
        return volume;
    }

    private void Resolve(Float3 position = default, VolumeProfile? baseProfile = null, LayerMask? mask = null)
        => _stack.Resolve(_scene, position, mask ?? LayerMask.Everything, baseProfile);

    [Fact]
    public void NothingInRange_LeavesEveryEffectOff()
    {
        Resolve();

        Assert.Null(_stack.Get<Bloom>());
        Assert.False(_stack.IsActive<Bloom>());
    }

    [Fact]
    public void BaseProfile_AppliesOnlyItsOverriddenFields()
    {
        VolumeProfile profile = BloomProfile(2f);
        profile.Get<Bloom>()!.Threshold.Value = 5f;

        Resolve(baseProfile: profile);

        Bloom bloom = _stack.Get<Bloom>()!;
        Assert.Equal(2f, bloom.Intensity.Value);
        Assert.Equal(1.2f, bloom.Threshold.Value);
        Assert.True(bloom.Intensity.Override);
        Assert.False(bloom.Threshold.Override);
        Assert.True(_stack.IsActive<Bloom>());
    }

    [Fact]
    public void NotOverridden_ChangesNothing()
    {
        AddVolume(BloomProfile(4f, overridden: false));

        Resolve();

        Assert.Equal(0f, _stack.Get<Bloom>()!.Intensity.Value);
        Assert.False(_stack.IsActive<Bloom>());
    }

    [Theory]
    [InlineData(1f, 2f, 3f)]
    [InlineData(2f, 1f, 1f)]
    public void HigherPriority_AppliesLast(float firstPriority, float secondPriority, float expected)
    {
        AddVolume(BloomProfile(1f), firstPriority);
        AddVolume(BloomProfile(3f), secondPriority);

        Resolve();

        Assert.Equal(expected, _stack.Get<Bloom>()!.Intensity.Value);
    }

    [Fact]
    public void Weight_LerpsFromWhatIsUnder()
    {
        AddVolume(BloomProfile(4f), weight: 0.5f);

        Resolve(baseProfile: BloomProfile(2f));

        Assert.Equal(3f, _stack.Get<Bloom>()!.Intensity.Value, 4);
    }

    [Fact]
    public void LocalVolume_FadesOverItsBlendDistance()
    {
        Volume volume = AddVolume(BloomProfile(4f), global: false);
        volume.Size = new Float3(2f, 2f, 2f);
        volume.BlendDistance = 2f;

        Resolve(new Float3(0.5f, 0f, 0f));
        Assert.Equal(4f, _stack.Get<Bloom>()!.Intensity.Value, 4);

        Resolve(new Float3(2f, 0f, 0f));
        Assert.Equal(2f, _stack.Get<Bloom>()!.Intensity.Value, 4);

        Resolve(new Float3(5f, 0f, 0f));
        Assert.Equal(0f, _stack.Get<Bloom>()!.Intensity.Value);
    }

    [Fact]
    public void VolumeOnAnotherLayer_IsIgnored()
    {
        Volume volume = AddVolume(BloomProfile(4f));
        volume.GameObject.LayerIndex = 3;
        LayerMask mask = LayerMask.Everything;
        mask.RemoveLayer(3);

        Resolve(mask: mask);

        Assert.Null(_stack.Get<Bloom>());
    }

    [Fact]
    public void InactiveComponent_DoesNotBlend()
    {
        VolumeProfile profile = BloomProfile(4f);
        profile.Get<Bloom>()!.Active = false;
        AddVolume(profile);

        Resolve();

        Assert.False(_stack.IsActive<Bloom>());
    }

    [Theory]
    [InlineData(0.4f, TonemappingMode.None)]
    [InlineData(0.6f, TonemappingMode.AgX)]
    public void Enums_SwitchAtHalfway(float weight, TonemappingMode expected)
    {
        var profile = new VolumeProfile();
        profile.Add<Tonemapping>().Mode.Set(TonemappingMode.AgX);
        AddVolume(profile, weight: weight);

        Resolve();

        Assert.Equal(expected, _stack.Get<Tonemapping>()!.Mode.Value);
    }

    [Fact]
    public void Colors_Lerp()
    {
        var profile = new VolumeProfile();
        profile.Add<ColorGrading>().Lift.Set(new Color(1f, 0.5f, 0f, 0f));
        AddVolume(profile, weight: 0.5f);

        Resolve();

        Color lift = _stack.Get<ColorGrading>()!.Lift.Value;
        Assert.Equal(0.5f, lift.R, 4);
        Assert.Equal(0.25f, lift.G, 4);
    }

    [Fact]
    public void TurningOff_IsHeldForAWhile()
    {
        Volume volume = AddVolume(BloomProfile(1f));
        Resolve();
        Assert.True(_stack.IsActive<Bloom>());

        volume.Weight = 0f;
        for (int i = 0; i < VolumeStack.DeactivationFrames; i++)
        {
            Resolve();
            Assert.True(_stack.IsActive<Bloom>());
        }
        Assert.Equal(0f, _stack.Get<Bloom>()!.Intensity.Value);

        Resolve();
        Assert.False(_stack.IsActive<Bloom>());

        volume.Weight = 1f;
        Resolve();
        Assert.True(_stack.IsActive<Bloom>());
    }

    [Fact]
    public void DisabledVolume_DoesNotApply()
    {
        Volume volume = AddVolume(BloomProfile(4f));
        volume.Enabled = false;

        Resolve();

        Assert.Null(_stack.Get<Bloom>());
    }

    [Fact]
    public void ProfileWrites_GoToAnInstance()
    {
        VolumeProfile shared = BloomProfile(1f);
        Volume volume = AddVolume(shared);

        volume.Profile.Get<Bloom>()!.Intensity.Value = 5f;
        Resolve();

        Assert.True(volume.HasInstantiatedProfile);
        Assert.Equal(1f, shared.Get<Bloom>()!.Intensity.Value);
        Assert.Equal(5f, _stack.Get<Bloom>()!.Intensity.Value);
    }

    [Fact]
    public void Profile_HoldsOneComponentPerType()
    {
        var profile = new VolumeProfile();
        Bloom first = profile.Add<Bloom>();

        Assert.Same(first, profile.Add<Bloom>());
        Assert.Single(profile.Components);
        Assert.True(profile.Remove<Bloom>());
        Assert.False(profile.Has<Bloom>());
    }
}
