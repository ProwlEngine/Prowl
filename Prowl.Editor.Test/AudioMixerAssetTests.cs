// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Runtime;
using Prowl.Runtime.Audio;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>
/// The mixer only earns its keep if a source can reference one bus out of it and that reference
/// survives a round trip through the asset database, including a reimport that reorders the buses.
/// </summary>
public class AudioMixerAssetTests : EditorTestHarness
{
    private Guid CreateMixer(AudioMixer mixer, string relativePath = "Game.audiomixer")
    {
        string abs = AssetAbsolutePath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
        File.WriteAllText(abs, Serializer.Serialize(typeof(object), mixer).WriteToString());
        return Assets.ImportFile(relativePath);
    }

    [Fact]
    public void ImportedMixer_RegistersEachGroupAsASubAsset()
    {
        var mixer = new AudioMixer();
        mixer.AddGroup("Music");
        mixer.AddGroup("SFX");

        Guid guid = CreateMixer(mixer);

        var imported = Assets.Get(guid) as AudioMixer;

        Assert.NotNull(imported);
        Assert.Equal(3, imported!.Groups.Count);

        foreach (AudioMixerGroup group in imported.Groups)
            Assert.NotEqual(Guid.Empty, group.AssetID);
    }

    // Every saved OutputGroup is keyed on the sub-asset GUID, so inserting a bus above another one
    // must not hand its GUID to a different group. A positional identity would do exactly that.
    [Fact]
    public void GroupGuids_SurviveInsertingAnotherGroup()
    {
        var mixer = new AudioMixer();
        AudioMixerGroup music = mixer.AddGroup("Music");

        Guid guid = CreateMixer(mixer);
        var imported = (AudioMixer)Assets.Get(guid)!;
        Guid musicId = imported.FindGroup("Music").AssetID;

        // Insert a bus ahead of Music in the list, then reimport the asset from disk.
        imported.AddGroup("Ambience");
        File.WriteAllText(AssetAbsolutePath("Game.audiomixer"), SerializeAsset(imported));
        Assets.Reimport(guid);

        var reimported = (AudioMixer)Assets.Get(guid)!;

        Assert.Equal(musicId, reimported.FindGroup("Music").AssetID);
    }

    // Renaming a bus is an everyday edit and must not repoint anything at it.
    [Fact]
    public void GroupGuids_SurviveARename()
    {
        var mixer = new AudioMixer();
        mixer.AddGroup("Music");

        Guid guid = CreateMixer(mixer);
        var imported = (AudioMixer)Assets.Get(guid)!;
        Guid musicId = imported.FindGroup("Music").AssetID;

        imported.FindGroup("Music").GroupName = "Soundtrack";
        File.WriteAllText(AssetAbsolutePath("Game.audiomixer"), SerializeAsset(imported));
        Assets.Reimport(guid);

        var reimported = (AudioMixer)Assets.Get(guid)!;

        Assert.Equal(musicId, reimported.FindGroup("Soundtrack").AssetID);
    }

    // A group referenced by a source ships as its own cache file, so it can be loaded with no mixer
    // around it. It still has to know what it feeds into, or bus nesting flattens in a build.
    [Fact]
    public void GroupLoadedOnItsOwn_StillResolvesItsParent()
    {
        var mixer = new AudioMixer();
        AudioMixerGroup music = mixer.AddGroup("Music");
        mixer.AddGroup("Stingers", music);

        Guid guid = CreateMixer(mixer);
        var imported = (AudioMixer)Assets.Get(guid)!;
        Guid stingersId = imported.FindGroup("Stingers").AssetID;

        ReopenDatabase();

        // Resolved by GUID alone, the way an AudioSource's OutputGroup resolves it.
        var stingers = Assets.Get(stingersId) as AudioMixerGroup;

        Assert.NotNull(stingers);
        Assert.NotNull(stingers!.Mixer);
        Assert.Equal("Music", stingers.Parent?.GroupName);
    }

    // An AudioSource pointing at a bus has to record that dependency, or the mixer is not collected
    // into a build and the reference resolves to nothing on the player's machine.
    [Fact]
    public void Scene_TracksAudioSourceOutputGroupDependency()
    {
        var mixer = new AudioMixer();
        mixer.AddGroup("Music");

        Guid mixerGuid = CreateMixer(mixer);
        var imported = (AudioMixer)Assets.Get(mixerGuid)!;
        AudioMixerGroup music = imported.FindGroup("Music");

        var scene = new Runtime.Resources.Scene();
        var go = new GameObject("Speaker");
        go.AddComponent<AudioSource>().OutputGroup = music;
        scene.Add(go);

        Guid sceneGuid = CreateSceneAsset(scene, "Mixed.scene");

        var entry = Assets.GetEntry(sceneGuid);
        Assert.NotNull(entry);
        Assert.Contains(music.AssetID, entry!.Dependencies);
    }

    private static string SerializeAsset(EngineObject asset)
    {
        Guid saved = asset.AssetID;
        asset.AssetID = Guid.Empty;
        try { return Serializer.Serialize(typeof(object), asset).WriteToString(); }
        finally { asset.AssetID = saved; }
    }
}
