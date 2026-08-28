// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Editor.Importers;
using Prowl.Runtime;
using Prowl.Runtime.Audio;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>
/// Audio that has to go through the asset database: importing clips with their settings, and mixers
/// whose groups are referenced individually by the sources that feed them.
/// </summary>
/// <remarks>
/// The importer decodes for real through the native decoder here, which needs no output device, so
/// these assert what a clip actually came out as rather than what the settings asked for. The runtime
/// side of audio lives in Prowl.Runtime.Test's AudioTests; this file is only what needs an editor.
/// </remarks>
public class AudioAssetTests : EditorTestHarness
{
    private const int SourceRate = 44100;

    #region Helpers

    private const float Left = 0.5f;
    private const float Right = 0.25f;

    /// <summary>
    /// A real stereo WAV so the importer has something it can decode. The two channels hold different
    /// constants, which is what lets a conversion be checked for having kept the audio and kept it the
    /// right way round rather than only for having the expected shape.
    /// </summary>
    private static byte[] StereoWav(int frames = 512, int sampleRate = SourceRate)
    {
        var samples = new float[frames * 2];

        for (int i = 0; i < frames; i++)
        {
            samples[i * 2] = Left;
            samples[i * 2 + 1] = Right;
        }

        using var source = AudioClip.Create("Source", samples, 2, sampleRate);
        return source.GetEncodedData();
    }

    private Guid ImportBytes(string relativePath, byte[] data)
    {
        string abs = AssetAbsolutePath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
        File.WriteAllBytes(abs, data);
        return Assets.ImportFile(relativePath);
    }

    /// <summary>Edits an asset's import settings on disk and reimports it.</summary>
    private AudioClip Reimport(Guid guid, string relativePath, Action<EchoObject> configure)
    {
        string metaPath = MetaFile.GetMetaPath(AssetAbsolutePath(relativePath));
        MetaFileData meta = MetaFile.Read(metaPath);

        meta.Settings ??= EchoObject.NewCompound();
        configure(meta.Settings);

        MetaFile.Write(metaPath, meta);
        Assets.Reimport(guid);

        return (AudioClip)Assets.Get(guid)!;
    }

    private Guid CreateMixer(AudioMixer mixer, string relativePath = "Game.audiomixer")
    {
        string abs = AssetAbsolutePath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
        File.WriteAllText(abs, SerializeAsset(mixer));
        return Assets.ImportFile(relativePath);
    }

    /// <summary>Serializes an asset the way its file is written: AssetID cleared so the whole object
    /// is emitted rather than an $assetId reference back to itself.</summary>
    private static string SerializeAsset(EngineObject asset)
    {
        Guid saved = asset.AssetID;
        asset.AssetID = Guid.Empty;
        try { return Serializer.Serialize(typeof(object), asset).WriteToString(); }
        finally { asset.AssetID = saved; }
    }

    #endregion

    #region Clip import

    // The importer used to hand the clip the absolute path it was imported from. That path is what
    // got cached and shipped, so a built game resolved a path from the developer's machine and played
    // nothing.
    [Fact]
    public void ImportedClip_CarriesItsData_AndNoSourcePath()
    {
        byte[] source = StereoWav();
        Guid guid = ImportBytes("Beep.wav", source);

        var clip = Assets.Get(guid) as AudioClip;

        Assert.NotNull(clip);
        Assert.NotEqual(IntPtr.Zero, clip!.Handle);
        Assert.Equal((ulong)source.Length, clip.DataSize);
        Assert.Equal(string.Empty, clip.FilePath);
    }

    // The build ships the serialized asset, so the bytes have to be in the serialized form. If this
    // regresses to a file path, audio is silent in every build and only in builds.
    [Fact]
    public void ImportedClip_SerializesItsData_NotAPath()
    {
        byte[] source = StereoWav();
        Guid guid = ImportBytes("Music.wav", source);

        var clip = Assets.Get(guid) as AudioClip;
        Assert.NotNull(clip);

        EchoObject echo = EchoObject.ReadFromString(SerializeAsset(clip!));

        Assert.False(echo.Get("IsFileBased")!.BoolValue);
        Assert.Equal(string.Empty, echo.Get("FilePath")!.StringValue);
        Assert.Equal(source, echo.Get("AudioData")!.ByteArrayValue);
    }

