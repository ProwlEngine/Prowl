using Prowl.Echo;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

namespace Prowl.Editor.Importers;

/// <summary>How an imported clip's audio is stored.</summary>
public enum AudioLoadType
{
    /// <summary>Store the file as it was encoded and decode it during playback. Smallest asset.</summary>
    CompressedInMemory = 0,

    /// <summary>Decode at import and store uncompressed. Larger asset, no decoding while playing.</summary>
    DecompressOnLoad = 1,
}

/// <summary>
/// Imports audio files (.wav, .mp3, .ogg, .flac) into AudioClip assets.
/// </summary>
public static class AudioImportKeys
{
    public const string LoadType = "loadType";
    public const string ForceMono = "forceMono";
    public const string SampleRateOverride = "sampleRateOverride";
}

[ImporterFor(".wav", ".mp3", ".ogg", ".flac")]
public class AudioImporter : AssetImporter
{
    public override int Version => 3;

    public override EchoObject? DefaultSettings()
    {
        var settings = EchoObject.NewCompound();
        settings[AudioImportKeys.LoadType] = new EchoObject((int)AudioLoadType.CompressedInMemory);
        settings[AudioImportKeys.ForceMono] = new EchoObject(false);
        settings[AudioImportKeys.SampleRateOverride] = new EchoObject(0);
        return settings;
    }

    public override bool Import(ImportContext ctx)
    {
        try
        {
            // The encoded bytes are read into the clip rather than pointing it at the source file:
            // a path based clip serializes the path it was imported from, which is an absolute path
            // on this machine and resolves to nothing in a built game.
            byte[] data = System.IO.File.ReadAllBytes(ctx.AbsolutePath);

            if (data.Length == 0)
            {
                Debug.LogError($"Failed to import audio, the file is empty: {ctx.AbsolutePath}");
                return false;
            }

            var loadType = (AudioLoadType)ReadInt(ctx, AudioImportKeys.LoadType, (int)AudioLoadType.CompressedInMemory);
            bool forceMono = ReadBool(ctx, AudioImportKeys.ForceMono, false);
            int sampleRateOverride = ReadInt(ctx, AudioImportKeys.SampleRateOverride, 0);

            var clip = new AudioClip(data);

            // Converting the channel count or the rate means decoding, and once decoded there is no
            // encoder to put it back into its original format, so any conversion stores WAVE.
            bool convert = forceMono || sampleRateOverride > 0;

            if (convert || loadType == AudioLoadType.DecompressOnLoad)
            {
                byte[] decoded = clip.DecodeToWave(forceMono ? 1u : 0u, (uint)System.Math.Max(0, sampleRateOverride));

                if (decoded.Length > 0)
                {
                    clip.Dispose();
                    clip = new AudioClip(decoded);
                }
                else
                {
                    Debug.LogWarning($"Could not decode '{ctx.FileName}', importing it as it was encoded instead.");
                }
            }

            clip.ClipName = ctx.FileName;
            clip.Name = ctx.FileName;
            ctx.SetMainAsset(clip);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Failed to import audio: {ctx.AbsolutePath}\n{ex.Message}");
            return false;
        }
        return true;
    }

    private static int ReadInt(ImportContext ctx, string key, int fallback)
        => ctx.Settings != null && ctx.Settings.TryGet(key, out EchoObject? value) && value != null ? value.IntValue : fallback;

    private static bool ReadBool(ImportContext ctx, string key, bool fallback)
        => ctx.Settings != null && ctx.Settings.TryGet(key, out EchoObject? value) && value != null ? value.BoolValue : fallback;
}
