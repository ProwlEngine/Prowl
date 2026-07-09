using System.IO;

using Prowl.Runtime;
using Prowl.Runtime.Resources;

namespace Prowl.Editor.Importers;

/// <summary>
/// Imports audio files (.wav, .mp3, .ogg, .flac) into AudioClip assets.
/// </summary>
[ImporterFor(".wav", ".mp3", ".ogg", ".flac")]
public class AudioImporter : AssetImporter
{
    public override int Version => 1;

    public override bool Import(ImportContext ctx)
    {
        try
        {
            var clip = new AudioClip(ctx.AbsolutePath, streamFromDisk: false);
            clip.ClipName = Path.GetFileNameWithoutExtension(ctx.AbsolutePath);
            ctx.SetMainAsset(clip);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Failed to import audio: {ctx.AbsolutePath}\n{ex.Message}");
            return false;
        }
        return true;
    }
}