    [Fact]
    public void ByDefault_TheEncodedFileIsStoredAsItIs()
    {
        byte[] source = StereoWav();
        Guid guid = ImportBytes("AsIs.wav", source);

        var clip = Assets.Get(guid) as AudioClip;

        Assert.NotNull(clip);
        Assert.Equal((ulong)source.Length, clip!.DataSize);
        Assert.Equal(2, clip.Channels);
        Assert.Equal(SourceRate, clip.SampleRate);
    }

    [Fact]
    public void ForceMono_CollapsesTheClipToOneChannel()
    {
        Guid guid = ImportBytes("Mono.wav", StereoWav());

        AudioClip clip = Reimport(guid, "Mono.wav", s => s[AudioImportKeys.ForceMono] = new EchoObject(true));

        Assert.Equal(1, clip.Channels);
        Assert.Equal(SourceRate, clip.SampleRate);

        // Same amount of time, half the samples.
        Assert.Equal(512ul, clip.SampleCount);

        // The channels are mixed together rather than one of them being thrown away. Only the audio
        // says which of those happened, and dropping a channel is silence for anything panned away
        // from the one that was kept.
        float[] samples = clip.GetSampleData();

        Assert.Equal(512, samples.Length);
        foreach (float sample in samples)
            Assert.Equal((Left + Right) / 2f, sample, 3);
    }

    [Fact]
    public void SampleRateOverride_ResamplesTheClip()
    {
        Guid guid = ImportBytes("Resampled.wav", StereoWav());

        AudioClip clip = Reimport(guid, "Resampled.wav", s => s[AudioImportKeys.SampleRateOverride] = new EchoObject(22050));

        Assert.Equal(22050, clip.SampleRate);
        Assert.Equal(2, clip.Channels);

        // Half the rate over the same duration, so about half the frames.
        Assert.InRange(clip.SampleCount, 250ul, 262ul);
    }

    // The stored bytes change shape when decompressing, but what comes back out has to be the same
    // audio at the same format.
    [Fact]
    public void DecompressOnLoad_KeepsTheFormat()
    {
        Guid guid = ImportBytes("Decompressed.wav", StereoWav());

        AudioClip clip = Reimport(guid, "Decompressed.wav",
            s => s[AudioImportKeys.LoadType] = new EchoObject((int)AudioLoadType.DecompressOnLoad));

        Assert.Equal(2, clip.Channels);
        Assert.Equal(SourceRate, clip.SampleRate);
        Assert.Equal(512ul, clip.SampleCount);

        // Decode and re-encode is where a channel swap or a scaling mistake would land, and the format
        // alone cannot see either.
        float[] samples = clip.GetSampleData();

        for (int frame = 0; frame < 512; frame++)
        {
            Assert.Equal(Left, samples[frame * 2], 3);
            Assert.Equal(Right, samples[frame * 2 + 1], 3);
        }
    }

    // Decompressing stores 16 bit rather than the decoder's own float output, which would double the
    // size of every decompressed clip for a difference below anything a game source gets near.
    [Fact]
    public void DecompressOnLoad_StoresSixteenBit()
    {
        byte[] source = StereoWav();
        Guid guid = ImportBytes("Sized.wav", source);

        AudioClip clip = Reimport(guid, "Sized.wav",
            s => s[AudioImportKeys.LoadType] = new EchoObject((int)AudioLoadType.DecompressOnLoad));

        // 512 stereo frames at two bytes a sample, plus a 44 byte header.
        Assert.Equal(512ul * 2 * 2 + 44, clip.DataSize);

        // And the source it came from was the float equivalent, so this really is smaller.
        Assert.True(clip.DataSize < (ulong)source.Length);
    }

