using System.IO;

using Prowl.Echo;
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

    public override ImportResult Import(string absolutePath, EchoObject? settings)
    {
        var result = new ImportResult();
        try
        {
            var clip = new AudioClip(absolutePath, streamFromDisk: false);
            clip.Name = Path.GetFileNameWithoutExtension(absolutePath);
            result.MainAsset = clip;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Failed to import audio: {absolutePath}\n{ex.Message}");
        }
        return result;
    }
}
