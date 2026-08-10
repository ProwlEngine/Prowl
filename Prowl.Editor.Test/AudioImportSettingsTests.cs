// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Editor.Importers;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>
/// Audio import settings. The importer decodes for real through the native decoder here, so these
/// assert what the clip actually came out as rather than what the settings asked for.
/// </summary>
public class AudioImportSettingsTests : EditorTestHarness
{
    private const int SourceRate = 44100;

    /// <summary>A real stereo WAV so the importer has something it can decode.</summary>
    private static byte[] StereoWav(int frames = 512, int sampleRate = SourceRate)
    {
        var samples = new float[frames * 2];

        for (int i = 0; i < frames; i++)
        {
            samples[i * 2] = 0.5f;
            samples[i * 2 + 1] = -0.5f;
        }

        using var source = AudioClip.Create("Source", samples, 2, sampleRate);
        return source.GetEncodedData();
    }

    private Guid Import(string relativePath, byte[] data)
    {
        string abs = AssetAbsolutePath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
        File.WriteAllBytes(abs, data);
        return Assets.ImportFile(relativePath);
    }

    /// <summary>Edits the asset's import settings on disk and reimports it.</summary>
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

    [Fact]
    public void ByDefault_TheEncodedFileIsStoredAsItIs()
    {
        byte[] source = StereoWav();
        Guid guid = Import("Beep.wav", source);

        var clip = Assets.Get(guid) as AudioClip;

        Assert.NotNull(clip);
        Assert.Equal((ulong)source.Length, clip!.DataSize);
        Assert.Equal(2, clip.Channels);
        Assert.Equal(SourceRate, clip.SampleRate);
    }

    [Fact]
    public void ForceMono_CollapsesTheClipToOneChannel()
    {
        byte[] source = StereoWav();
        Guid guid = Import("Mono.wav", source);

        AudioClip clip = Reimport(guid, "Mono.wav", s => s[AudioImportKeys.ForceMono] = new EchoObject(true));

        Assert.Equal(1, clip.Channels);
        Assert.Equal(SourceRate, clip.SampleRate);

        // Same amount of time, half the samples.
        Assert.Equal(512ul, clip.SampleCount);
    }

    [Fact]
    public void SampleRateOverride_ResamplesTheClip()
    {
        byte[] source = StereoWav();
        Guid guid = Import("Resampled.wav", source);

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
        byte[] source = StereoWav();
        Guid guid = Import("Decompressed.wav", source);

        AudioClip clip = Reimport(guid, "Decompressed.wav",
            s => s[AudioImportKeys.LoadType] = new EchoObject((int)AudioLoadType.DecompressOnLoad));

        Assert.Equal(2, clip.Channels);
        Assert.Equal(SourceRate, clip.SampleRate);
        Assert.Equal(512ul, clip.SampleCount);
    }

    // A file the decoder cannot read must still import as a clip rather than failing the whole scan.
    [Fact]
    public void AnUndecodableFile_StillImports()
    {
        Guid guid = Import("Garbage.wav", [1, 2, 3, 4, 5, 6, 7, 8]);

        AudioClip clip = Reimport(guid, "Garbage.wav", s => s[AudioImportKeys.ForceMono] = new EchoObject(true));

        Assert.NotNull(clip);
        Assert.Equal(8ul, clip.DataSize);
        Assert.Equal(0, clip.Channels);
    }
}
