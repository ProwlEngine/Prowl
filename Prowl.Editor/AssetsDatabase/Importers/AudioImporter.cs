using Prowl.Runtime;
using Prowl.Runtime.Resources;

namespace Prowl.Editor.Importers;

/// <summary>
/// Imports audio files (.wav, .mp3, .ogg, .flac) into AudioClip assets.
/// </summary>
[ImporterFor(".wav", ".mp3", ".ogg", ".flac")]
public class AudioImporter : AssetImporter
{
    public override int Version => 2;

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

            var clip = new AudioClip(data);
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
}