    // A file the decoder cannot read must still import as a clip rather than failing the whole scan.
    [Fact]
    public void AnUndecodableFile_StillImports()
    {
        Guid guid = ImportBytes("Garbage.wav", [1, 2, 3, 4, 5, 6, 7, 8]);

        AudioClip clip = Reimport(guid, "Garbage.wav", s => s[AudioImportKeys.ForceMono] = new EchoObject(true));

        Assert.NotNull(clip);
        Assert.Equal(8ul, clip.DataSize);
        Assert.Equal(0, clip.Channels);
    }

    #endregion

    #region Mixer assets

    // Assets are shared instances, so a script setting a mixer group's volume at run time writes to
    // the project's copy of it. Dropping the loaded instances is what stops that outliving the play
    // session, and it is what both sides of a play mode transition do.
    [Fact]
    public void UnloadAll_ThrowsAwayRuntimeChangesToAnAsset()
    {
        var mixer = new AudioMixer();
        mixer.AddGroup("Music");

        Guid guid = CreateMixer(mixer);

        var loaded = Assets.Get(guid) as AudioMixer;
        Assert.NotNull(loaded);

        AudioMixerGroup music = loaded!.FindGroup("Music")!;
        Assert.Equal(0f, music.VolumeDB);

        // What a gameplay script does to a mixer it was handed.
        music.VolumeDB = -24f;
        Assert.Equal(-24f, loaded.FindGroup("Music")!.VolumeDB);

        Assert.True(Assets.UnloadAll() > 0);

        var reloaded = Assets.Get(guid) as AudioMixer;

        Assert.NotNull(reloaded);
        Assert.NotSame(loaded, reloaded);
        Assert.Equal(0f, reloaded!.FindGroup("Music")!.VolumeDB);
    }

    // A pinned asset is pinned because something needs that exact instance to survive, which is the
    // one thing the drop has to respect.
    [Fact]
    public void UnloadAll_LeavesPinnedAssetsAlone()
    {
        Guid pinned = CreateMixer(new AudioMixer(), "Pinned.audiomixer");
        Guid ordinary = CreateMixer(new AudioMixer(), "Ordinary.audiomixer");

        var pinnedAsset = Assets.Get(pinned) as AudioMixer;
        var ordinaryAsset = Assets.Get(ordinary) as AudioMixer;

        Assert.NotNull(pinnedAsset);
        Assert.NotNull(ordinaryAsset);

        AssetDatabase.LockPermanent(pinned);

        try
        {
            Assets.UnloadAll();

            Assert.False(pinnedAsset!.IsDisposed);
            Assert.Same(pinnedAsset, Assets.Get(pinned));

            Assert.True(ordinaryAsset!.IsDisposed);
            Assert.NotSame(ordinaryAsset, Assets.Get(ordinary));
        }
        finally
        {
            AssetDatabase.Unlock(pinned);
        }
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
        mixer.AddGroup("Music");

        Guid guid = CreateMixer(mixer);
        var imported = (AudioMixer)Assets.Get(guid)!;
        Guid musicId = imported.FindGroup("Music").AssetID;

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

    #endregion

    #region Scene dependencies

    // AudioSource used to serialize the resolved clip inline instead of its AssetRef field, so the
    // clip's GUID never reached the dependency set and a build shipped a scene with no audio.
    [Fact]
    public void Scene_TracksAudioSourceClipDependency()
    {
        var clip = new AudioClip([1, 2, 3, 4]) { AssetID = Guid.NewGuid() };

        var go = new GameObject("Holder");
        go.AddComponent<AudioSource>().Clip = clip;

        var scene = new Runtime.Resources.Scene();
        scene.Add(go);
        Guid sceneGuid = CreateSceneAsset(scene, "Clipped.scene");

        var entry = Assets.GetEntry(sceneGuid);
        Assert.NotNull(entry);
        Assert.Contains(clip.AssetID, entry!.Dependencies);
    }

    // Same for the bus a source feeds, or the mixer is not collected into a build and the reference
    // resolves to nothing on the player's machine.
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

    #endregion
}
